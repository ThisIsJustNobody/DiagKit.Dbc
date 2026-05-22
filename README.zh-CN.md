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
tests/DiagKit.Dbc.Tests      单元、conformance、runtime 和 fuzz 测试
tests/DiagKit.Dbc.Benchmarks benchmark、soak 和 DBC corpus 验证入口
DiagKit.Dbc.slnx             解决方案
```

## 核心范围

- 支持 `Strict` / `Lenient` 的 DBC loader 和结构化 diagnostics。
- 提供 diagnostics 格式化/分组、`Errors` / `Warnings` 分类、`SignalPath`、`DbcSimpleRuntime` / `DbcSimpleChannel` 非热路径易用入口。
- Node、Message、Signal、环境变量、值表、属性、复用信号和来源行号元数据。
- 兼容 Vector `System*LongSymbol`，`Name` 默认使用完整名称，`SourceName` / `NameAliases` 保留 DBC 结构行短名。
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

本项目使用 MIT 许可证。NuGet 包版本由 MinVer 根据 Git tag 自动生成，tag 前缀为 `v`。首个预览 tag 为 `v1.0.0-preview`；后续预览使用 `v1.0.0-preview.1` 这类点分编号。发布流程对齐 DiagKit 家族：分支/PR 运行 CI，推送 tag 自动发布 NuGet 并创建 GitHub Release。

发布说明、贡献指南和安全漏洞报告方式见 [CHANGELOG.md](CHANGELOG.md)、[CONTRIBUTING.md](CONTRIBUTING.md) 和 [SECURITY.md](SECURITY.md)。
发布流程见 [发布清单](docs/PUBLISHING.zh-CN.md)。
