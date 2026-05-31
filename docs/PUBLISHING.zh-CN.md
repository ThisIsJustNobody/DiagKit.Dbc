# 发布清单

本文记录 `DiagKit.Dbc` 发布 NuGet 包前后的固定步骤。发布行为应以 Git tag 为准，版本由 MinVer 自动计算。

## 发布范围

当前计划预览版本：

- tag: `v1.2.0-preview.1`
- package version: `1.2.0-preview.1`
- package id: `DiagKit.Dbc`
- license: MIT

版本号不写入项目文件，由 MinVer 根据 `v*` tag 自动生成。tag 一旦推送到远端或包已经发布到 NuGet，就不要移动；需要修正时发布新的 tag，例如 `v1.2.0-preview.1`、`v1.2.1-preview` 或后续稳定版。

## 本地预检

从干净的 `main` 分支开始：

```bash
git status --short
git fetch --tags
dotnet restore DiagKit.Dbc.slnx
dotnet build DiagKit.Dbc.slnx --configuration Release --no-restore
dotnet test DiagKit.Dbc.slnx --configuration Release --no-build
dotnet pack src/DiagKit.Dbc/DiagKit.Dbc.csproj --configuration Release --no-build --output artifacts/packages
```

如果修改了 loader、codec 或 runtime 热路径，至少额外运行：

```bash
dotnet run --project tests/DiagKit.Dbc.Benchmarks/DiagKit.Dbc.Benchmarks.csproj -- --matrix
```

如果修改了 parser 或真实 DBC 兼容行为，根据风险运行 `--corpus`；如果修改了调度/GC 相关代码，根据风险运行 `--soak`。

## 包内容检查

确认生成物：

- `DiagKit.Dbc.<version>.nupkg`
- `DiagKit.Dbc.<version>.snupkg`

建议检查 `.nupkg` 中至少包含：

- `lib/net10.0/DiagKit.Dbc.dll`
- `lib/net10.0/DiagKit.Dbc.xml`
- `README.md`
- `README.zh-CN.md`
- `diagkit-dbc.png`
- MIT license expression
- GitHub repository metadata

## Tag 策略

当前预览版 tag 示例：

```bash
git tag v1.2.0-preview.1
git push origin main
git push origin v1.2.0-preview.1
```

如果当前预览版之后还需要继续发布预览，使用点分编号或新的预览基线，例如 `v1.2.0-preview.1`、`v1.2.0-preview.2` 或 `v1.2.1-preview`。

后续稳定版示例：

```bash
git tag v1.2.0
git push origin main
git push origin v1.2.0
```

MinVer 使用 `v` 作为 tag prefix。没有新 tag 的提交会生成递增的 preview metadata，不应作为正式发布版本随意上传。

## GitHub Actions 发布

仓库提供 `Release` workflow，并对齐 DiagKit 家族发布方式：推送 `v*` tag 后自动执行 restore、build、test、pack，随后发布 NuGet 包和符号包，并创建 GitHub Release。

推荐流程：

1. 在本地完成预检和包内容检查。
2. 确认 `main` 已包含要发布的提交。
3. 创建目标 tag，例如当前计划预览版 `v1.2.0-preview.1`，或后续预览版 `v1.2.0-preview.2`。
4. 推送 `main`，等待 `CI` workflow 通过。
5. 推送 tag，触发 `Release` workflow。
6. 在 GitHub Actions 检查发布作业；发布成功后确认 NuGet 页面和 GitHub Release。

正式发布需要仓库 secret：

- `NUGET_API_KEY`

`Release` workflow 会校验 tag 形状，允许 `v1.0.0`、`v1.0.0-preview` 和 `v1.0.0-preview.1` 这类语义版本 tag；带 `-` 的 tag 会创建 prerelease GitHub Release。

## 手动发布备用命令

如果 GitHub Actions 临时不可用，可以本地发布。发布前再次确认当前 checkout 位于目标 tag：

```bash
git describe --tags --exact-match
```

确认无误后执行：

```bash
dotnet nuget push artifacts/packages/*.nupkg --api-key <NUGET_API_KEY> --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/packages/*.snupkg --api-key <NUGET_API_KEY> --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## 发布后检查

- NuGet 页面显示版本、README、license、repository URL。
- SourceLink 能从符号包定位到 GitHub commit。
- GitHub Release 和 CHANGELOG 已描述当前版本范围和已知边界。
- README badge 指向正确的包版本和 CI workflow。
