# P1b / P2a：宿主接入、Editor 面板和运行变量

状态：verified（本记录列出的范围）；P1 的真实外部 Sample 人工闭环仍待 P6，不等同 feature 完成。

## 交付

- `EngineDebugRun` 将可选 Runtime 调试会话接入独立协议；默认 Avalonia 宿主仅增加中立的可选绑定回调，不依赖通信组件。Sample 在命令行收到完整调试邀请时连接，标题页可连接，开始回调可选，停止解绑当前运行。
- Editor 的“测试”菜单、项目级调试 Dock、项目设置中的本地启动配置。先保存并通过正式构建器产出 `Output/Debug`，再监听并启动指定应用；测试 profile 位于项目 `.galnet/debug-profile`，启动参数使用 ArgumentList。
- 当前实例 Player/Save 变量分栏；轮询权威状态（150ms，当前实现为全量查询），草稿不被刷新覆盖，应用检查运行身份、执行代次、类型及原值。关闭 Dock 保留项目级连接与草稿，关闭项目断开。
- 当前 Group/作者 Entry 独立高亮；使用编译 SourceMap 映射，不猜测编译数字序号。内容编辑后停止使用旧映射。Graph/Entry 右键定位与有界路线列表先接入现有 Runtime 追帧端口。
- 路线规划优先唯一、覆盖全部选中节点的直接后继链；即使另有短路，也不丢弃选中节点。保留平行出口身份，候选/深度/搜索量有上限。

## 证据

- `dotnet test test/GalNet.Debugging.Tests/GalNet.Debugging.Tests.csproj --no-restore -v minimal`：5 项通过，包括真实 TCP 上 Engine 绑定、变量读写、跳转换代、旧请求拒绝和停止解绑。
- `dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --no-restore --filter FullyQualifiedName~DebugRoutePlanner -v minimal`：3 项通过。
- `dotnet test test/GalNet.Editor.Tests/GalNet.Editor.Tests.csproj --no-restore --filter FullyQualifiedName~ExternalDebugPanelConnects -v minimal`：1 项通过。实际挂载 Dock View，通过 loopback TCP 更新两作用域、修改变量、保留冲突草稿、检查 Graph/Entry 高亮及作者历史不变、定位后变量保留、断线/关闭项目清理。
- Sample 构建通过，0 errors；已有 Avalonia 资源构造器 warnings 保留。

## 尚未验收

上述 TCP 测试的两端运行在同一测试进程，并非外部 Sample 媒体人工验收。文件选择器、真实 Sample 标题页开始/停止、进程重开留给 P6。P3 的断点/作者单步、P4 的目标条件一次豁免和部分跨组边界仍继续实现；缓存性能与固定锚点没有宣称交付。握手目前验证协议/项目/编译内容指纹；没有单独的运行契约实现指纹。
