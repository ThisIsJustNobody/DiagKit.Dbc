# 贡献指南

感谢你考虑为 DiagKit.Dbc 做贡献。

## 项目范围

`DiagKit.Dbc` 是硬件无关的 DBC runtime core。除非已有单独设计决策明确纳入范围，否则贡献应把硬件 SDK、UI 框架、CAN trace 文件格式、CSV 工作流和完整 J1939 协议行为留在核心包之外。

适合贡献的方向包括：

- DBC loader 兼容性和结构化诊断。
- Signal codec 一致性和边界场景。
- Runtime 调度、快照和样本投影行为。
- 测试、benchmark、文档和包质量。

## 开发准备

安装 `global.json` 指定的 .NET SDK，然后运行：

```bash
dotnet restore DiagKit.Dbc.slnx
dotnet test DiagKit.Dbc.slnx
```

包验证：

```bash
dotnet pack src\DiagKit.Dbc\DiagKit.Dbc.csproj --configuration Release --no-restore
```

可选 runtime 验证：

```bash
dotnet run --project tests\DiagKit.Dbc.Benchmarks\DiagKit.Dbc.Benchmarks.csproj -- --matrix
dotnet run --project tests\DiagKit.Dbc.Benchmarks\DiagKit.Dbc.Benchmarks.csproj -- --soak --seconds 30
dotnet run --project tests\DiagKit.Dbc.Benchmarks\DiagKit.Dbc.Benchmarks.csproj -- --corpus path\to\dbc-folder
```

## Pull Request 要求

- 改变公共行为前先补聚焦测试。
- 保持热路径低分配；runtime 帧处理路径避免 LINQ、字符串查找、异常控制流和不必要分配。
- 如果新行为可能改变既有 runtime 语义，应保持显式或 opt-in。
- 面向用户的行为变化需要同步更新 `README.md`、`src/DiagKit.Dbc/README.md`、`CHANGELOG.md` 和包 release notes。
- 不要提交生成输出、构建产物和本地 worktree。

## DBC 兼容性变更

修改 parser 行为时，请至少包含以下一种依据：

- 单元测试中的最小 DBC 片段。
- 通过 corpus harness 验证过的脱敏真实 DBC 样本路径。
- 对不支持或有歧义语法的明确诊断预期。

如果某个 DBC/J1939/协议行为存在多种合理工业解释，请先开 issue 或讨论，再进入实现。
