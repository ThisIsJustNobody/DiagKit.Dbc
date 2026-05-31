# DiagKit.Dbc

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/DiagKit.Dbc?label=NuGet&color=orange)](https://www.nuget.org/packages/DiagKit.Dbc)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![CI](https://github.com/ThisIsJustNobody/DiagKit.Dbc/actions/workflows/ci.yml/badge.svg)](https://github.com/ThisIsJustNobody/DiagKit.Dbc/actions/workflows/ci.yml)

[English](README.md)

DiagKit.Dbc 是面向 .NET 10 的 DBC 运行时核心库，用于诊断工具和 CAN/CAN FD 应用。当前重点是 DBC 加载、元数据建模、bit 级信号编解码、运行时通道状态、信号样本投影，以及硬件无关的周期发送调度。

## 目录结构

```text
src/DiagKit.Dbc              产品库与 NuGet 包内容
src/DiagKit.Dbc.Workbook     DBC Excel 格式扩展包
src/DiagKit.Dbc.Tool         DBC Excel export/import/validate CLI 工具
tests/DiagKit.Dbc.Tests      单元、conformance、runtime 和 fuzz 测试
tests/DiagKit.Dbc.Workbook.Tests  Workbook 导出/导入测试
tests/DiagKit.Dbc.Tool.Tests      CLI 测试
tests/DiagKit.Dbc.Benchmarks benchmark、soak 和 DBC corpus 验证入口
DiagKit.Dbc.slnx             解决方案
```

## 核心范围

- 支持 `Strict` / `Lenient` 的 DBC loader 和结构化 diagnostics。
- 提供 diagnostics 格式化/分组、`Errors` / `Warnings` 分类、`SignalPath`、`DbcSimpleRuntime` / `DbcSimpleChannel` 非热路径易用入口。
- Node、Message、Signal、环境变量、值表、属性、复用信号和来源行号元数据。
- 兼容 Vector `System*LongSymbol`，`Name` 默认使用完整名称，`SourceName` / `NameAliases` 保留 DBC 结构行短名。
- 通过 `DbcWriter` 做规范化 DBC 导出，包含写出 diagnostics、Vector long-symbol 导出和 reload 语义等价测试覆盖。
- 通过 `DbcDocumentBuilder` 新建或语义编辑文档，再交给 writer 导出。
- CAN/CAN FD identifier、DLC、flags 和 timestamp 模型。
- Intel/Motorola 信号 codec、raw/physical 转换和显式写入策略。
- 面向接收处理、当前状态 snapshot 和信号样本流的 runtime session/channel。
- 硬件无关的立即构帧和周期 due-frame 轮询。
- cycle time、send type、timeout、signal start value 和 CAN FD frame format 的语义映射。

核心库不直接依赖 CanHub 或硬件 SDK。接收方向由上层把硬件帧转换为 `DbcFrameView` 后调用处理入口；发送方向由上层通过 `IDbcFrameSink` 消费待发送帧。

## 入口选择

| 场景 | 建议入口 | 说明 |
| --- | --- | --- |
| 首次接入、UI、脚本、测试台 | `DbcSimpleRuntime` | 加载 DBC、保留 diagnostics，并提供 `"Message.Signal"` 便捷 API。 |
| 生产运行时状态机 | `DbcRuntimeSession` / `DbcChannelRuntime` | 使用预解析 handle、snapshot、sink 和周期轮询。 |
| 底层工具和元数据处理 | `DbcLoader.LoadDocumentOrThrow`、`DbcDocument`、`DbcCodec` | 直接查看 DBC 元数据，或做无状态 encode/decode。 |

API 集成方式见 [API 使用指南](docs/API.zh-CN.md)。
详见 [CanHub / 外部硬件适配边界](CANHUB-ADAPTER.zh-CN.md)。

## 规范化 DBC 导出

`DbcWriter` 从不可变 `DbcDocument` 生成稳定、可重新加载的 DBC 文本。它适用于新建、语义编辑后生成、CI 规范化导出，并以 reload 语义等价为目标。这是规范化导出，不是逐字节 round-trip 编辑；不承诺保留原文件空白、语句顺序、未知语句或注释位置。

默认 `DbcWriterCompatibilityProfile.ReloadEquivalent` 优先保留本库可重新加载的元数据，因此可能输出当前尚未列入 CANdb++ known-good 的语句，例如普通 `BA_ ... EV_ ...` 环境变量属性赋值和 `BA_REL_` 关系属性赋值。面向 CANdb++ 打开验证时，请使用 `DbcWriterCompatibilityProfile.CanDbPlusKnownGood`：严格模式遇到这些已知不支持语句会失败，宽松模式会省略它们并返回 warning。当前 CANdb++ known-good 集合包括 `EV_`、`BO_TX_BU_`、`BA_DEF_REL_` 和 `BA_DEF_DEF_REL_`；`BA_REL_` assignment 需要真实 Vector/CANdb++ 样例或官方语法后再重新支持。

```csharp
var document = DbcLoader.LoadTextDocumentOrThrow(dbcText);
var result = DbcWriter.WriteText(document);
File.WriteAllText("normalized.dbc", result.GetTextOrThrow());
```

需要新建或编辑 DBC 时可以先使用 `DbcDocumentBuilder`：

```csharp
var builder = DbcDocumentBuilder.Create();
builder.AddNode("ECU");
builder.AddMessage(new DbcRawMessageId(256), "Status", 8, "ECU")
    .AddSignal("Speed", 0, 16)
    .WithScaling(0.1, 0);

var text = DbcWriter.WriteTextOrThrow(builder.Build());
```

## DBC Excel 格式

Excel 编辑位于独立扩展包 `DiagKit.Dbc.Workbook`，核心 `DiagKit.Dbc` 不包含 Excel API。`.xlsx` 文件是 DBC 的通用语义表格格式：只包含 `Network`、`Nodes`、`Messages`、`Signals`、`ValueDescriptions`、`MultiplexRanges`、`EnvironmentVariables`、`AttributeDefinitions`、`Attributes` 和 relation attribute 等 DBC 实体表，不包含 manifest、readme sheet、来源路径/哈希或内部对象 key。

可以创建空模板，也可以先从 DBC 导出单个 `.xlsx`，之后编辑这些 DBC 语义表，再只基于这个 Excel 文件导入并输出 normalized DBC。该能力不是 CAN trace、波形采样、EOL 测试脚本，也不是供应商 DBC 原文件的保格式 round-trip。导入后仍会走 `DbcWriter` validation；`Vector__XXX`、`VFrameFormat`、`Gen*` 定时/发送类型元数据和 Vector independent signals 等会按 normalized DBC 输出能力规范化。

```csharp
using DiagKit.Dbc.Workbook;

var document = DbcLoader.LoadDocumentOrThrow("vehicle.dbc");
DbcWorkbookExporter.WriteWorkbookOrThrow("edit.xlsx", document);

var imported = DbcWorkbookImporter.ImportWorkbookFile("edit.xlsx").GetDocumentOrThrow();
var normalized = DbcWriter.WriteTextOrThrow(imported);
```

也可以使用 CLI：

```bash
diagkit-dbc workbook template -o edit.xlsx
diagkit-dbc workbook export vehicle.dbc -o edit.xlsx
diagkit-dbc workbook validate edit.xlsx
diagkit-dbc workbook import edit.xlsx -o normalized.dbc
```

## 快速开始

```csharp
var simple = DbcSimpleRuntime.LoadFile("vehicle.dbc");
Console.WriteLine(DbcDiagnosticFormatter.FormatGrouped(simple.LoadResult.Diagnostics));
var clockStart = Stopwatch.GetTimestamp();
simple.SetPhysicalValue("VehicleStatus.VehicleSpeed", 42.5);
var frame = simple.BuildFrame("VehicleStatus", DbcTimestamp.FromElapsed(Stopwatch.GetElapsedTime(clockStart)));
```

## 构建和测试

```bash
dotnet build DiagKit.Dbc.slnx
dotnet test DiagKit.Dbc.slnx
```

## 验证入口

```bash
dotnet run --project tests/DiagKit.Dbc.Benchmarks/DiagKit.Dbc.Benchmarks.csproj -- --matrix
dotnet run --project tests/DiagKit.Dbc.Benchmarks/DiagKit.Dbc.Benchmarks.csproj -- --soak --seconds 30
dotnet run --project tests/DiagKit.Dbc.Benchmarks/DiagKit.Dbc.Benchmarks.csproj -- --corpus path/to/dbc-folder
```

## 包状态

本项目使用 MIT 许可证。NuGet 包版本由 MinVer 根据 Git tag 自动生成，tag 前缀为 `v`，项目文件不硬编码发布版本号。下一次计划预览 tag 为 `v1.2.0-preview.1`。CI 覆盖 pull request 以及 `main`、`master`、`release/**` 分支 push；推送 tag 后自动发布 NuGet 并创建 GitHub Release。

发布说明、贡献指南和安全漏洞报告方式见 [CHANGELOG.md](CHANGELOG.md)、[CONTRIBUTING.md](CONTRIBUTING.md) 和 [SECURITY.md](SECURITY.md)。
发布流程见 [发布清单](docs/PUBLISHING.zh-CN.md)。
