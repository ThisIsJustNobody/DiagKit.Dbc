# DiagKit.Dbc

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ThisIsJustNobody/DiagKit.Dbc/blob/main/LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/DiagKit.Dbc?label=NuGet&color=orange)](https://www.nuget.org/packages/DiagKit.Dbc)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![CI](https://github.com/ThisIsJustNobody/DiagKit.Dbc/actions/workflows/ci.yml/badge.svg)](https://github.com/ThisIsJustNobody/DiagKit.Dbc/actions/workflows/ci.yml)

[English](README.md)

`DiagKit.Dbc` 是面向 .NET 10 的 DBC 运行时核心库，用于诊断工具和 CAN/CAN FD 应用。它提供 DBC 加载、不可变元数据模型、信号编解码、运行时通道状态、信号样本投影，以及硬件无关的周期发送调度。

## 当前能力

- 以 `Strict` 或 `Lenient` 模式加载 DBC，并输出结构化 diagnostics。
- 提供 `DbcDiagnosticFormatter` 分组输出、`Errors` / `Warnings`、`SignalPath` 和 Simple facade，降低首次接入和旧项目迁移成本。
- 不需要运行时状态时，可用 `DbcLoader.LoadDocumentOrThrow(...)` 直接获取不可变文档。
- 可通过 `DbcSimpleRuntime` / `DbcSimpleChannel` 批量枚举 `DbcSignalViewSnapshot`，方便 UI 绑定。
- 建模 Node、Message、Signal、环境变量、关系属性元数据、属性、值表、复用信号、CAN identifier、CAN FD flags 和来源行号。
- 兼容 Vector `SystemNodeLongSymbol`、`SystemMessageLongSymbol`、`SystemSignalLongSymbol` 和 `SystemEnvVarLongSymbol`，默认展示完整名称，同时保留短名别名查找。
- 使用 `DbcWriter` 导出稳定、可重新加载的规范化 DBC 文本，包含写出 diagnostics、Vector long-symbol 导出和 reload 语义等价覆盖。
- 使用 `DbcDocumentBuilder` 新建或语义编辑文档，再交给 writer 导出。
- 编解码 Intel/Motorola 信号、 signed 值、浮点信号、raw 值和 physical 值。
- 使用显式写入策略处理范围问题，避免静默修正关键语义。
- 将接收帧处理为当前状态 snapshot 和流式 `SignalSample`，供实时波形、历史回放和分析层消费。
- 支持立即构帧和周期帧轮询，但不接管硬件、线程、timer 或应用队列。
- 映射常见 DBC 语义，例如 cycle time、send type、timeout、signal start value 和 `VFrameFormat`。
- 宽松模式兼容常见第三方 DBC 输出，包括 Vector 可解释的传输协议长度 message 元数据、重复 value table warning、同名 signal 元数据歧义等情况。
- 提供 deterministic fuzz/property 测试、benchmark matrix、显式 soak 运行和真实 DBC corpus 验证入口。

## 范围边界

本包保持硬件无关，不依赖 CanHub、Vector、ZLG、WPF、Excel/CSV 业务流程或任何硬件 SDK。宿主应用负责把硬件帧转换为 `DbcFrameView`，并通过 `IDbcFrameSink` 消费待发送帧。

需要通过 Excel 批量编辑 DBC 常用参数时，请引用独立扩展包 `DiagKit.Dbc.Workbook`，或使用 `DiagKit.Dbc.Tool` 的 `diagkit-dbc workbook template/export/import/validate` 命令。导入时只读取 `.xlsx` DBC 语义表格文件本身；它不表示 CAN trace、波形采样、EOL 测试脚本或 DBC 源文件保格式编辑。

公共边界已经为 J1939 预留空间，但完整 J1939 协议栈不属于本包职责。Lenient 加载可保留超过 64 字节的 J1939/传输协议 payload 元数据；`DbcChannelRuntime` 和 CAN/CAN FD 帧 API 只处理 `SupportsSingleFrameRuntime == true` 的 message。后续协议层行为应放在独立扩展库中。

更完整的集成说明见仓库文档：[API 使用指南](https://github.com/ThisIsJustNobody/DiagKit.Dbc/blob/main/docs/API.zh-CN.md)。

## 入口选择

| 场景 | 建议入口 | 说明 |
| --- | --- | --- |
| 首次接入、UI、脚本、测试台 | `DbcSimpleRuntime` | 加载 DBC、保留 diagnostics，并提供 `"Message.Signal"` 便捷 API。 |
| 生产运行时状态机 | `DbcRuntimeSession` / `DbcChannelRuntime` | 使用预解析 handle、snapshot、sink 和周期轮询。 |
| 底层工具和元数据处理 | `DbcLoader.LoadDocumentOrThrow`、`DbcDocument`、`DbcCodec` | 直接查看 DBC 元数据，或做无状态 encode/decode。 |

## 规范化 DBC 导出

`DbcWriter` 从不可变 `DbcDocument` 写出稳定、可重新加载的 DBC 文本，适用于新建、语义编辑后生成和 CI 规范化导出。它承诺 reload 语义等价，不是逐字节 round-trip 编辑；不保留原文件空白、语句顺序、未知语句或注释位置。

默认 writer profile 是 `ReloadEquivalent`，会优先保留本库可重新加载的元数据，但可能输出当前尚未列入 CANdb++ known-good 的普通 `BA_ ... EV_ ...` 环境变量属性和 `BA_REL_` 关系属性赋值。面向 CANdb++ 导出时使用 `DbcWriterCompatibilityProfile.CanDbPlusKnownGood`；严格模式会失败关闭，宽松模式会省略这些语句并返回 warning。

```csharp
var document = DbcLoader.LoadTextDocumentOrThrow(dbcText);
var result = DbcWriter.WriteText(document);
var text = result.GetTextOrThrow();
```

需要新建或编辑文档时，可以先使用 `DbcDocumentBuilder`：

```csharp
var builder = DbcDocumentBuilder.Create();
builder.AddNode("ECU");
builder.AddMessage(new DbcRawMessageId(256), "Status", 8, "ECU")
    .AddSignal("Speed", 0, 16)
    .WithScaling(0.1, 0);
```

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
var start = Stopwatch.GetTimestamp();
var now = DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(start));

var report = channel.RegisterCyclicPublishingMessagesFromDbc();
channel.PollDueFrames(now, txSink);
```

接外部硬件适配器时，把硬件接收帧转换为 `DbcFrameView` 调用 `ProcessReceivedFrame`，把 `IDbcFrameSink.OnFrame` 输出复制或同步转发到硬件发送 API。`DbcSimpleRuntime` / `DbcSimpleChannel` 适合 UI、脚本和迁移；高频路径继续使用预解析 handle、`DbcFrameView`、`IDbcFrameSink` 和 `ISignalSampleSink`。

## 验证

```bash
dotnet test ..\..\DiagKit.Dbc.slnx
dotnet run --project ..\..\tests\DiagKit.Dbc.Benchmarks\DiagKit.Dbc.Benchmarks.csproj -- --matrix
dotnet run --project ..\..\tests\DiagKit.Dbc.Benchmarks\DiagKit.Dbc.Benchmarks.csproj -- --soak --seconds 30
dotnet run --project ..\..\tests\DiagKit.Dbc.Benchmarks\DiagKit.Dbc.Benchmarks.csproj -- --corpus path\to\dbc-folder
```

## 包状态

本包使用 MIT 许可证。NuGet 版本由 MinVer 根据 Git tag 自动生成，tag 前缀为 `v`。首个预览 tag 为 `v1.0.0-preview`；后续预览使用 `v1.0.0-preview.1` 这类点分编号。发布流程对齐 DiagKit 家族：先通过 CI，再由 tag 自动发布 NuGet 并创建 GitHub Release。
