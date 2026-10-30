# 跨平台应用与发行设计

- 日期：2026-10-29
- 状态：P0/P1 仍有平台/原生 Editor 验收待补；P2 Windows 合包已实现，结果见 execution.md；桌面三平台及移动端完整发布尚未验收。
- 需求：[feature.md](feature.md)

## 1. 问题与目标

共享引擎、内容、Avalonia 展示与媒体 API 已具备可移植基础，但当前应用发行把 Windows RID、原生媒体、EXE 布局、脚本宿主和桌面生命周期绑定在一起。目标是让剧情作者无需 SDK 即可导出和发行内容，同时让修改宿主源码的开发者保留完整编译路径，并逐步交付桌面三平台和五平台模板。

本设计区分当前代码事实、拟采用决策和必须验证的条件。设计中的接口名与参数仅表达职责，不代表已有公开 API；稳定规则在实现验证后再同步 spec。

2026-10-29 用户修订：不考虑向后兼容。新内容导出/安装仅使用完整 galpak v4；旧版文件和旧式旁置剧情导出不保留兼容分支。下文兼容性相关原始建议以此修订为准，未触及的独立旧目录读取路径不构成本 feature 的兼容承诺。

## 2. 基线与候选方案

### 2.1 平台基线建议

| 目标 | 首批建议 | 发行形式 |
| --- | --- | --- |
| Windows | win-x64 | 自包含目录 ZIP，保留现有 lib/docs/pak 规则 |
| Linux | linux-x64，Ubuntu 24.04 LTS 起验证 | 自包含目录 tar.gz，保留可执行权限；记录实际系统动态依赖 |
| macOS | osx-arm64、osx-x64 分包 | .app 与同级 pak/docs 的归档；每个架构独立签名与验收 |
| Android | 首先 arm64 真机，模拟器按需增加 ABI | 平台工程生成 APK/AAB，原生库按 ABI 部署 |
| iOS | arm64 真机，模拟器独立构建 | 平台工程、静态原生链接、AOT 与签名 |

这是设计基线建议，不是已验证兼容声明。Linux 在基线系统上构建并记录 glibc、音频、字体、显示及 VLC 动态依赖；不把较新系统构建结果宣称为较老系统可运行。X11/XWayland 与原生 Wayland 的验收结果分别记录。macOS 最低版本由 .NET/Avalonia/原生依赖支持交集及构建结果确定，不凭 .NET RID 推定。

R2R 是单独发布优化：保留现有 Windows 通道，其他平台先验证普通 JIT 自包含发布。契约闭包不参与 R2R 转换。桌面不为本 feature 强制 AOT；iOS AOT 提前做风险探针。

### 2.2 视频后端选择

选用 LibVLCSharp + 各平台 LibVLC，优先延续已有视频能力。LibVLCSharp 官方文档明确它是基于 LibVLC、面向桌面和移动的跨平台 .NET 音视频 API；这支持继续采用 VLC，但不证明当前工程的部署与展示路径已在所有平台可用。

没有默认引入第二套播放器：桌面三平台优先同一 LibVLC 实现；Android/iOS 同样先验证 VLC。在具体原生绑定、回调、AOT 或性能验证失败时，记录证据后通过现有视频适配边界决定替换，不先按操作系统重写播放器。

候选与取舍：

- 依赖用户安装 VLC：早期开发验证方便，但用户 VLC 版本/插件与正式包难以一致，不作为预期的最终开箱即用方案。
- 应用私有 LibVLC：增加体积与依赖维护，但版本、插件和测试可控；作为正式发行方向。Linux 必须收集可重分发的依赖闭包并实测加载，不只复制一个 libvlc.so。
- 全部改用平台原生播放器：新增多套行为与场景合成适配，当前没有证据支持其成本；不作为桌面默认方案。

## 3. 模块职责与数据流

```text
Editor 作者项目 ──导出──> galpak / 内容目录
                              │
预编译运行包 ──────────────────┤
                              ├─> 安装、契约验证、发行清单、归档 ─> 游戏分发包
应用源码工程 ──目标工具链发布───┘

平台宿主 -> 应用路径/平台能力/生命周期/媒体部署
                    -> Avalonia GameHost / GameView / Rendering
                    -> GameHost / Runtime / Assets / 持久化
```

- Core/Runtime 不感知 .app、APK、窗口、SDL 或 VLC 部署路径。
- Storage/FileSystem 复用既有安装验证、内容读取和文件持久化；宿主提供内容与可写根。
- Media.Miniaudio / Media.LibVlc 管理原生边界；平台部署、初始化和媒体会话所有权由宿主组合明确。
- Avalonia.GameHost 组合共享会话与输入，平台能力以已有显示/媒体端口为基础补齐；不因五平台目标建立一套无实际调用者的通用 OS 抽象。
- Editor 继续作者职责，UI 隐藏应用组合发行入口；维护者与源码开发者调用外部工具。

## 4. 应用位置与跨平台发行

### 4.1 路径与包身份

发布清单增加明确的平台、架构、入口、托管/原生根、内容位置、契约位置和构建能力。路径均相对发行根并验证不逃逸；发布时生成、合包时读取，避免散落的 EXE 文件名猜测。

Windows/Linux 延续入口与 lib 分离。macOS 建议：

```text
Game/
  Game.app/Contents/
    MacOS/             原生入口
    Resources/         应用固定资源与布局元数据
    ...                按发布工具规则放置运行时和签名原生库
  pak/                 可替换的游戏内容
  docs/                发行清单、许可、说明
```

macOS 具体 managed/native 子目录由最小 bundle 发布验证确定，所有需要签名的二进制都在 bundle 内；pak 放在 .app 同级，避免普通合包修改已签名应用内部。宿主从 bundle 定位外层发行根，不从当前工作目录推断。移动端内容位置由平台宿主提供，不复用外置桌面 pak 猜测。

macOS 预编译合包模式保持原 bundle 名称及身份，不改 Info.plist 或签名资源。需要改应用名、图标、bundle ID 的开发者使用构建模式，再执行签名；源构建签名后不再修改 bundle。外部内容与最终归档另由发行清单校验，不能宣称应用签名覆盖外部 pak。公证凭据缺失时输出明确标记的本地候选，不能标为已公证发布包。

用户 profile、日志和媒体缓存保持可写用户目录，与安装目录分离；游戏 profile 沿用 projectId 身份，不因移动目录或换平台包版本意外重置。

### 4.2 Editor 应用选择与外部调试

新建项目所选应用需识别 Windows EXE、Linux 可执行文件和 macOS .app/内部入口，通过布局元数据找到严格契约。兼容当前普通开发目录，但多义位置报错，不任意挑 DLL。

调试直接启动可监控的目标进程，参数用结构化列表传递；macOS .app 解析实际入口后启动，避免通过不可监控的启动器丢失退出码/标准输出。保留 token 脱敏、连接超时、输出排空、会话身份及严格契约校验。

## 5. 内容导出与两种发行模式

### 5.1 内容完整性

P0 源码检查确认原导出仅归档本体，剧情分片旁置。按用户“不考虑向后兼容”的修订，统一导出 galpak v4：归档携带本体及全部已声明剧情分片，installation.json v4 索引基础 PAK、localization.pak 和剧情 PAK；game.pak 保持六文档、content v3/GPAK v3 不变。目录导出使用同一准备结果，因此 game.pak 和所有有效载荷与解包后的 galpak 一致。

不保留旧版安装或旧 API 转发分支。资源覆盖补丁保持显式独立导出，不从归档旁或作者工作区自动收集任意补丁；两种导出的 ResourcePatches 初始为空。内容复制复用安装器的清单、hash 和路径验证；不把普通 unzip 当作已验证安装。最终核对运行包的契约闭包字节身份。

### 5.2 工具与入口

避免 sh 与 ps1 各维护一套安装、hash 与路径逻辑。建议把发行组合核心放在复用现有 Storage/Editor.Shared 能力的 CLI 服务中，通过现有分发 Headless CLI 增加命令；独立工具包携带当前平台自包含 CLI。复用 CLI 不要求目标机器安装 SDK，Roslyn 等随应用依赖不等于外部 dotnet SDK。

sh/ps1 为薄入口：定位工具、转发参数并保留退出码。sh 直接执行当前平台 CLI，不要求 pwsh；ps1 支持已声明的 PowerShell 版本，源码构建过程再检测 dotnet SDK 和原生工具。若 CLI 依赖过大，后续可提取最小工具宿主，但首期不复制安装实现。

拟定语义（最终命令名在实施时确定）：

```text
compose --runtime <运行包或目录> --content <galpak或目录> --output <输出>
build-compose --app-project <csproj> --content <galpak或目录>
              --runtime-id <rid> --configuration Release --output <输出>
```

- 合包只读输入，验证布局、清单、hash、目标架构、契约和内容闭包，在新暂存目录安装并归档。
- 构建模式先以参数化进程调用发布，明确应用工程与作者项目含义不同；构建配置能传递调试能力和受控 MSBuild 属性。
- 自定义工程通过清单/布局适配约定提供入口、契约和内容定位；不假定任意 csproj 发布结果自动兼容。
- 平台原生库必须与 RID 一致，Windows 构建 Linux 时不能因宿主 IsOSPlatform('Windows') 误纳 Windows VLC。
- 最终验证成功后再发布最终输出；拒绝非空输出冲突，失败清理只针对本次自有暂存目录。
- ZIP 的 Unix mode 与 tar 的权限、符号链接分别处理；拒绝不安全归档路径和逃逸符号链接，合法 bundle 链接保留结构。归档恢复后再次验收入口可执行、动态库加载与许可完整性。

源码模式已落地的首个协议要求显式 apphost 文件名和版本，并将普通自包含 publish 包装为 lib/docs/pak。Windows entryPoint 位于 lib，避免猜测/重写任意 SDK apphost 的程序集相对路径；Linux 用根 sh 入口设置私有 loader 并调用 lib 中的 ELF。SDK/锁定还原/发布分步执行，额外参数受控，不开放任意 MSBuild 属性拼接；原生工具由项目构建或显式目标库满足。Windows 宿主到 Windows/Linux 已实跑，Linux 宿主 SDK、其他定制布局及 macOS 不由此推定可用。

## 6. 音频、VLC 与展示集成

### P2 已落地的发行边界

首个切片采用 docs/application-layout.json v1，声明 RID、入口、托管目录、契约目录及内容目录；原生库与运行时当前位于 lib，部署方式/开发能力复用 release-manifest 的 selfContained/sampleFlavor，不重复建立另一套能力表。compose 仅接受带完整发行清单的纯 win-x64 Sample（ZIP 或目录）、完整内容（galpak 或目录），输出新的 ZIP。其他 RID/归档形式留到 P3/P4；符号链接当前全部拒绝，合法 macOS bundle 链接保留尚未实现。

后续 Linux 合包切片已扩展为 linux-x64 tar.gz/Unix 目录输入及 tar.gz 输出，继续复用同一布局、严格契约和内容验证。tar 内 Unix rwx 权限独立记录，不依赖 Windows 暂存文件属性；拒绝 setuid/setgid/sticky、硬/软链接与特殊条目。启动脚本使用发行清单的 nativeEntryPoint 验证实际 ELF x64，两处入口均有执行位；直接 ELF 入口也可用。输出使用一个顶层目录，保留空目录并重建发行清单。Windows 主机仅通过 tar 输入保留 Linux 权限，不能从 Windows 目录猜测执行位。macOS 和源码 build-compose 继续待后续阶段。

内容 contractFiles 绑定加载 catalog 时捕获的闭包，导出检测磁盘变化，合包只做元数据/hash 比对，不执行项目代码。所有脚本复用自包含 Editor.Headless；sh/ps1 不承载解析/安装逻辑，也不提供 dotnet 回退。自包含 lib 内的框架依赖先复用默认加载上下文，避免项目 resolver 再加载 System.Text.Json 导致类型身份分裂。

实际 Windows R2R 原生窗口验证发现 SkiaSharp 3.119.4 在合成字体读取中崩溃；选择仅将该 DLL 保持 IL，应用和其余程序集继续 R2R。此为当前版本组合下实测缓解，不推定其他平台也需要同样排除。

### 6.1 原生音频

保留 PCM 混音/DSP 与 miniaudio。桌面发布按 RID 构建对应库，沿用 GalNetMiniaudioNativePath 部署入口，构建与输出按目标隔离。无法打开真实设备明确报错，不用 null device 冒充验收。移动端按 Android ABI 动态链接、iOS 静态链接分别部署。

### 6.2 VLC 平台配置

当前存在四类初始化入口：剧情 AvaloniaVideoPresenter/LibVlcRuntime、GalleryMediaService、GalleryThumbnailService、Editor AssetInspectorControlViewModel。统一为应用级的初始化配置与工厂，各自仍可拥有独立 LibVLC/MediaPlayer 会话；统一配置不等于跨生命周期强行共享同一个播放器。

配置职责：目标原生库路径、插件目录、必要平台初始化、版本与加载诊断、解码选项。由组合根提供；不把工作目录、用户机器已安装 VLC 或全局环境变量作为唯一加载依据。若平台加载器需要环境变量，必须在首次 native load 前集中配置并验证影响范围。

Windows/macOS 为 app-local native 部署；Linux 首轮允许系统 libVLC 开发验证，正式交付验证应用私有原生闭包或明确声明的系统依赖，不能将开发环境可用记作便携包通过。Android/iOS 需匹配 LibVLCSharp 当前目标框架的原生绑定、初始化与链接；上游支持列表不能替代 net10 平台工程试编译。

### 6.3 剧情视频与 Gallery

剧情视频现有路径为资源租约 -> 临时文件 -> LibVLC CPU 像素回调 -> SceneFrameSource -> Layer/ScenePost；继续保留图层、shader、跳过及释放语义，不直接换成覆盖在场景外的原生视频控件。

Gallery 播放与缩略图，以及 Editor 预览各有独立行为；需要同时接入平台初始化并验收。GameView 当前引用 LibVLCSharp.Avalonia，移动模板需验证其 VideoView 展示支持；即使原生 LibVLC 能解码，控件也可能需要改为共享像素展示或平台适配。这个问题属于展示集成，不等于 VLC 不跨平台。

移动先验证现有 CPU 像素回调路径的正确性、AOT 回调保留与性能。当前剧情视频使用 --avcodec-hw=none，不承诺硬件解码或零拷贝；高分辨率/高帧率吞吐不达预算时，再设计 GPU 互操作，不能降低层效果正确性冒充支持。

## 7. 统一手柄输入

保留玩家输入动作与按键映射，在设备采集边界引入中立的设备快照：连接身份、标准按钮/轴、布局类型。设备信息不进入 Core 剧情状态。SDL3 作为首选候选，选用可审核、能处理 AOT 的窄托管绑定；版本和 native ABI 固定到发行清单。

SDL 仅负责设备输入，不接管 Avalonia 窗口、渲染和主循环。初始化/事件泵遵守主线程要求，在 Avalonia UI 线程调度更新；仅启用所需子系统，验证 gamepad 初始化是否隐式依赖视频事件能力。不在后台线程任意调用 PumpEvents，也不创建额外游戏窗口。

共享动作层处理死区、按下/释放、导航连发、当前设备策略；焦点丢失/拔出时清空状态，避免卡键与后台操作。保留当前首个有效设备行为作为初始策略，多设备不混合重复触发；布局提示区别位置与字母名称。原生库缺失/初始化失败显示不可用并记录原因，不阻止键鼠游戏，也不能把可选降级记为手柄验收通过。

移动模板复用动作层；SDL 移动初始化与平台事件集成独立验证，不假设桌面程序集复制进 APK/IPA 即可使用。

## 8. 五平台模板

P6 当前实现（2026-10-30）：新增源码共享库 `GalNet.Sample.Player`，把原 Sample 的语言选择、本地化资源、主视图、截图实现和公共注册/执行组合迁入；原 Sample 保留桌面入口与调试扩展，并复用该库。CrossPlatform App 组合 `SamplePlayerSession`，由 UI 线程拥有会话范围、取消初始化并按顺序异步释放；原生窗口行为归宿主，Activity 重挂载移动现有视图，不再打开第二套会话。跨平台重启替换 scope，继续使用项目身份定位的 profile。

Android Asset / iOS Bundle 通过 `BundledGameInstaller` 将非可寻址 galpak 流写到临时文件、计算 SHA256 并交给正式安装器校验，按归档 hash 分版缓存；相同 projectId 的内容更新继续使用相同 profile。Android/iOS 明确排除传递来的桌面 SDL runtime/build 资产，移动输入后端另接。Android Release 使用 MonoVM + RunAOTCompilation，已完成两种 ABI 的实际发布；iOS 只有代码接入，后续验证。当前仓库工程集中复用已完成，独立模板生成和完整移动运行仍待后续，不以这些切片宣称 P6/P7 全部完成。

2026-10-30 用户修订：真实手柄、macOS、iOS 验收后移，共享宿主和 Android 可先推进，不再等待全部 P0/P5 平台证据。NativeAOT 不作为移动模板前提；优先沿用 .NET Android/iOS 支持的 Mono 运行时和 Mono AOT，在对应平台后续验证签名、裁剪、静态原生绑定和媒体回调。Mono AOT 与 NativeAOT 的开关和运行时不同，不能用桌面 PublishAot 自检替代移动发布，也不能靠全局关闭诊断声称兼容。

2026-10-29 执行顺序修订：`src/Samples/GalNet.Sample.CrossPlatform/` 已由用户初始化，包含共享、Desktop、Android、iOS 和 Browser 工程。目前共享 App 为模板页面启动逻辑，尚未接入游戏会话。复用此骨架，不另建同用途项目。Browser 保留，但不纳入本 feature 的支持范围。

P1–P5 期间，功能改动集中在现有 Sample 和公共库，不把页面、资源、本地化与会话组合复制到新项目。早期移动风险探针使用隔离的最小输入，不同步整套 Sample 内容。桌面功能稳定后，P6 集中接入最终 Sample 基线；能引用公共库的逻辑直接复用，只有组合根、平台资源与必要平台代码留在目标宿主。接入后记录实际保留的差异及归属，避免形成两套共享业务实现。

模板划分共享应用/页面组合与 Desktop、Android、iOS 平台工程。模板有独立身份、图标、bundle/package ID、内容部署声明与最小运行示例。共享业务不复制 Sample 的桌面 Closing/Open/重启逻辑。

平台宿主提供：

- 主视图挂载与 lifetime；关闭、返回、前后台、销毁和恢复事件。
- 内容来源与安装版本；初次将内容安装至沙盒或提供已验证的包内读取实现。
- 可写 profile/cache/log 根；临时媒体文件租约与清理。
- 显示能力、安全区域、方向与触摸；按能力隐藏桌面窗口大小设置。
- 媒体和输入原生部署、系统会话与生命周期映射。

移动暂停协调剧情推进、音频输出与保存；系统可能直接终止进程，不能依赖正常退出时最后一次落盘。启动恢复沿用明确检查点，不承诺任意后台瞬间恢复媒体游标。清空玩家状态后的“重启”实现为会话/导航重建，不启动新进程。

Android 处理音频焦点、返回键、路由及前后台；iOS 处理 AVAudioSession、音频中断/路由、静态链接与签名。Gallery 与剧情视频都必须覆盖。先做 iOS 最小完整发布探针，识别 JSON、DI、第三方 UI/视频回调的 AOT/裁剪问题；发现不兼容依赖时局部替换或显式注册，不全局吞掉警告。

## 9. 兼容性与迁移

- 隐藏现有 Editor 应用导出选项，不删除维护者 release 能力；内容导出命令语义保留。
- Windows 包布局、玩家/开发版能力、严格契约和 profile 身份保持各自明确规则；不为旧文件格式增加兼容层。
- 新建项目选择应用与调试适配承接 F-20261029-04；入口过滤不只支持 EXE。
- galpak 安装仅接受完整 v4，旧内容需重新导出；不降低严格清单、路径和 hash 校验来接受旧包。
- 更换手柄后端保留共享动作语义，Windows 原有按键操作回归；不双开 XInput 与 SDL 导致重复输入。
- 依赖升级、第三方绑定、原生资源和许可进入可复现清单；不以最新版本为由同时升级全部 UI 依赖。

## 10. 风险与验证影响

| 风险 | 需要的证据/退出条件 |
| --- | --- |
| 私有 Linux VLC 动态依赖不完整 | 无开发环境依赖的基线系统解压运行、视频/音轨/缩略图测试；列明未私有化系统库 |
| macOS bundle 加载或签名失效 | 两架构构建；移动目录、合包前后签名验证；具备凭据时完成公证与正常启动 |
| VLC 回调和 Gallery 控件移动兼容 | Android/iOS 发布、播放/跳过/返回/释放、缩略图及后台恢复；iOS 实际 AOT 产物 |
| SDL 与 Avalonia 事件循环冲突 | 不创建 SDL 窗口的三平台热插拔、焦点、重复导航和正常退出测试 |
| galpak 旁置补丁漏交付 | 归档/目录安装后同一 buildId 内容闭包与实际运行对照 |
| 合包校验无意依赖 SDK | 全新无 SDK 环境调用分发工具，检测预编译路径未启动构建 |
| 跨架构契约字节不一致 | 在目标发布矩阵核对契约 IL hash，不能仅按程序集版本接受 |
| 移动全量 PAK/视频内存超预算 | 真实资源量、分辨率和设备上的峰值内存、帧耗时及恢复结果 |

自动化验证按后端、打包、CLI、UI 层划分；已有 backend-portability 增加展示/宿主触发覆盖，并另建应用发布矩阵。正常退出、设备播放、IME/文件选择、手柄和移动生命周期要记录原生/真机证据。WSL2/WSLg 作为 Linux 早期运行证据，真实 Linux 桌面补发布前验收；Windows 环境不能代替 macOS/iOS 测试。

后续 Phase Plan 应给每项探针明确失败分支：不在当前环境可执行的验证保留未验收状态；失败不自动降级成“支持”，也不自动扩大到第二套后端重写。

## 11. 设计资料

本次已读取的上游资料：

- [LibVLCSharp 官方文档](https://docs.videolan.me/libvlcsharp/docs/home.html)：跨桌面/移动 API，LibVLC 与托管库分别安装。
- [Linux 设置](https://docs.videolan.me/libvlcsharp/docs/linux-setup.html)：原生库、插件和加载位置；其中旧系统命令不是本项目当前支持基线。
- [Android 说明](https://docs.videolan.me/libvlcsharp/docs/android.html)：平台专有压缩/保留配置，最终以当前 .NET Android 工程验证。
- [SDL3 初始化](https://wiki.libsdl.org/SDL3/SDL_InitSubSystem)、[事件泵](https://wiki.libsdl.org/SDL3/SDL_PumpEvents)：主线程要求。

仓库依据：Directory.Packages.props（LibVLCSharp 3.10.0 / Windows LibVLC 3.0.23.1）；Media.LibVlc/LibVlcRuntime；AvaloniaVideoPresenter；GalleryMediaService；GalleryThumbnailService；Editor AssetInspectorControlViewModel；Miniaudio native/CMakeLists.txt；GameLaunchOptions；DebugApplicationProcess；GalpakInstaller；docs/spec/architecture.md 与 docs/manual/cross-platform-backends.md。

### 2026-10-30 后续修订：Sample 优先与移动恢复语义

独立模板生成延期，不阻挡 Sample 功能；macOS/手柄验收延期，Android/iOS 先实现再验收。P7a 使用独立持久检查点与停止/释放 run 的方式处理后台和音频中断，回到标题由用户“继续”重建稳定状态；不承诺暂停任意 primitive 的墙钟或原地恢复所有媒体游标。后台检查点不覆盖手动槽，显式新游戏/读档/清空会移除旧检查点。Android/iOS 事件只进入共享宿主协调，不进入 Core。

Gallery 与缩略图改用与剧情视频同一 CPU 像素输出，消除原生 VideoView 子窗口的移动展示依赖。Android VLC 固定官方 3.7.0-beta 以获得 16 KB ELF 对齐（稳定 3.6.5 失败）；其预发布状态与设备验证是显式交付限制。iOS 静态音频、VLC 与 AVAudioSession 目前只有源码接线，须在 Mac 工具链完成编译链接与运行证据。
