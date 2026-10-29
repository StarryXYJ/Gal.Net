# P0 环境与内容闭包检查

日期：2026-10-29。此表区分已检查的环境与待执行的平台验证，不构成平台支持声明。

| 环境 | 本次事实 | 尚缺证据 |
| --- | --- | --- |
| Windows x64 | Windows 10.0.26100；SDK 10.0.401 与 global.json 一致；运行时 10.0.12；相关项目可构建与运行测试 | 分发 Editor 在无 SDK 环境中的完整烟测；本 feature 的最终三平台发行验收 |
| Ubuntu WSL2 | 已安装名为 Ubuntu 的 WSL2，/etc/os-release 为 Ubuntu 26.04.1 LTS；gcc 可定位，dotnet/cmake/pkg-config 未定位 | Linux SDK/CMake/依赖环境、媒体及 GUI 测试；不能替代设计中的 Ubuntu 24.04 最低基线验证 |
| macOS arm64/x64 | 当前执行宿主为 Windows，未执行 macOS 构建 | 两架构原生依赖、bundle、签名与运行 |
| Android | Windows 安装的 .NET workload 列表包含 android 36.1.69/10.0.100 | 当前模板恢复、NDK/原生库、发布及设备运行，不能以 workload 列表判为可用 |
| iOS | Windows workload 列表包含 ios 26.5.10301/10.0.100 | macOS/Xcode 构建宿主、静态音频、VLC/AOT、签名和真机探针 |
| SDL | 尚未执行 native 集成探针 | 版本/绑定、主线程设备更新、三平台真实手柄 |

WSL 发行版查询在受限进程内曾返回访问拒绝；通过获准的 WSL 只读查询成功确认环境。未安装/升级 Linux 软件或修改用户已有 CrossPlatform 工程。

## 内容闭包结论与实施选择

原 GamePackageExporter.ExportAsync 仅把 prepared.Base 写入 galpak，把 prepared.Stories 发布到归档旁的 StoryPatches/<buildId>。所以单独移动归档会漏掉剧情。ExportDirectoryAsync 已写入本体和全部剧情，两个交付物原先不等价。

用户明确不考虑向后兼容后，采用唯一的新标准：

- installation.json v4 索引基础 PAK、localization.pak 与全部已声明剧情分片；不索引 game.pak 自身。
- Archive/Directory 使用同一 PrepareAsync，内容文档及载荷一致；归档内包含全部剧情，不再生成旁置剧情目录。
- 安装器固定读取 pak/game.pak，只接受新索引；不保留 v1/v2/v3 归档安装。
- 资源覆盖补丁仍由独立导出入口显式交付，不自动收集工作区/归档旁任意补丁；两种内容导出的 ResourcePatches 均初始为空。
- galpak 可用普通 ZIP 工具解压得到 pak/；正式安装器额外验证闭包、大小/hash、路径和重复条目。

相关测试覆盖独立移动归档、全部剧情运行、目录/归档逐文件一致、缺失/损坏剧情拒绝、取消保留原文件、重复安装保留外置翻译以及重复安装时仍校验源归档 hash。最终运行数量与限制记录在 execution.md。
