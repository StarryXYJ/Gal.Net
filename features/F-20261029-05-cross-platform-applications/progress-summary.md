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
- 以上均为本地候选，不宣称精确 tag 的公开发行。操作见 [发行打包说明](../../docs/manual/publishing/engine-release.md)，媒体边界见 [跨平台后端说明](../../docs/manual/extensions/media-and-platform-backends.md)。

## P0 — SDL3 共存探针（Windows/WSLg 切片）

固定 SDL 3.4.18 及官方归档 SHA256，独立 Avalonia 自包含探针使用源生成窄绑定。Windows 官方 x64 DLL 与 WSL 输入专用源构建均完成 3 次 × 52 项检查、坏 native 哈希拒绝 exit 1 和正常退出 0；初始化/事件泵在 UI 主线程，仅 gamepad 及隐式 joystick/events，未启用 SDL video/audio。虚拟设备按键/轴、释放、断连、重新初始化与真实 Avalonia 焦点切换通过。源码/构建临时目录与运行副本自动清理，保留 native/许可/精简证据。

入口：[探针说明](probes/sdl3/README.md)；证据：`out/cross-platform-validation/20261029-sdl3/`。这是共存可行性结果，后续正式适配见下一段；真实设备、macOS、移动与 AOT 不在此探针验收内。

## P5 — 正式 SDL 手柄适配与部署（Windows/WSLg 自动化切片）

新增独立 `GalNet.Input.Sdl3`，使用自有源生成窄 C ABI 和固定 3.4.18.1 原生 NuGet（SDL 3.4.18），替换 XInput。中立快照归 Presentation.Abstractions；GameView 处理稳定设备选择、死区/迟滞、失焦或断连取消、回到中立再武装及实际按钮标签。GameHost 在 UI 主线程初始化、32ms 更新和释放，共享自有 SDL input 租约；缺库/版本/外部所有权冲突可诊断，键鼠不依赖 SDL。

最终 Windows/WSLg 各 3 次 × 40 项正式后端/定时器检查与 exit 0，另各 1 次缺库降级通过；包括真实焦点切换、热插拔、按住时断连、错误线程、共享租约、保留外部 events 引用与无关事件。完整 Presentation 313/313、相关输入 47/47、架构 53/53、发行合包 33/33 通过。五个本地 NuGet 候选完成实际构建，四类仓库外消费者编译运行，SDL native/清单及两份许可均传递到 Desktop/Avalonia 消费者；源码合包与维护者封装将通知放入 docs/ThirdParty/SDL3。

证据：`out/cross-platform-validation/20261030-sdl3/`；[正式探针入口](probes/sdl3-host/README.md)、[当前输入规范](../../docs/spec/presentation/game-view.md)、[部署说明](../../docs/manual/publishing/engine-release.md)。Player/Developer 普通、Windows R2R 与 Linux Desktop 锁图已更新；本次 NuGet 是验证用本地候选，旧 dev.9 应用包、dev.10 CLI 和用户手测安装未更新。真实手柄、macOS、原生 Linux 桌面、最终应用发行矩阵与移动初始化仍待验收，P5 整体保持 in-progress。

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

本切片提交：`3078ce0b`。

## P6a — 共享玩家集中接入与 Mono AOT 发布

新增 `GalNet.Sample.Player` 源码库，迁入主视图、语言选择、两种 UI 语言资源、截图 primitive 和公共注册/执行组合。原 Sample 保留桌面入口及调试工具；CrossPlatform 共享 App 复用同一库，具有 UI 线程会话范围、启动取消/异步清理、Activity 视图重挂载和 scope 重建入口。平台 galpak 流经正式安装器校验到 SHA256 分版缓存，profile 继续按 projectId 定位；Android/iOS 设置沙盒路径，不直接遍历 APK/bundle。

验证：原 Sample 与 CrossPlatform Windows 自包含宿主 2/2 原生启动、包内 CoreCLR/VLC/SDL、会话、进度落盘及确认退出通过；新 CrossPlatform Linux 宿主经过原生 SDK 发布、严格 build-compose、973 文件 hash 检查和 WSLg 正常启动退出。Android Debug APK 和实际完整 galpak 资产打包通过；Release MonoVM + RunAOTCompilation 在 ARM64/x86_64 各编译 116 个程序集，最终 APK 232 个 AOT 条目，嵌入内容与导出归档逐字节一致，无误入的桌面 SDL 库。未关闭链接/AOT 诊断。

冻结本 feature 源码后的标准 Release CI：构建/locked restore/发布保护通过，2,206 项测试通过，仅保留 P1 已记录的 2 项 Editor 既有失败；其中 Integration 186/186。并行 PAK/Core 两次提交进入真实 HEAD 后另跑 Debug Integration，191/191 通过。应用矩阵增加新 CrossPlatform 桌面宿主及源路径触发，真实 Win/Linux 输出通过相同 SDK 载荷检查；未宣称远端五应用/四 RID 新矩阵已全部执行。

证据：`out/cross-platform-validation/20261030-shared-player/`；最终本地 Android APK：`out/cross-platform-p6-android-final/org.galnet.sample-Signed.apk`。[宿主操作与边界](../../src/Samples/GalNet.Sample.CrossPlatform/README.md)。macOS/iOS/手柄验收继续延期；iOS 只有代码接入。Android 还需原生音视频/Gallery、系统生命周期、返回/触摸/显示及设备运行，模板还需脱离仓库独立生成/构建。P6/P7 未整体完成，旧 dev.9/dev.10 未更新。

## P7a — Sample 移动媒体与生命周期

独立模板按用户决定延后。移动宿主显式提供 VLC 工厂，桌面 Default 延迟初始化；Android 实际构建两 ABI miniaudio，iOS 接静态 ForceLoad/主程序符号。剧情视频、Gallery 三种主题和缩略图共用像素输出，消除 Gallery 原生子窗口依赖。共享后台流程串行写独立检查点/偏好/已读进度，再释放剧情/Gallery 媒体并回到标题，由“继续”重建稳定剧情状态；检查点不占玩家槽，显式新游戏/读档/清空会删除旧检查点。Android 音频焦点/路由、系统返回、安全区与 iOS 音频会话/中断/后台事件均已接线。

Windows Integration 193/193、Presentation 313/313、GameHost 10/10 通过，真实 VLC 场景/Gallery/缩略图与释放在 Windows/Linux 各 5/5。Android 真实 GameTestCase 完整导出后嵌入本地签名候选，ARM64/x86_64 各 115 程序集实际 Mono AOT，所有 native 架构/ELF LOAD 与 APK 的 16 KB 对齐、完整内容 hash 和无桌面 SDL 均通过检查。

证据：`out/cross-platform-p7-tools/`；最终 APK `out/cross-platform-p7-android-final/org.galnet.sample-Signed.apk`；可复现载荷检查见 [移动探针](probes/mobile/verify-android-package.py)。稳定 Android VLC 3.6.5 的 4 KB native 已明确失败，当前固定官方 3.7.0-beta（3.0.22-rc1），未压制诊断，须继续设备验证及稳定版替换。

没有连接 Android 设备，APK 本轮未运行；iOS 没有 Mac/Xcode 编译/链接/运行证据，生产签名/AAB、移动触摸/方向/性能及完整桌面验收仍待补。Linux 为 Ubuntu 26.04.1 隔离 SDK/既有本机媒体上的切片回归，不代替新整仓库发行、24.04 最低基线或公开再分发审查。P6/P7 整体继续 in-progress。

## P7b — Android 真机基础运行

2026-10-30 在 REDMI K80 Pro / Android 16 / ARM64 通过纯 USB 安装 Release Mono AOT 候选。修复首次内容初始化的目录链接报错：Android 先使用 `FilesDir.CanonicalPath`，保留严格链接检查；补启动日志回退和具体路径诊断。祖先链接保护回归 9/9 通过，新包两 ABI 各 117 程序集实际 AOT，完整内容/原生架构/16 KB 对齐通过；未改 native，复用与旧 APK 字节核对一致的音频库。

真机语言选择、剧情视频与立绘叠加已显示，用户确认声音和触摸推进正常，应用日志无未处理异常。候选及 hash/原生复用方式/失败记录见 execution.md P7b 与 `out/android-device-validation/`。此前 USB 掉线原因仍未确定；Rider 启动项缺失已通过安装 Android Support 插件解决。

基础 smoke 已完成；存读档、Gallery、音频焦点/路由、后台检查点及进程终止恢复、方向/安全区/性能、x86_64 运行和签名发布仍待补，不宣称移动完整验收。iOS/macOS/真实手柄继续按用户决定延期。

## P7c — 移动全屏与再次打开

移动端按用户决定隐藏退出按钮；Android 标题系统返回保存/暂停后退到桌面，保留可重新挂载的会话，不永久关闭共享 App。Android 收起状态栏和底部导航栏，并保留刘海/挖孔安全区；16:9 画布等比扩大到全高，长屏侧边留白仍保留。

修复重复切回同一 scoped 页面时清空当前 presenter 的空白页问题。三种主题失败回归先复现，再随完整 Presentation 313/313、共享宿主 7/7 通过；初次 4 项桌面音频失败来自测试目录缺 native，显式部署后全部通过。新 Release Mono AOT APK 双 ABI/内容/对齐检查通过，已安装手机；cold launcher 和 2×Back、1×Home 后同 PID 重进均显示正常标题，无退出按钮和常驻系统栏。证据和最终哈希见 execution.md P7c。完整移动与 iOS 验收继续追踪。

## P6b — 通用宿主与内容能力归位

按后续模板结构修订，把 `PlayerView`、语言选择 VM/视图和 `AvaloniaPlayerHost` 归入 Avalonia.GameHost；宿主通过注册回调提供页面、主题、本地化与设置，不依赖 Sample.Contracts。原桌面窗口直接使用通用视图，CrossPlatform 经临时 Sample 委托使用通用会话所有权。释放顺序明确在销毁本地化服务前恢复之前的解析器。

非可寻址流安装归 Storage.FileSystem；GameProfilePaths 保留 projectId/开发目录的原 SHA256 身份，必须由宿主提供 profile 根。SceneScreenshotWriter 负责场景捕获和安全写入，Sample 只保留项目 entry、文件名 schema 和任务适配。

Windows 自动化实际执行：Presentation 322/322、Integration 195/195、架构 54/54、Storage 34/34。独立宿主测试没有 Sample.Contracts 引用；原窗口的主题/语言和截图、后台检查点回归通过。CrossPlatform Desktop Debug 构建通过；14 组既有 Desktop/R2R 配置 locked restore 通过，包版本/hash 与目标框架/RID 键不变。明确区分 TRX 的实际执行与未执行显式用例。

规范/手册已同步 [宿主](../../docs/spec/presentation/game-host.md)、[模块边界](../../docs/spec/architecture/modules.md)、[定制](../../docs/manual/extensions/customize-sample.md)和 CrossPlatform README。证据在 `out/cross-platform-p6b/`，详见 execution.md。本轮没有重新发布/运行 Android、Linux、macOS 或 iOS；旧设备证据不升级为新代码通过。Player 的临时项目配置/资源及两个小型委托仍存在，P6c 删除；通用调试和最终发布设备矩阵仍待 P6d/P6e。

## P6c — 两个主项目独立定制并删除 Player

原 Sample 与 CrossPlatform 主项目各自拥有 `SampleCustomization`，直接配置主题/页面、字段编辑器、HostOptions、项目契约与执行模块；没有互相引用。CrossPlatform 直接使用 AvaloniaPlayerHost，平台目录/profile 规则仍由宿主提供。小型截图契约适配各项目自有，dispatch/取消/捕获写入机制共享。

通用 UI 文案进入 GameHost，项目标题/说明分别由主项目 Localization 拥有；FromEmbeddedResources 保留项目/外部翻译覆盖及文化回退。调试文案暂留原 Sample，待可选调试模块一起迁移。已删除 Player 源码、工程、解决方案/CI 路径和锁图，两个实际桌面 deps.json 均无其运行时依赖。

Presentation 实际执行 324/324、Integration 196/196、Architecture 55/55 通过；包括独立页面映射、不同项目标题/语言状态及 en-US/zh-CN 合并文案。14 组发布参数图强制重算后 locked 验证、普通玩家/开发版锁图和 CrossPlatform Desktop 构建通过；原包版本/hash 与框架/RID 键保持。既有媒体/检查点/截图测试通过，没有修改契约格式或 profile 键。

保留并修复 UI 聚合 provider 提前回退遮住外部默认语言译文的问题；额外发现普通 locked restore 可保留无用 Project 节点，增加源锁图 JSON 门禁。失败和最终证据在 `out/cross-platform-p6c/`，详见 execution.md。未重新运行移动/非 Windows 应用；可选调试及最终平台产物复验继续 P6d/P6e，不把本次结构完成当作完整模板生成或五平台支持。

## P6d — 可选通用调试

新增 `GalNet.Avalonia.Debugging`，统一调试命令/快照、500 行日志、可嵌入日志/确认 UI 和外部协议会话适配；只依赖通用 GameHost 与协议库，不依赖 Sample/项目契约。普通宿主没有反向调试依赖。两个 Sample 提供项目 replay 策略和平台重启/打开目录回调；原窗口继续使用独立日志窗口，CrossPlatform 使用不需要额外窗口的容器，移动隐藏打开目录入口。

两个 Sample 统一调试/启动参数编译能力，玩家版保留普通日志并排除调试模块、协议及资源。Activity 重建迁移完整调试容器，释放取消确认并拆下内容，日志异步更新隔离旧挂载。新增无 Sample.Contracts 的通用宿主测试实际连接 TCP，验证 inspect/stop/旧 run 拒绝与断开/重建。

Presentation 实际执行 333/333（包含工作区并行截图任务新增用例），调试版 Integration 197/197、玩家版 192/192、Architecture 56/56；显式原生用例未计入通过。四 RID × 两宿主 × 两 flavor、原 Sample 两种 R2R、两宿主普通图及 Android/iOS/Browser 两种图共 28 组 force-evaluate/locked restore 通过。200 份既有锁文件保留框架/RID 键和保留包的版本/hash；玩家测试图明确移除 DiagnosticsSupport 及两项专用传递依赖。CrossPlatform 开启/关闭调试实际构建与 deps.json 检查通过。

规范、定制说明和 CrossPlatform README 已同步，文档校验通过。证据 `out/cross-platform-p6d/`，失败复现及原始计数见 execution.md。尚未用新结构发布/运行 Windows/Linux/Android；iOS/macOS/手柄、移动外部调试与独立 NuGet/模板交付不因此完成。
