# 变更日志

本文件记录项目的重要变更。包版本由 Git tag 通过 MinVer 自动生成；当前预览版本为 `1.1.0-preview.2`。

## Unreleased

- Added normalized DBC export through `DbcWriter`, including diagnostics and reload-equivalence coverage.
- Added semantic `DbcDocumentBuilder` for creating or editing documents before export.
- Documented that first-stage export is normalized and does not preserve original file formatting.

## 1.1.0-preview.2 - 2026-05-22

### 易用性升级

- `DbcLoader` 新增 `LoadDocument*` / `LoadTextDocument*` 便捷入口，减少只需要不可变文档时的加载样板。
- `DbcSimpleRuntime` / `DbcSimpleChannel` 新增批量 `DbcSignalViewSnapshot` API，方便 UI 按全部信号、message、发送节点或接收节点绑定。
- 常见 message/signal/path 解析异常补充大小写敏感、可用集合和歧义处理提示。
- 兼容 Vector `SystemNodeLongSymbol`、`SystemMessageLongSymbol`、`SystemSignalLongSymbol`、`SystemEnvVarLongSymbol`：对象 `Name` 默认恢复完整名，DBC 短名保存在 `SourceName` / `NameAliases`，解析完整名或短名均可命中，冲突时失败关闭。
- 环境变量新增 `Attributes`，并正确应用 `BA_ ... EV_ ...` 属性赋值。
- README 和中文 API 指南明确三层入口：简单使用、生产运行时状态机、底层工具。

### 测试与发布

- 修复 `MSTest.TestAdapter` / `MSTest.TestFramework` 版本不一致导致 `dotnet test` 不发现测试的问题；Release 测试恢复为真实执行 172 个测试。

## 1.0.0-preview - 2026-05-21

### 新增

- DBC 加载器，支持 `Strict` / `Lenient` 模式和结构化诊断。
- Node、Message、Signal、值表、属性、来源行号、CAN identifier、CAN FD 和时间戳模型。
- Intel/Motorola bit 级信号编解码、signed/raw/physical 转换、浮点信号和显式写入策略。
- Runtime session/channel 模型，支持接收帧处理、当前快照、信号样本、观察消息过滤和发布消息选择。
- 硬件无关的立即构帧和周期 due-frame 轮询。
- DBC 语义映射：cycle time、send type、timeout、start value、CAN FD `VFrameFormat`。
- 简单 `SG_MUL_VAL_` range activation 支持，用于 runtime sample/snapshot 的 active 判断。
- 确定性 loader fuzz/property 测试、benchmark matrix、显式 soak 模式和真实 DBC corpus 验证入口。
- MIT 许可证、MinVer 版本、GitHub SourceLink、符号包和 NuGet 包元数据。
- GitHub Actions CI、Dependabot、Issue 模板、PR 模板、贡献指南和安全策略。
- XML documentation 输出、中文 API 使用指南、发布清单，以及 tag push 自动 NuGet 发布和 GitHub Release workflow。

### 审计加固

- 数值 codec 修复 64-bit signed/unsigned 精度与范围边界，raw/physical 写入按策略失败或钳制，避免溢出和静默反转。
- 时间戳与调度契约明确 `DbcTimestampKind.MonotonicTicks` 使用 `TimeSpan.Ticks` 单位；调度和 timeout 路径拒绝混用 timestamp kind，并保护 deadline/missed-cycle 统计。
- `DbcChannelRuntime` 对偶发并发 public calls 保持内部一致性；channel-scoped handles 在跨 channel 误用时失败关闭，并且不再把内部 message/signal 索引作为 public API 暴露。
- Loader 加固 DoS 和诊断边界：`DbcLoadOptions.MaxStatementLength` 默认 1 MiB，超长 statement 输出 `DBC_STATEMENT_TOO_LONG`，格式错误输入输出结构化 diagnostics。
- Motorola bit 写入具备失败原子性，非法 bit range 不会部分修改目标 payload。
- 公开 payload 边界改为只读：`DbcFrame.Data` 与 `MessageSnapshot.Data` 暴露 `ReadOnlySpan<byte>`，跨异步或保存时由调用方显式复制。
- `CanIdentifier` 补齐与 `IComparable<CanIdentifier>` 一致的比较运算符。
- Vector 兼容边界调整：Lenient 加载可保留 payload 超过 64 字节的 J1939/传输协议 message 元数据，并用 `SupportsSingleFrameRuntime` 区分是否可进入当前 CAN/CAN FD 单帧 runtime；重复 value table 在 Lenient 下保留第一份并 warning，Strict 仍失败。

### 易用性升级

- 加载结果新增 `Errors`、`Warnings`、`HasErrors`、`HasWarnings` 和 `ThrowIfErrors()`；新增 `DbcDiagnosticFormatter`，便于 UI、日志和异常直接展示 diagnostics。
- `DbcTimestamp` 新增 `DateTimeOffset` UTC factory，继续要求单调调度时间使用 `DbcTimestamp.FromElapsed(...)`。
- 新增 `SignalPath`、`DbcSimpleRuntime`、`DbcSimpleChannel` / `DbcSimpleFrameValues` 作为非热路径 facade，用于脚本、UI、测试台和旧项目迁移；同名 signal 歧义与 runtime unsupported message 均失败关闭。
- 新增 `DbcDiagnosticFormatter.Summarize()` / `FormatGrouped()`，支持按 severity/code 分组展示 diagnostics。
- 新增 `AddCycleTimePublishingMessagesFromDbc()`，与严格按 SendType 的 `AddCyclicPublishingMessagesFromDbc()` 区分，显式支持“只要有 CycleTime 就发送”的测试台场景；新增 `Register*` 报告 API 与按发送节点批量注册入口。
- 新增 `DbcSignalViewSnapshot`，把当前 raw/physical/quality/timestamp 与 unit、min/max、value table 元数据合并为展示友好的快照。
- README 和中文 API 指南补充 5 分钟黄金路径、周期发送选择规则、线程模型和硬件适配边界。

### 说明

- 这是预览版本。公共 API 已可用于集成试用，但在第一个稳定版前仍可能根据真实项目反馈调整。
- 完整 J1939 协议行为和多级 extended multiplexing 不在本预览范围内。
