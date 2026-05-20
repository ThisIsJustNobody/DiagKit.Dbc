# 安全策略

## 支持版本

安全修复面向最新发布的 preview 或 stable 包版本。

## 报告漏洞

请不要在公开 issue 中披露安全敏感问题。

优先使用本仓库的 GitHub private vulnerability reporting：

https://github.com/ThisIsJustNobody/DiagKit.Dbc/security/advisories/new

如果 private reporting 不可用，请先通过私有渠道联系维护者，再公开细节。

## 范围

可能属于安全敏感范围的内容包括：

- 恶意或畸形 DBC 输入导致拒绝服务、过量内存使用或未处理异常。
- 错误帧编码/解码导致诊断工具产生不安全行为。
- 影响包完整性的构建、包或 SourceLink 元数据问题。

硬件 SDK 行为、宿主应用授权、车辆/网络安全策略不属于本核心库范围，除非问题直接由 `DiagKit.Dbc` 引起。
