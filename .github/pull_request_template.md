## 摘要

- 

## 验证

- [ ] `dotnet test DiagKit.Dbc.slnx`
- [ ] `dotnet pack src\DiagKit.Dbc\DiagKit.Dbc.csproj --configuration Release --no-restore`
- [ ] 修改 runtime、codec 或 loader 行为时，已运行 benchmark/corpus 验证

## 范围

- [ ] 没有向 `DiagKit.Dbc` 增加硬件 SDK、UI、J1939 协议栈或应用工作流依赖
- [ ] 公共 API 或协议行为变化已记录
- [ ] 面向用户的行为变化已更新 README / CHANGELOG / release notes
