# 已完成阶段总结

更新：2026-10-30。这里只汇总已实现、已验证的子范围；整个 feature 仍在进行中。[剩余计划](phase-plan.md)负责后续任务，[执行记录](execution.md)保留原始结果、失败和限制，[环境矩阵](environment-matrix.md)记录平台证据。用户已将手柄、macOS、iOS 验收后移，允许移动使用 Mono AOT；不以延期平台阻挡共享宿主和 Android。

## 已完成的交付

| 范围 | 结果 | 验证与提交 |
| --- | --- | --- |
| P0 内容闭包、P1 实现 | Editor 仅保留 galpak/内容文件夹；完整 v4 归档包含全部剧情分片，与目录导出等价；不保留旧安装格式及旁置剧情导出 | 集成 181 项、挂载导出 2 项通过；当时 Editor 全量有 2 项既有失败，原生分发 UI 验收仍待补。`5cc98d23` |
| P2 Windows 无 SDK 合包 | 布局元数据、严格契约/hash、纯运行包与内容组合、输入保护及 sh/ps1 薄入口 | 分发 CLI 合包和合成 Sample 原生启动/正常退出通过。`38988818` |
| P3a Windows 媒体 | 剧情视频、Gallery/缩略图和 Editor 预览共用 VLC 初始化工厂，播放器独立释放；完整默认契约部署 | 真实视频像素、Gallery、Editor 音频与包内依赖加载通过；历史全量测试的既有失败保留在执行记录。`42ecb004` |
| P3b 字体与 WSLg | 内置 Inter 默认字体、开放 CJK 回退；WSLg 条件选择 d3d12，保留用户 profile | Presentation 302 项通过；Sample 窗口/会话/退出通过，用户确认声音和 blur 人工复测正常，未测帧耗时。`fbac8cfc`、`455e1965`、`1e3c8cde` |
| Windows 发布稳定性 | 原子 Move 对短暂 Windows 句柄冲突有界重试，保持取消、旧输出和回滚语义；生成 dev.9 | Storage 32 项、Editor.Shared 494 项、两种分发 DesktopSmoke 和连续 40 次导出通过。`27c02fa5` |
| P3b Linux 本地候选 | Ubuntu 24.04 私有媒体闭包、Editor/CLI、纯/整合 Sample tar.gz 和 Contracts；保留权限、空目录及来源记录 | 独立解包 hash、真实 Editor/Sample 启动退出、包内 VLC/FFmpeg 视频帧、两种导出与安装通过。WSLg 证据不替代原生桌面和完整媒体验收。`27c02fa5` |
| P4 Linux 无 SDK 合包 | tar.gz/Unix 目录运行包和 galpak/内容目录输入，重建清单、保留执行位与空目录，拒绝不安全 tar 条目 | Editor.Shared 504 项、打包保护 8 项、WSL 两组真实合包及合成 Sample 正常启动退出通过。`d3b173fa` |
| P4 源码 build-compose | 显式应用工程、apphost、RID/版本/配置；SDK 检查、锁定还原、普通自包含发布、标准布局与严格合包；结构化退出码/日志、取消自有 SDK 进程树 | 历史 Editor.Shared 515 项、Windows 原生 1 项通过；Windows 宿主生成 Windows/Linux 包并运行。2026-10-30 Linux 原生 SDK 的两种内容输入及仓库外独立宿主也通过，详见下文 |

## 当前交付与证据

- Windows 应用候选：`out/release/0.1.0-dev.9/`。
- Linux 应用候选：`out/linux-release/0.1.0-dev.9/`。
- 新 Linux compose CLI 候选：`out/linux-compose-cli-dev10/`；旧 dev.9 应用包未重写，原包中的 CLI 不自动获得新能力。
- `build-compose` 位于当前源码 CLI 构建；未重新封存整套 Editor/CLI 发行包，旧候选不自动获得该入口。
- 精简验证与清理清单：`out/cross-platform-validation/20261029/`、`out/cross-platform-validation/20261029-compose/`；两次清理约 21.1 GiB、2.9 GiB，保留用户存档、手测安装和最新候选。
- 以上均为本地候选，不宣称精确 tag 的公开发行。操作见 [发行打包说明](../../docs/manual/release-packaging.md)，媒体边界见 [跨平台后端说明](../../docs/manual/cross-platform-backends.md)。

## P0 — SDL3 共存探针（Windows/WSLg 切片）

固定 SDL 3.4.18 及官方归档 SHA256，独立 Avalonia 自包含探针使用源生成窄绑定。Windows 官方 x64 DLL 与 WSL 输入专用源构建均完成 3 次 × 52 项检查、坏 native 哈希拒绝 exit 1 和正常退出 0；初始化/事件泵在 UI 主线程，仅 gamepad 及隐式 joystick/events，未启用 SDL video/audio。虚拟设备按键/轴、释放、断连、重新初始化与真实 Avalonia 焦点切换通过。源码/构建临时目录与运行副本自动清理，保留 native/许可/精简证据。

入口：[探针说明](probes/sdl3/README.md)；证据：`out/cross-platform-validation/20261029-sdl3/`。这是共存可行性结果，后续正式适配见下一段；真实设备、macOS、移动与 AOT 不在此探针验收内。

## P5 — 正式 SDL 手柄适配与部署（Windows/WSLg 自动化切片）

新增独立 `GalNet.Input.Sdl3`，使用自有源生成窄 C ABI 和固定 3.4.18.1 原生 NuGet（SDL 3.4.18），替换 XInput。中立快照归 Presentation.Abstractions；GameView 处理稳定设备选择、死区/迟滞、失焦或断连取消、回到中立再武装及实际按钮标签。GameHost 在 UI 主线程初始化、32ms 更新和释放，共享自有 SDL input 租约；缺库/版本/外部所有权冲突可诊断，键鼠不依赖 SDL。

最终 Windows/WSLg 各 3 次 × 40 项正式后端/定时器检查与 exit 0，另各 1 次缺库降级通过；包括真实焦点切换、热插拔、按住时断连、错误线程、共享租约、保留外部 events 引用与无关事件。完整 Presentation 313/313、相关输入 47/47、架构 53/53、发行合包 33/33 通过。五个本地 NuGet 候选完成实际构建，四类仓库外消费者编译运行，SDL native/清单及两份许可均传递到 Desktop/Avalonia 消费者；源码合包与维护者封装将通知放入 docs/ThirdParty/SDL3。

证据：`out/cross-platform-validation/20261030-sdl3/`；[正式探针入口](probes/sdl3-host/README.md)、[当前输入规范](../../docs/spec/control.md)、[部署说明](../../docs/manual/release-packaging.md)。Player/Developer 普通、Windows R2R 与 Linux Desktop 锁图已更新；本次 NuGet 是验证用本地候选，旧 dev.9 应用包、dev.10 CLI 和用户手测安装未更新。真实手柄、macOS、原生 Linux 桌面、最终应用发行矩阵与移动初始化仍待验收，P5 整体保持 in-progress。

## P4/P5 — 应用发布矩阵与 Linux SDK 宿主

新增共享 Python 入口与路径触发的 Windows/Linux/macOS Intel/ARM CI 矩阵，实际构建 Editor、CLI、Player、Developer，检查自包含运行时、目标原生头、SDL 清单/许可和同宿主共享契约。Windows 首次生成 Desktop/win-x64 图后，默认 locked restore 路径通过；Linux 使用官方 SHA512 校验的隔离 SDK 10.0.401 和无 Windows bin/obj 的源码快照，四种实际发布及 locked restore 均通过。实跑发现 Editor 的 Microsoft.NET.SDK 大小写在 Linux 无法解析，已改为 Microsoft.NET.Sdk。

证据：`out/cross-platform-validation/20261030-applications/`；[入口说明](../../scripts/ci/README-applications.md)。Windows 脚本保护测试 4/4 通过，覆盖已有输出保留、错误 SDK/native 拒绝、自有进程树超时及无关进程保留。macOS 两架构已生成锁图，未执行实际 macOS CI/运行/签名。原始 SDK 输出不等于完整私有媒体/最终归档，Linux 宿主 build-compose 与跨宿主构建上下文的契约字节等价仍独立追踪；不降低严格 hash 校验。

## 仍须保留的边界（当前）

P1 原生 Editor UI 两种导出、完整 Linux 媒体/Gallery/调试及原生桌面、公开再分发许可和对应源码可用性、最终桌面发行矩阵、跨平台模板和移动运行均未因此完成；手柄、macOS/签名及 iOS 验收按用户决定延期。当前定制工程验证限定于明确布局/契约协议，不推断任意 csproj；具体待办在剩余计划中。

实施约定继续有效：不考虑旧格式兼容；P6 前只维护现有 Sample 和公共库，桌面稳定后再集中接入 CrossPlatform；macOS 环境缺失不阻挡独立的 Windows/Linux 切片。每个可验证阶段完成后提交相关代码、测试和文档，将计划中已完成的步骤归并为本总结，保留未验收项及证据。

## P4 — Linux 原生源码合包与仓库外宿主

本轮设备为 Ubuntu 26.04.1 / WSLg，使用官方 SHA512 核验的隔离 SDK 10.0.401、`774b35f7` 的无 bin/obj 源码快照和既有原生音频库，完成 Editor/CLI/Developer Sample 普通发布。使用同一实际应用构建的三份契约创建作者项目，在 SDK 搜索隔离下导出 galpak/目录；两种输入分别完成真实 locked restore/publish/build-compose。另一个仓库外独立 csproj 只引用存储和契约工程，不引用 Sample 可执行程序，也完成严格合包并在无 SDK 环境读回正确 projectId/buildId。

三个归档均核对全部文件清单/hash、入口执行位及三份契约字节。两个 Sample 重新解包到含空格目录，从其他工作目录启动，确认包内 CoreCLR/VLC/SDL 加载、会话就绪及退出确认后 exit 0。Windows/Linux 发布保护测试各 4/4 通过。新增[可复现源码探针](probes/source-compose/README.md)。

证据：`out/cross-platform-validation/20261030-source-compose/`。媒体来自本设备已有 Ubuntu 26.04 开发依赖，逐项复核保留的 SHA256 和版权文件；没有冒充 Ubuntu 24.04 最低基线或经过公开分发审查的新候选，也没有更新旧 dev.9/dev.10。保持跨宿主源码上下文的严格契约边界；完整媒体/原生桌面和统一最终发行另行验收。
