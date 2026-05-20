# CanHub / 外部硬件适配边界

`DiagKit.Dbc` 不直接引用 CanHub 或任何硬件 SDK。上层应用负责硬件接收、发送线程和设备生命周期；DBC 核心只处理：

- 外部帧输入：`DbcFrameView` -> `DbcChannelRuntime.ProcessReceivedFrame(...)`
- 待发送帧输出：`DbcChannelRuntime.PollDueFrames(...)` / `BuildFrameNow(...)` -> `IDbcFrameSink`
- 运行时信号状态：`SetPhysicalValue(...)`、`SetRawValue(...)`、snapshot 与 sample sink

可执行示例见 `tests/DiagKit.Dbc.Tests/ExternalHardwareAdapterExampleTests.cs`。测试里用 `HardwareFrame` 代替真实 CanHub DTO，真实项目只需要把字段名换成 CanHub 的帧类型。

## 初始化

DBC 文档只加载一次，可以被多个通道共享；每路 CAN 创建一个 channel runtime。

```csharp
var document = DbcLoader.LoadFile(dbcPath, DbcLoadOptions.Strict).GetDocumentOrThrow();
var session = DbcRuntimeSession.Create(document);

var can1 = session.CreateChannel("CAN1");
var message = can1.ResolveMessage("CommandStatus");
var speed = can1.ResolveSignal(message, "TargetSpeed");

can1.AddPublishingMessage(message); // 默认使用 DBC GenMsgCycleTime
```

热路径建议提前解析 `MessageHandle` / `SignalHandle`，不要在每帧处理中按字符串查找。

## 接收方向

外部接收循环把硬件帧映射成 `DbcFrameView`，再交给 channel runtime 解码。`DbcFrameView` 是 view 型帧，不复制 payload，适合在硬件回调或已持有 buffer 的同步路径中使用。
下面示例假设适配层已把硬件时间戳转换为 `TimeSpan.Ticks` 单位的 elapsed ticks。

```csharp
static DbcFrameView ToDbcFrameView(HardwareFrame frame)
{
    var dataLength = DbcDlc.ToDataLength(frame.Dlc);
    var identifier = new CanIdentifier(
        frame.ArbitrationId,
        frame.IsExtendedId ? CanIdFormat.Extended : CanIdFormat.Standard);

    var flags = DbcFrameFlags.None;
    if (frame.IsFlexibleDataRate) flags |= DbcFrameFlags.FlexibleDataRate;
    if (frame.IsBitRateSwitch) flags |= DbcFrameFlags.BitRateSwitch;

    return new DbcFrameView(
        identifier,
        frame.Payload.AsSpan(0, dataLength),
        flags,
        DbcTimestamp.FromElapsed(TimeSpan.FromTicks(frame.ElapsedTimeTicks)));
}

void OnHardwareFrame(HardwareFrame frame)
{
    var channel = GetRuntimeChannel(frame.ChannelIndex);
    channel.ProcessReceivedFrame(ToDbcFrameView(frame), signalSampleSink);
}
```

如果硬件 buffer 在回调返回后会复用，`ProcessReceivedFrame` 可以同步完成解码和内部状态复制；不要把 `DbcFrameView` 保存到队列或跨异步边界。需要排队时，应在外部复制为自己的硬件 DTO，或使用拥有型 `DbcFrame`。

## 发送方向

周期发送由上层时钟驱动。`PollDueFrames(now, sink)` 只产出到期帧，不 sleep、不创建线程、不调用硬件 SDK。

```csharp
sealed class CanHubTransmitSink : IDbcFrameSink
{
    public void OnFrame(
        CanIdentifier identifier,
        ReadOnlySpan<byte> data,
        DbcFrameFlags flags,
        DbcTimestamp timestamp)
    {
        var frame = new HardwareFrame
        {
            ArbitrationId = identifier.Value,
            IsExtendedId = identifier.IsExtended,
            IsFlexibleDataRate = flags.HasFlag(DbcFrameFlags.FlexibleDataRate),
            IsBitRateSwitch = flags.HasFlag(DbcFrameFlags.BitRateSwitch),
            Dlc = DbcDlc.FromDataLength(data.Length),
            TimestampTicks = timestamp.Ticks,
            Payload = data.ToArray(),
        };

        hardware.Send(frame);
    }
}

var schedulerStart = Stopwatch.GetTimestamp();

void Tick()
{
    var now = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(schedulerStart));

    can1.PollDueFrames(
        now,
        transmitSink);
}
```

`OnFrame` 中的 `data` 只在回调期间有效。如果硬件发送 API 能同步接收 `ReadOnlySpan<byte>`，可以避免复制；如果要异步发送或入队，应复制 payload，或在适配层使用数组池/固定缓冲区复用，避免高频场景持续分配。

手动立即构帧可以使用：

```csharp
var now = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(schedulerStart));
can1.SetPhysicalValue(speed, 12.34, timestamp: now);
can1.BuildFrameNow(message, now, transmitSink);
```

## 并发和时间戳

`DbcDocument` 可以跨通道、跨线程共享。`DbcChannelRuntime` 对偶发并发 public calls 保持内部状态一致；但在高频主机中，同一路 channel 的 `ProcessReceivedFrame`、`SetPhysicalValue`、`PollDueFrames` 仍建议由同一个执行上下文串行调用，以获得更可预测的延迟。如果接收线程和发送调度线程都会触碰同一个 channel，优先由上层通过队列、actor 或锁把调用串行化。

接收帧的 timestamp 应优先使用硬件时间戳；如果硬件只提供墙钟时间，适配层需要明确转换策略。`DbcTimestampKind.MonotonicTicks` 的单位是 `TimeSpan.Ticks` 表示的单调 elapsed 时间，不是原始 Stopwatch 或硬件计数器 ticks。硬件原始计数器应先按频率转换为 `TimeSpan`，再调用 `DbcTimestamp.FromElapsed(...)`。周期调度的 `now` 应使用单调高精度 elapsed 时间，避免系统时间调整导致调度跳变。

`DbcFrame.Data` 和 `MessageSnapshot.Data` 是 `ReadOnlySpan<byte>` 公开只读视图。需要把帧或快照跨异步边界保存、入队或交给只能接收 `byte[]` 的硬件 API 时，应显式复制，例如 `.ToArray()`。

## 波形和历史回放

实时波形与历史 trace 回放使用同一入口：上层把帧按时间顺序转成 `DbcFrameView`，传入 `ProcessReceivedFrame`，再从 `ISignalSampleSink` 收集 `SignalSample`。核心不解析 trace 文件，也不绘制波形。
