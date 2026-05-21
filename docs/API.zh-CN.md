# DiagKit.Dbc API 使用指南

本文面向上层应用集成者，说明如何把 DBC 文档、CAN/CAN FD 帧、运行时状态、周期发送和波形/历史分析串起来。

## 入口选择

| 场景 | 建议入口 | 说明 |
| --- | --- | --- |
| 首次接入、UI、脚本、测试台 | `DbcSimpleRuntime` | 加载 DBC、保留 diagnostics，并提供 `"Message.Signal"` 便捷 API。 |
| 生产运行时状态机 | `DbcRuntimeSession` / `DbcChannelRuntime` | 使用预解析 handle、snapshot、sink 和周期轮询，适合高频实时路径。 |
| 底层工具和元数据处理 | `DbcLoader.LoadDocumentOrThrow`、`DbcDocument`、`DbcCodec` | 直接查看 DBC 元数据，或做无状态 encode/decode。 |

## 5 分钟黄金路径

只解析 DBC 并查看消息/信号：

```csharp
var dbc = DbcSimpleRuntime.LoadFile("vehicle.dbc");
Console.WriteLine(DbcDiagnosticFormatter.FormatGrouped(dbc.LoadResult.Diagnostics));

foreach (var message in dbc.Document.Messages)
{
    Console.WriteLine($"{message.Name}: {message.Signals.Count} signals");
}
```

收到一帧 CAN 后按物理值解码：

```csharp
var values = dbc.ProcessFrame(identifier, payload, timestamp: timestamp);
var speed = values.GetPhysicalValue("VehicleSpeed");
```

设置某个信号并立即构帧发送：

```csharp
dbc.SetPhysicalValue("VehicleStatus.VehicleSpeed", 42.5);
var frame = dbc.BuildFrame("VehicleStatus", timestamp);
```

按 DBC 周期自动发布：

```csharp
var session = DbcRuntimeSession.Create(dbc.Document);
var channel = session.CreateChannel("CAN1");
var clockStart = Stopwatch.GetTimestamp();
var now = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(clockStart));

var report = channel.RegisterCyclicPublishingMessagesFromDbc();
channel.PollDueFrames(now, txSink);
```

接外部硬件适配器时，硬件接收帧转 `DbcFrameView` 后调用 `ProcessReceivedFrame`，发送方向消费 `IDbcFrameSink.OnFrame`。如果硬件 API 异步发送或入队，应复制 `data`。

## 核心对象

`DbcDocument` 是 DBC 元数据模型，加载后可在多个 runtime session 之间共享。它保存：

- `Nodes`: DBC 节点。Node 不是 Message/Signal 容器，而是发送/接收关系实体。
- `Messages`: DBC message。Message 记录 transmitter、payload 长度、CAN identifier、CAN FD flags、cycle time、send type、timeout 等。`SupportsSingleFrameRuntime` 表示该 message 是否可由当前 CAN/CAN FD 单帧 runtime 直接处理。
- `Signals`: Message 持有 signal。Signal 记录 start bit、length、byte order、factor/offset、unit、receivers、multiplexing、timeout 等。
- `Attributes`: DBC 属性定义、默认值和对象赋值会尽量保留，并把常见属性映射为语义字段。
- `EnvironmentVariables`: `EV_` 环境变量元数据。它不会被当作 CAN frame signal 参与编解码。
- `RelationAttributeDefinitions` / `RelationAttributeDefaults` / `RelationAttributes`: `BA_DEF_REL_`、`BA_DEF_DEF_REL_`、`BA_REL_` 的可追溯原始元数据。首版不会猜测复杂关系目标并应用到 message/signal。

只需要 DBC 元数据、暂时不需要 runtime 状态时，可以直接加载文档：

```csharp
var document = DbcLoader.LoadDocumentOrThrow("vehicle.dbc");
```

定位关系建议始终按 Message -> Signal：

```csharp
var message = document.ResolveMessage("VehicleStatus");
var signal = message.ResolveSignal("VehicleSpeed");

var path = SignalPath.Parse("VehicleStatus.VehicleSpeed");
var sameSignal = document.ResolveSignal(path);
```

`SignalPath` 是公共值对象，只表示首版支持的 `Message.Signal` 形式；格式错误可用 `TryParse` 失败关闭。它适合配置文件、脚本和 simple facade，热路径仍建议解析成 handle 后缓存。

运行时热路径建议提前解析 handle，避免每帧字符串查找：

```csharp
var session = DbcRuntimeSession.Create(document);
var channel = session.CreateChannel("CAN1");

var messageHandle = channel.ResolveMessage("VehicleStatus");
var speedHandle = channel.ResolveSignal(messageHandle, "VehicleSpeed");
var sameHandle = channel.ResolveSignal(SignalPath.Parse("VehicleStatus.VehicleSpeed"));
```

如果不想用异常表达缺失对象，可使用 `TryResolveMessage` 和 `TryResolveSignal`。

名称查找默认使用 ordinal 大小写敏感规则。

同一 message 内允许保留同名 signal，以兼容 Vector CANdb++ 可打开但会警告的 DBC。此时 `message.TryResolveSignal(name)` 会返回 `false`，`message.ResolveSignal(name)` 会抛出歧义异常；调用方应使用 `message.FindSignals(name)` 枚举候选。运行时需要可缓存 handle 时，可把具体 `DbcSignal` 对象传给 `channel.ResolveSignal(messageHandle, signal)`，避免按名称静默选错。

## 加载 DBC

Strict 模式适合发布前校验和 CI；Lenient 模式适合导入真实世界 DBC 后查看 diagnostics。

```csharp
var result = DbcLoader.LoadFile("vehicle.dbc", DbcLoadOptions.Strict);
Console.WriteLine(DbcDiagnosticFormatter.Format(result));
Console.WriteLine(DbcDiagnosticFormatter.FormatGrouped(result.Diagnostics));

if (result.HasErrors)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.Code} line {error.LineNumber}: {error.Message}");
    }
}

var document = result.GetDocumentOrThrow();
var sameDocument = DbcLoader.LoadDocumentOrThrow("vehicle.dbc", DbcLoadOptions.Strict);
```

需要按严重级别和错误码做 UI 展示时，可先汇总：

```csharp
var summary = DbcDiagnosticFormatter.Summarize(result.Diagnostics);
foreach (var group in summary.Groups)
{
    Console.WriteLine($"{group.Severity} {group.Code}: {group.Diagnostics.Count}");
}
```

Lenient 模式不会静默吞错。只要发现 unsupported、missing reference 或可恢复结构问题，都应检查 `Diagnostics`。常见第三方兼容包括带空格/连字符的 quoted attribute name、`BA_ "Name"BO_ ...` 这类可无歧义恢复的缺失空格、`BO_TX_BU_` 多发送方、`VAL_`/`VAL_TABLE_` 多行 quoted text，以及 `GenSigStartValue` 的 Vector 常见宽整数/物理初值写法。

```csharp
var result = DbcLoader.LoadFile("vehicle.dbc", DbcLoadOptions.Lenient);
if (!result.Succeeded)
{
    // 上层可以展示 diagnostics，而不是只显示“加载失败”。
    throw new InvalidOperationException(DbcDiagnosticFormatter.Format(result));
}
```

`ThrowIfErrors()` 只在存在 Error 级诊断时抛出异常；warning-only 的 Lenient 加载可以继续使用文档，同时把 warning 展示给用户或写入日志。

`DbcLoadOptions.MaxStatementLength` 默认限制单条 DBC statement 最长 1 MiB。超长 statement 会被跳过并输出 `DBC_STATEMENT_TOO_LONG` 诊断，调用方可在导入受信任的大型供应商文件时显式调高上限。

首版边界是 CAN/CAN FD 单帧 runtime，但 loader 的 DBC 数据库层会尽量保留 Vector/CANdb++ 有明确依据的元数据。`BO_` payload 长度超过 64 字节时，Lenient 模式会保留 message/signal 并输出 `DBC_MESSAGE_RUNTIME_UNSUPPORTED` warning；这些 message 可用于查看数据库元数据，但 `DbcChannelRuntime` 不会为它们解析 runtime handle，`Dlc` 也不会伪造成 CAN FD DLC。内容冲突的重复 value table 在 Lenient 下保留第一份并 warning；Strict 仍把重复 value table 作为 Error。无法明确归属到唯一 signal 的按名称元数据不会被猜测应用到任意一个同名 signal。当前版本不提供 DBC 导出。

## CAN ID 与 DBC 原始 ID

公共运行时 API 使用 `CanIdentifier`：

```csharp
var id = new CanIdentifier(0x18FF50E5, CanIdFormat.Extended);
var message = document.ResolveMessage(id);
```

DBC 文件中的 `BO_` 数字会先保存为 `DbcRawMessageId`，再转换为 runtime `CanIdentifier`。不要把 DBC raw id 当作硬件层 CAN ID 直接使用；扩展帧标志和 normalized CAN ID 需要分清。
`CanIdentifier` 支持 `==`、`!=`、`<`、`>`、`<=`、`>=` 和 `IComparable<CanIdentifier>`，排序时先按 normalized identifier 值比较，再按 frame format 比较。

## 信号编解码

如果只需要无状态编解码，可使用 `DbcCodec` 或 `DbcMessage`/`DbcSignal` 直接处理 payload：

```csharp
if (!message.SupportsSingleFrameRuntime)
{
    throw new NotSupportedException("当前 DiagKit.Dbc runtime 只处理 CAN/CAN FD 单帧 message。");
}

Span<byte> payload = stackalloc byte[message.DataLength];

var write = DbcCodec.WritePhysical(payload, signal, 42.5, SignalWritePolicy.Strict);
if (!write.Succeeded)
{
    throw new InvalidOperationException(write.Diagnostic);
}

var physical = DbcCodec.DecodePhysical(payload, signal);
```

`DbcMessage.Decode` 与 `DbcCodec.DecodeMessage` 会对普通复用和已支持的 `SG_MUL_VAL_` range 进行激活判断；非激活分支输出 `SignalQuality.InactiveMultiplex`，物理值为 `NaN`。对 `SupportsSingleFrameRuntime == false` 的 message，当前版本只承诺元数据保留；message/signal 级 codec 会失败关闭，不把传输协议 payload 当作 CAN/CAN FD 单帧处理。

写入策略：

- `Strict`: 超出物理范围或 raw 范围时失败。
- `ClampToRawRange`: raw value 超出 bit 长度可表达范围时钳制。
- `ClampToPhysicalRange`: 先把物理值钳制到 DBC 定义的 min/max。

runtime 中则应通过 handle 读写当前通道状态：

```csharp
var clockStart = Stopwatch.GetTimestamp();
var timestamp = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(clockStart));
var result = channel.SetPhysicalValue(speedHandle, 42.5, timestamp: timestamp);
```

如果当前场景不是热路径，可以用 `DbcSimpleChannel` 降低迁移成本：

```csharp
var simple = DbcSimpleChannel.Create(document);
var speedPath = SignalPath.Parse("VehicleStatus.VehicleSpeed");

simple.SetPhysicalValue(speedPath, 42.5);
var frame = simple.BuildFrame("VehicleStatus", timestamp);

var values = simple.Decode(new DbcFrameView(identifier, payload, timestamp: timestamp));
if (values.TryGetPhysicalValue("VehicleSpeed", out var speed))
{
    Console.WriteLine(speed);
}
```

如果希望连加载、diagnostics 和 simple channel 都一并封装，可以使用 `DbcSimpleRuntime`：

```csharp
var dbc = DbcSimpleRuntime.LoadFile("vehicle.dbc");
dbc.SetPhysicalValue("VehicleStatus.VehicleSpeed", 42.5);
var frame = dbc.BuildFrame("VehicleStatus", timestamp);
```

Simple facade 使用 `"Message.Signal"` / `SignalPath`，适合 UI、脚本、测试台和旧项目迁移。`DbcSimpleRuntime` 默认 Lenient 加载并在 Error 级 diagnostics 时失败关闭，Warning 保留在 `LoadResult`。它会在同名 signal 歧义、message 超出当前单帧 runtime 支持、路径不存在时失败关闭；高频收发仍建议使用预解析 handle。

## 接收帧与样本

实时和历史数据都可以统一表达为 `DbcFrameView`。实时场景中，硬件回调把 payload 映射为 `ReadOnlySpan<byte>` 后同步调用；历史 trace 场景中，trace parser 只需要还原 identifier、payload 和 timestamp。

```csharp
var frame = new DbcFrameView(identifier, payload, flags, timestamp);
var count = channel.ProcessReceivedFrame(frame, sampleSink);
```

`ISignalSampleSink` 用于低分配流式消费：

```csharp
private sealed class PlotSink : ISignalSampleSink
{
    public void OnSignalSample(in SignalSample sample)
    {
        if (sample.Quality == SignalQuality.Valid)
        {
            // 上层把 sample.Timestamp 作为 x，sample.PhysicalValue 作为 y。
        }
    }
}
```

这也是实时波形和导入 CAN Trace 后生成历史波形的推荐边界。`DiagKit.Dbc` 不解析 trace 文件，也不绘图，只负责把帧流投影为带质量状态的 signal samples。

## 当前快照

快照适合 UI 表格、脚本判断和“一键操作”读取当前状态：

```csharp
var snapshot = channel.GetSignalSnapshot(speedHandle);
if (snapshot.Quality == SignalQuality.Valid)
{
    Console.WriteLine(snapshot.PhysicalValue);
}
```

如果要按 DBC timeout 标记 stale，需要传入当前单调时间：

```csharp
var clockStart = Stopwatch.GetTimestamp();
var now = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(clockStart));
var snapshot = channel.GetSignalSnapshot(speedHandle, now);
```

需要给 UI 表格或脚本同时展示当前值和 DBC 元数据时，使用 view snapshot：

```csharp
var view = channel.GetSignalViewSnapshot(speedHandle, now);
Console.WriteLine($"{view.MessageName}.{view.SignalName} = {view.PhysicalValue} {view.Unit}");

if (view.ValueDescription is not null)
{
    Console.WriteLine(view.ValueDescription);
}
```

`DbcSignalViewSnapshot` 会复制 value table，包含当前 raw/physical、quality、timestamp、unit、min/max 和当前 raw value 的可选描述。它是展示友好的快照，不替代低分配 sample/snapshot 热路径。

UI 表格需要一次性绑定所有信号时，可使用 simple facade 的批量 view API：

```csharp
var rows = dbc.GetSignalViewSnapshots(now);
var vehicleRows = dbc.GetSignalViewSnapshotsForMessage("VehicleStatus", now);
var txRows = dbc.GetSignalViewSnapshotsTransmittedBy("VCU", now);
var rxRows = dbc.GetSignalViewSnapshotsReceivedBy("HOST", now);
```

批量 view 按 DBC message 顺序和 signal 顺序输出；当前 CAN/CAN FD 单帧 runtime 不支持的 message 仍会返回带元数据的 `NoData` 行，方便 UI 不漏显示数据库内容。

`DbcTimestampKind.MonotonicTicks` 表示单调 elapsed 时间，单位是 `TimeSpan.Ticks`，不要直接传入原始 Stopwatch 计数。
timestamp kind 必须一致；`Unspecified` 不参与 timeout 判定。
需要使用墙钟时间时，`DbcTimestampKind.UtcDateTimeTicks` 对应 `DateTime.UtcNow.Ticks`；周期调度和 timeout 判断不要把不同 kind 的 ticks 混用。
`DbcTimestamp.FromUtc(DateTimeOffset)` 会先转换为 UTC ticks，适合保留外部 trace 或硬件时间来源的墙钟语义。

## 周期发送

核心库不创建后台线程、不 sleep、不调用硬件 SDK。上层用自己的高精度循环定期调用 `PollDueFrames`：

```csharp
var clockStart = Stopwatch.GetTimestamp();
var firstDueTime = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(clockStart));

channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10), firstDueTime);

while (running)
{
    var now = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(clockStart));
    channel.PollDueFrames(now, txSink);
}
```

周期发布入口的选择规则：

| API | 行为 |
|-----|------|
| `AddPublishingMessage(messageHandle, period)` | 显式注册单条消息；`period` 为空时使用 `GenMsgCycleTime`，不检查 SendType。 |
| `AddCyclicPublishingMessagesFromDbc()` | 批量注册 SendType 明确为 `Cyclic` / `CyclicAndEvent` 且有正 `GenMsgCycleTime` 的消息。 |
| `AddCycleTimePublishingMessagesFromDbc()` | 批量注册所有有正 `GenMsgCycleTime` 的消息，忽略 SendType；适合旧工具或测试台约定。 |
| `RegisterCyclicPublishingMessagesFromDbc()` | 与 `AddCyclic...` 规则相同，但返回每条 message 的注册/跳过报告。 |
| `RegisterCycleTimePublishingMessagesFromDbc()` | 与 `AddCycleTime...` 规则相同，但返回每条 message 的注册/跳过报告。 |
| `RegisterPublishingMessagesTransmittedBy(nodeName)` | 按发送节点批量注册；未指定 `period` 时使用每条 message 的 `GenMsgCycleTime`。 |

显式使用 DBC 元数据注册明确周期类消息：

```csharp
var report = channel.RegisterCyclicPublishingMessagesFromDbc(firstDueTime);
foreach (var skipped in report.Skipped)
{
    Console.WriteLine($"{skipped.MessageName}: {skipped.Status} - {skipped.Reason}");
}
```

如果供应商 DBC 只可靠填写了 `GenMsgCycleTime`，可选择更宽松的批量入口：

```csharp
channel.RegisterCycleTimePublishingMessagesFromDbc(firstDueTime);
```

如果只想注册某个节点发送的消息：

```csharp
channel.RegisterPublishingMessagesTransmittedBy("VCU", firstDueTime: firstDueTime);
```

默认策略：

- 轻微迟到时立即发当前值。
- 保持绝对周期相位。
- 严重滞后时跳过错过的历史周期，不补发 backlog。
- 通过 `GetScheduleSnapshot` 查看 emitted、deadline miss、missed cycle 和 jitter。

`IDbcFrameSink.OnFrame` 中的 `data` 只保证在回调期间有效。如果硬件层需要异步发送或入队，应在适配层复制或复用缓冲。

`DbcFrame.Data` 和 `MessageSnapshot.Data` 是 `ReadOnlySpan<byte>` 公开只读视图。它们的构造函数会复制输入 payload，但公开属性不提供可变数组；需要跨异步边界、长期保存或交给要求 `byte[]` 的 API 时，应显式复制，例如 `snapshot.Data.ToArray()` 或 `frame.Data.ToArray()`。

## 并发和 GC 边界

推荐模型：

- `DbcDocument` 作为不可变元数据共享。
- `DbcChannelRuntime` 对偶发并发 public calls 保持内部状态一致；高频主机仍建议每个 channel 由单一执行上下文串行驱动，以获得更可预测的延迟。
- 跨线程通过上层队列、snapshot、sample sink 或拷贝后的 `DbcFrame` 传递。
- 热路径使用预解析 handle、`DbcFrameView`、`ReadOnlySpan<byte>`、`IDbcFrameSink` 和 `ISignalSampleSink`。
- `DbcSimpleChannel` / `DbcSimpleRuntime` 是非热路径 facade，可以跨 UI/脚本入口使用，但不应用作高频实时循环的主 API。
- 避免每帧字符串查找、LINQ、异常控制流和持续分配。

## 范围外能力

当前 `DiagKit.Dbc` 不包含：

- 具体硬件 SDK 或 CanHub 直接依赖。
- WPF/UI、波形绘图、CSV/EOL 测试业务。
- CAN trace 文件格式解析。
- 完整 J1939 协议栈。
- 多级 extended multiplexing 的完整 runtime/codec 行为。

这些能力应由上层应用或未来扩展库实现。
