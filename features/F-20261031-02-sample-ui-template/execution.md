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
