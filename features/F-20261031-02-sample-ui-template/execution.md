# 实施记录

## P1：基线与修订设计

提交：`65bf293d`。消费者、所有权、测试及未验证边界见 [baseline.md](baseline.md)。本阶段修正原生测试夹具遗漏的资源文案，不改变产品行为。

## P2：共享呈现状态

提取 `GamePresentationState` 及 Playback/History partial，持有原场景、对白/NVL/选项、等待、播放与历史交互机制。GamePage、各 presenter、视频、输入、GameRun/Presentation/Session、设置和调试保存接线使用该对象；音频公告也绑定该状态。

默认 GamePageViewModel 暂仅提供存读档、设置及返回页面的命令，继承状态并通过 Scoped DI 别名与运行链路共享唯一实例，不复制状态和转发全部属性。状态暂继承仅含 ObservableObject 的 PageViewModelBase，使旧导航页面继续可运行；P3/P4 将游戏页改为组合状态，P5 删除此过渡继承及旧页面适配，不作为最终公开应用模型。

GameActionDispatcher/PlayerInputRouter 使用活动呈现状态；应用页面动作以单个 `Func<string, Task>` 回调交回当前 shell，设备输入不再引用具体页面 VM。取消播放、首空槽保存及其活动状态检查仍在内置机制执行。

验证：

- `.artifacts/sample-ui-template/p2/presentation-final.trx`：367 total / 361 executed / 361 passed / 0 failed，6 项显式用例未执行，与基线的未执行范围一致。新增 3 项：作用域状态身份、无页面/导航的选项取消后恢复、应用动作回调与交互阻塞。
- `.artifacts/sample-ui-template/p2/integration.trx`：80 executed / 79 passed / 1 failed。失败名称及错误与 P1 相同：Editor 保存后的 IsDirty=true，随后 CloseRequestedAsync 取消。没有新增 Sample 集成失败。
- 受影响构建通过，现有场景鉴赏、模态输入、重挂载、存档与清理测试随上述回归执行。
- 修改/新增 C# 文件的 `dotnet format whitespace --folder --verify-no-changes --include ...` 通过；`git diff --check` 通过。solution 模式格式检查因受限命名管道无法连接 BuildHost，改用不加载项目的文件夹模式完成同一批文件的空白检查。

原生窗口关闭、真实窗口存读档和其他平台验收仍沿用 P1 缺口，不从本阶段 headless 结果推断通过。主题/普通页面仍为迁移前结构，尚未完成用户目标；后续按 P3–P6 继续，不把这次状态提取描述为 UI 模板改造完成。

## P3：项目标题、加载与游戏页切片

Sample 应用项目新增 Views/TitlePage、LoadingPage、GamePage 及其 VM，标题按钮与完整游戏工具栏在项目 XAML 展开；开始/继续和项目页面命令可以直接编辑。GamePageViewModel 继承通用 PageViewModelBase 并组合 GamePresentationState，不派生默认游戏 VM，不镜像呈现属性。SampleCustomization 在一个入口注册项目 VM/View、加载/失败回退与输入动作；稳定 GamePage 控件别名与实际页面解析为同一 scoped 实例。

内置层新增 IPlayerApplication，借用应用表面及启动/返回/退出策略；AvaloniaPlayerHost 仍拥有 root/scope、初始化取消、会话与释放，不解析具体页面 VM。外部调试开始改为调用项目应用的 StartNewGameAsync，避免绕过项目标题命令。GameShell 通过 IGameplayPage.State 获取交互状态，返回标题和应用输入由回调提供。GamePage.Presentation 与页面 DataContext 分离，内部呈现部件显式绑定运行状态。

GameLaunchFlow 不再依赖页面类型，接收三个呈现回调；仍保留加载最低停留时间和准备/播放分离。失败路径补充取消后等待准备任务结束，再回退 UI；门控回归检查准备清理尚未完成时加载流程不能提前返回。

迁移边界：保留短期 DefaultPlayerApplication、旧主题及其他普通页面，供 P4/P5 继续迁移；没有新增 Sample UI/VM 项目，没有启用运行期主题切换。语言选择/确认/公告仍使用当前通用视图，未复制其机制到项目。

验证证据（本地输出不提交）：

- `p3/presentation-final.trx`：370 total / 364 executed / 364 passed / 0 failed，6 项显式用例未执行。包括准备/呈现顺序、失败清理等待，以及禁止解析默认标题/壳/PlayerView 时自定义根表面仍能初始化、暂停和释放。
- `p3/integration-final.trx`：91 executed / 90 passed / 1 failed；唯一失败仍为 P1/P2 已记录的 Editor 保存后 dirty，不是新增失败。范围包含 Sample、PlayerInteraction、ResourceGallery、CrossPlatformWindow 和 SharedPlayerHost。
- `p3/template-debug-final.trx`：最终增量补验 6 passed / 0 failed。模板测试将旧标题/加载/游戏 VM 的解析显式设为抛错，仍完成应用启动命令、新游戏/继续、加载失败/取消、自定义页面进入/返回及工具栏点击不推进剧情。
- `p3/architecture.trx`：56 passed / 0 failed。
- `p3/native-final.trx`：显式原生用例 1 passed / 0 failed。正式 Sample 通过项目启动命令进入游戏，捕获 exact/reduced/default/scene/reset 五张不同场景 PNG，使用自有可见主窗口确认退出，进程退出码 0。该用例不验证实际窗口中的手动存读档或所有媒体/移动平台。
- `p3/captures/`：已检查标题页及中文最小窗口退出确认，与 P1 保持原布局；只作为当前默认布局参考，不声称逐像素相同。
- 修改/新增 C# 的文件夹模式空白格式检查、`git diff --check` 通过。

P1 原生退出缺口已定位并修正测试夹具：NativeSampleWindow 使用过时标题前缀 `GalNet CrossPlatform`，实际窗口为 `GalNet Avalonia CrossPlatform Player`。现在按实际标题精确识别，同时保留自有 PID、可见性和唯一窗口检查，没有放宽为任意窗口或以强杀代替退出。先前失败证据继续保留。

正式宿主/页面规范、Sample 定制说明、页面扩展示例与 Sample README 已同步本阶段事实。P4–P6 仍待实施，特别是其他页面与全局布局迁移、整套主题删除、仓库外模板及平台/发行验收。


## P4：完整项目页面与全局 UI（2026-11-01）

全部普通页面及项目命令已进入唯一 Sample 应用项目。Sample 不再调用主题的页面注册，也不解析旧普通页面 VM。旧 VM/主题/默认表面仍作为其他旧消费者的过渡适配保留，P5 删除；不把本阶段描述为整套主题已取消。

| 功能 | Sample 拥有 | 内置保留 | 验证 |
| --- | --- | --- | --- |
| 设置、关于 | XAML、VM、返回路由 | 设置分类/编辑器、持久化队列 | 本地化、设置保存恢复、独立宿主集成回归 |
| 存读档 | XAML、模式和保存/读档后路由 | SaveSlotCollection、单槽替换/重命名、Bitmap 释放、会话保存 | 项目行命令进入/返回、改名复用行、原存档持久化/缩略图回归 |
| Gallery 索引、CG/视频/场景网格 | XAML、实际内容类型注册与详情导航 | GalleryThumbnailCollection、行/缩略图生命周期 | 四类精确导航、缺失类型诊断、锁定项与名称/预览回归 |
| CG、音频、视频详情 | XAML、VM 激活与退出策略 | GalleryImageState、GalleryAudioState、GalleryVideoState | 原生 LibVLC 五项通过；暂停/拖动/继续/位置保留/释放 |
| 场景重播 | XAML、项目游戏布局、返回导航 | GalleryScenePlayback、独立 GamePresentationState、run 清理 | 两个真实 Sample 场景 headless 播放到边界且不落盘解锁/已读；门控启动取消/Dispose 各一次释放 |
| 截图 | ScreenshotDialog.axaml | 截图控件行为、预览缓存、正式捕获和平台输出 | 项目弹窗预览/取消/UI 模式切换/完整输出；取消不调用完整捕获 |
| 首次语言 | LanguageSelectionView.axaml | 语言选择等待与取消、本地化来源 | 等待期间释放、scope 重建、语言保存恢复 |
| 退出/返回、公告 | PlayerView.axaml | 无布局 PlayerSurface 的取消/焦点/输入屏蔽/自动恢复/解绑 | 双确认隔离、取消恢复、同页重挂载、公告动画与中文最小窗口截图 |

新增 AddAvaloniaGameView 只接受显式页面映射和 Gallery 注册，不强制完整页面列表。每条普通页面映射自动注册 VM/View Scoped 服务，减少组合入口重复声明。仍对已启用游戏和截图机制校验对应控件契约；错误包含实际模型/控件名。项目新增普通页面只需模型、视图、映射及导航调用。GalleryNavigationService 接受项目缺失类型呈现回调；GameShell 通过 IGameSavePreviewPage 能力捕获预览，切页复位设备选择，不引用具体设置/存档页。

项目 VM 组合六种内置资源所有者，未复制复杂解码、轮询与 run 生命周期。场景重播使用项目 GamePage 布局和独立状态，并屏蔽主会话菜单路由。PlayerSurface 是无布局行为基类，项目加载自己的 XAML 后 InitializeSurface；命名控件缺失时给出对应名称和类型诊断。未新增 Sample UI/VM 子项目。

提取时修正两项资源归属问题：缩略图异步请求持有本次取消源和行快照，旧结果不能借用新请求的取消状态；重播启动取消自行释放结果前清空 pending slot，后续 Close/Dispose 不再重复释放。四项门控测试覆盖列表替换/释放及重播取消/启动期间释放；经验记录见 L-20261101-01-gallery-generation-resource-owner。

验证证据位于 `.artifacts/sample-ui-template/p4/`（本地输出不提交）：

- `presentation-final.trx`：374 total / 368 executed / 368 passed / 0 failed，6 项显式用例未执行；包含四项新异步门控回归。
- `integration-final.trx`：96 executed / 95 passed / 1 failed；范围为 Sample、PlayerInteraction、ResourceGallery、CrossPlatformWindow、SharedPlayerHost。保留唯一基线 Editor dirty 失败，不修改无关 Editor 行为。
- `template.trx`：中途项目页面/存档/截图定向 8 passed；最终集成还覆盖新的启用机制契约诊断，共 9 项模板测试。
- `media-final.trx`（以及中途 `media.trx`）：显式原生 LibVLC 5 passed / 0 failed，两个 Sample 视频、两首音乐及 duration parsing 释放，最终自动注册产物复验通过。
- `architecture.trx`：56 passed / 0 failed。
- `native.trx`：显式原生 Sample 1 passed / 0 failed，五个实际场景 PNG、项目启动链路和确认退出，进程退出码 0。
- `captures-final/`：项目普通页面、公告、语言和退出确认截图；已查看设置页和中文紧凑窗口确认。默认布局沿用原 Classic，设置长标签的既有列宽不在本次迁移中重做。
- `format-final.log`：修改/新增 C# 文件夹模式空白格式检查通过；`git diff --check`。

中间失败保留：一次 XAML 数据模板误加页面状态前缀，编译定位后修正；一次新测试用 nameof(类型别名) 断言诊断文本，实际报告真实类型全名，改用 typeof(...).FullName。`integration-before-assert-fix.trx` 保留该断言错误与 Editor 基线失败，不隐藏新增测试问题。

本阶段不改变平台外壳、契约、发布图或 debug/player 编译边界。Windows 原生场景与 LibVLC 已验证；真实原生窗口手动存读档、Linux/Android 设备与发行矩阵留在 P6，不用 headless 回归代替相应验收。P5 继续删除整套主题及旧适配，只保留配色与内置控件默认样式。


## P5 进行中（2026-11-01）

已删除完整主题协议、三套重复布局和旧普通页面 VM/默认表面；Sample 自己创建普通 Avalonia Styles、加载项目 Styles/Player.axaml 与 Dimensions.axaml。配色库只保留 GamePalette/GameTypography 及颜色资源；Fluent 桥接只负责颜色。AudioGalleryRow、SettingsCategories 移入 GameView.Controls。GameShell 与截图服务不选择应用样式，Sample 为根/壳/截图窗口加载自己的样式。GamePresentationState 只继承 ObservableObject；GamePage.Presentation 显式接线，不再以 DataContext 推断状态。Sample 的运行 GamePage 通过实际页面映射解析并接入 State，替换控件时保证工厂与运行对象身份一致。

测试迁移：把原三种布局矩阵收敛到唯一模板，仍保留全部配色/输入/尺寸/异步行为断言。架构检查不允许 Presentation.Tests 引用 Sample，因此将依赖项目 UI/样式的测试及其本地化资源移入 IntegrationTests；纯机制测试保留在 Presentation.Tests，没有放宽架构规则。首次完整集成 333 passed / 2 failed：一项既有 Editor dirty，另一项为移动测试时漏随迁的嵌入文案，已定位并补迁，待最终回归。此前呈现集 311 passed / 3 failed 均为 UI 分类计数假设，已改为验证全部实际可见分类，UI 用例已随迁集成。

默认全解决方案 force-evaluate restore 已成功；其余 profile 锁图、最终回归/原生与文档仍在推进，不宣称 P5 已完成。

P6 环境发现：WSL 发行版为 Ubuntu；Android SDK 实际为 F:/Android/android-sdk，ADB 当前无设备（List of devices attached 为空）。已异步询问用户是否接设备，继续独立推进其他验收。不得将本轮旧 APK 或此前设备结果视为 P5/P6 新实现的真机证据。


### P5 最终结果

整套主题协议、三套重复页面、旧普通页面 VM、DefaultPlayerApplication/旧 PlayerView 与语言布局、GamePage 的 DataContext 状态兼容和 GamePresentationState 的页面继承均已删除。唯一应用页面在 Sample，配色库无任何 ProjectReference，仅保留 Avalonia Fluent 颜色桥接/资源与字体回退。GameView 保留稳定控件默认样式，不包含应用页面注册策略。普通样式/字体/尺寸归项目 Styles/。

Sample 的 GamePage 服务按实际 VM→View 映射解析，同 scoped 页面工厂实例与运行 surface 一致，并显式指定 Presentation。自定义控件映射不再被固定 Views.GamePage 别名绕过。所有运行库不引用 Sample 或旧普通页面类型，旧 API/资源 URI 搜索无残留。

最终证据位于 `.artifacts/sample-ui-template/p5/`：

- `presentation-final.trx`：225 total / 219 executed / 219 passed / 0 failed，6 个显式用例未执行。
- `integration-final.trx`：350 total / 335 executed / 334 passed / 1 既有 Editor dirty 失败，15 个显式用例未执行。模板 UI 相关测试及文案资源从呈现层迁入集成层，旧三布局的重复参数移除，保留唯一模板 × 全部配色/窗口/交互断言；不能用测试数量减少声称覆盖增强。
- `architecture-final.trx`：56 passed；配色库直接项目依赖 allowlist 收紧为空，没有放宽测试项目依赖规则。
- `native-media.trx`：6 passed，含真实 Sample 五个不同渲染检查点、确认退出码 0 与五项 LibVLC 音视频/取消释放。
- `captures/`：标题、游戏、各普通页、配色、弹窗、媒体控件与窄窗口渲染证据；已查看 Mono 标题，保持默认布局。
- `locked-profiles.json`：Desktop 四 RID × 两模式，Android/iOS/Browser 各两模式，共 14 组 locked restore 全部成功。默认解决方案、配色库 WindowsReadyToRun locked restore 成功。它们只证明图可还原，不代表 macOS/iOS/Browser 实际运行已验收。
- `palette-lock-scan.json`：全部源锁图不存在配色库到 GalNet 项目的旧依赖边。普通 locked restore 曾保留三个旧边，最终再次 force-evaluate 再 locked 后清零。
- 包版本没有升级。结构化核对发现两份 Browser 图的 SDK 隐式包 Microsoft.NET.Sdk.WebAssembly.Pack 10.0.12 生成 contentHash 与基线不同；保留本机工作负载真实还原结果，并逐份 locked 复核，未伪造旧哈希。Browser 运行仍不在本 feature 支持范围。其余共同包的版本/contentHash 不变，初始检查与差异记录在 lock-comparison.json。
- `format-final.log` 和 `git diff --check` 通过。构建/还原日志保留中间编译、测试分层和文案遗漏的失败记录；最终没有新增回归失败。

P5 已完成。后续持续执行 P6 仓库外定制、实际平台/发行与收尾；Android 当前无设备，不把 AOT 或 headless 验证当作真机验收。

## P6a：仓库外模板、桌面原生与交付验收

状态：verified（2026-11-01）。实现冻结提交 `ba1fd764fd794940c1f690a354841b6875c4f7a0`，本地验证版本 `0.1.0-dev.99`，没有上传包或发布版本。P6b 的 Android 本轮真机操作仍待设备，整个 feature 保留 implementation。

### 独立消费者

证据根 `.artifacts/sample-ui-template/p6/`。`Pack-NuGet.ps1` 实际生成六个包；`Test-NuGet.ps1` 的 `package-validation/package-validation.json` passed=true，独立验证包闭包、生成器、导出类型、原生加载和许可。没有把仓库 ProjectReference 构建冒充包消费。

`probes/external-template.py` 将唯一 Sample 应用和自有契约复制到仓库外，用六包中的真实 GalNet.Avalonia/Contracts/SchemaGenerator（可选 Debugging）消费，不包含内置库源码引用。`external-template.json` 记录实际目录与 8 个修改/新增应用文件；9 项已有模板回归加五项定制的综合用例，最终 10/10 通过：标题按钮纯 XAML、自有计数命令、Notebook 页面/返回、Rose 配色及 Magenta 局部按钮模板、游戏工具栏自定义且实际点击不推进。

首轮新增工具栏用例在页面动画完成前查找按钮失败；修正探针等待 `ResetToAsync`，没有修改产品实现。原始 `external-before-transition-fix.trx` 保留，不以重试掩盖引擎失败。

### 发布与真实窗口

- `publish-windows/application-publish.json`：Editor、CLI、CrossPlatform Player/Developer 的 win-x64 自包含 raw publish/布局/共享契约闭包通过。
- `linux-application-publish.json`：Ubuntu 24.04.3 内原生 SDK 10.0.401 从独立检出发布上述四角色，passed=true；并非 Windows cross-publish。SDK 归档 SHA512 与保存的官方来源记录一致，`linux-environment.json`/`linux-steps.json` 记录源码、命令与退出码。
- `native-windows-final/window/native-flow.json`：真实 HWND，passed=true，previewBytes=261359，存档/改名/读取/scope 重建、暂停重挂载、确认取消/接受全部 true。前后实际场景 PNG 和 profile 保留；最终可复现驱动 `probes/native-windows.ps1` 已实跑通过。
- `native-linux.json`：真实 XID，Ubuntu 24.04.3 / WSLg，完全相同流程 passed=true，previewBytes=261359，前后场景图保存在 p6 根目录。使用实际控件绑定命令/UI 线程，不是操作系统鼠标全流程；不将 WSLg 宣称原生 Linux 桌面发行验收。
- `linux-stock-player/verification.json` 与 `linux-stock-developer/verification.json`：原封不动的原生发布，两个窗口均完成 Session ready、进度落盘、X11 关闭请求和确认退出码 0。PATH/SDK 发现被隔离，maps 验证私有 CoreCLR、VLC 和 SDL。

Linux 原生 CLI 首次对 Windows 包的共享 Core 字节校验拒绝，保留 `linux-native-contract-mismatch.log`。最终独立 Linux 包消费者显式引用 Windows 消费者已经交付的自有项目契约产物与匹配内容；Linux 原生源码发布另用自己的契约集合导出内容。不混用两套身份，不放宽严格校验。

探针初次遗漏正式入口的 WithInterFont/默认 Inter 导致 glyphTypeface 失败；随后 WSLg 原生进程曾以 -11 退出。按正式入口补齐字体并沿用已有 d3d12 环境配置后完整流程成功；保留 `linux-native-before-font-fix.log`、`linux-native-segfault.json`。未修改产品默认渲染策略，未在没有原生栈的情况下断言单一崩溃根因。临时 `/tmp` 工作目录曾消失，后改用经边界检查的独占 `~/work`；不复用旧应用快照。

### 媒体、Android 与完整回归

- `linux-media.trx` 首轮 4 passed / 1 failed：MusicGalleryRetainsEachTrackPositionAndCanStartBySeeking(200) 暂停记忆为 0，预期 >1000。`linux-media-retry.trx` 独立两项音频 2/2；`linux-media-final.trx` 全部五项 5/5（两视频实际解码/暂停/定位/释放，两音频位置记忆与定位，时长解析取消）。仍保留首轮异常，原因未确认，不能称已修复或长期稳定性已证明。这些控件是 headless，媒体是私有实际 VLC；真实窗口证据单列。
- Android 用正常 r28 NDK 源码、Release、Mono、RunAOTCompilation=true、PublishAot=false、双 ABI、CI=true/version99 构建，未切换为单 ABI 锁图。首次缺少显式 NDK 路径的失败保留；补齐现有 NDK 后成功。再从本次 Android 契约导出完整 galpak，嵌入最终 APK，未关闭契约校验或全局抑制诊断。
- `android-package.json`：签名本地 APK 145667556 字节，ARM64/x86_64 各 114 个 AOT 条目；完整 `assets/game.galpak` 与导出相同，全部原生 ELF LOAD ≥16 KB、架构正确、VLC/miniaudio 存在、无桌面 SDL。deviceTested=false；不安装/卸载/覆盖用户设备。`android-bundled.log` 与 `android-publish-final.log` 为成功构建日志。
- 官方 `Invoke-CI.ps1 -Stage BuildTest -Configuration Release`：build 0 errors，15 个 TRX 合计 2562 total / 2541 executed / 2538 passed / 3 failed，21 项未执行；`ci/ci-results.json` passed=false，报告没有改写。Editor 两项（文案白名单和居中 258/8）与跨平台 feature 历史记录相同；Integration 为 P1 已记录的保存后 dirty/取消关闭。完整原始 TRX 和 `ci-counts.json` 保留。
- 不将 `Explicit` 未执行等价为通过；P5 Windows 实际 LibVLC 五项与原生 Sample 证据仍有效，P6 Linux 专项独立执行。macOS/iOS/真实手柄维持原延期，不用锁图存在替代实跑。

### 文档与自审

`summary.md` 记录最终实现、成本和限制，`review.md` 为明确标注的自审。修正正式手册残留“两个 Sample”“主题拥有普通页”“内置设置主题”，补充真实包消费者与冻结契约操作。`scripts/docs/sync_wiki.py check` 检查 72 页及本地链接/锚点通过；没有发布 Wiki。

代码统计 `code-lines.json`：基线 d2d8665a→ba1fd764，四个受影响生产工程的 C#/XAML 13215→12086，净减1129（8.5%）；Sample 832→2559，Themes 2899→262。不把删除总量当净减，也不把测试参数合并当覆盖增强。

## P6b：Android 设备验收待环境

状态：pending-environment。本轮两次 ADB 设备列表为空；用户尚未提供设备。剩余操作是使用本轮匹配内容/契约 APK，在保留数据和正确签名条件下验证挂载、后台重进、返回优先级及暂停落盘；不能卸载清数据绕过签名，也不能沿用旧 UI 的设备结果。连接设备后继续此阶段，合格后再将 P6/整个 feature 标记 verified/complete 并单独提交。
