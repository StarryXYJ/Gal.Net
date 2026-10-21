# P5：有界的最近自然入口缓存

状态：implemented，Runtime 验证通过；热点前缀/固定入口预计算明确延期。

- 每 Group 最多一份最近自然入口；默认总计最多 256 个 Group、64 MiB 序列化状态 payload 预算，超过按 LRU 淘汰。超大单项不缓存。0 可关闭入口缓存。初始运行场景单独保留，未计入入口 payload 预算。
- 预算衡量的是纯状态序列化数据及固定元数据开销，不是精确托管堆容量；不保存媒体、控件或 Task。状态 DTO 提供缓存条数、payload 字节、上限与最近追帧耗时/指令数/是否入口命中。
- 淘汰后的同组回跳，Editor 改从游戏根规划合法路线；无法构建时仍可显式选择独立初始场景测试。普通读档清空入口和统计；自然再次进入覆盖原值，调试追帧不覆盖。
- 不缓存热点前缀：当前变量可影响场景参数，尚无可靠读集/纯状态 reducer；每次按当前变量重建前缀，避免过期复用。固定预计算也未启用：现有存在性分析不能证明资源、坐标、音轨及所有模块状态一致。没有后台预跑游戏/任意插件代码。

验证：Runtime 110 项通过（新增 3 项）；涵盖关闭/超大项、LRU 条数限制、普通读档失效、缓存与根路线重放的完整模块状态一致。2000 条根前缀 + 200 条目标 Group 的人工 primitive 测量：入口重建目标前 150 条为 15.40 ms / 150 条；根路线重建为 27.03 ms / 2151 条；入口 payload 396 bytes。此为本机合成逻辑负载的单次观测，不含真实媒体解码，不作为 SLA。

命令：`dotnet test test/GalNet.Runtime.Tests/GalNet.Runtime.Tests.csproj --no-restore -v minimal`；统计观测使用 `--no-build --filter FullyQualifiedName~LatestEntranceLru --logger "console;verbosity=normal"`。
