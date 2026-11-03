# Avalonia 游戏页面

## 当前范围

`GalNet.Avalonia.GameView` 是官方共享的 Avalonia 游戏页面宿主。它提供 GameShell、GamePage、截图对话框行为基类及共享 ViewModel/服务；Sample 的普通页面布局及应用 VM 位于其单个应用项目，通过组合期注册表完成 ViewModel 到 View 的映射。

页面宿主不读取游戏文件，也不持有项目目录。宿主负责提供游戏内容、资源、存档、玩家变量、进度和媒体实现；编辑器预览与官方 CrossPlatform Sample 在各自的组合根中装配这些服务。

```text
宿主 / 编辑器预览
  └─ GameShell
       ├─ IGameNavigationService
       ├─ 标题、加载、存读档、设置、鉴赏、关于页面
       └─ GamePage + AvaloniaGamePresenters
            ├─ Dialogue / Layer / Animation / Particle presenter
            └─ CompositeGameView ── Runtime.GameEngine
```

`IGameNavigationService` 只管理一个游戏 Scope 内的页面状态和回退历史。`IPageViewRegistry` 在组合期建立不可变的 ViewModel→View 映射，`IPageViewFactory` 从同一 Scope 解析页面并设置 `DataContext`。页面导航参数通过 `IActivatablePageViewModel<TArgs>` 传递，不注册到 DI。

`Presentation.GamePresentationState` 继承 ObservableObject，持有场景集合、对白/NVL/选项等待、播放模式、历史重播及交互阻塞等运行状态，不是可导航页面。GamePage、presenter、输入、会话和设置直接消费该对象；Sample 游戏 VM 通过 IGameplayPage.State 组合同一个 scoped 状态。场景鉴赏使用独立状态和 run。

`GameActionDispatcher` 与 `PlayerInputRouter` 接收活动 `GamePresentationState`。设备无关的播放动作直接使用该状态；打开保存、读取、设置页及首空槽保存失败后的页面回退由应用动作回调处理，输入层不选择具体页面 VM。

Sample 的游戏页面 VM 已通过 `IGameplayPage.State` 组合该状态，GameShell 通过此能力取得当前运行交互对象。`GamePage.Presentation` 独立于页面 DataContext，内部场景/对白/NVL/选项/历史绑定该状态，项目工具栏可以绑定自己 VM 的命令。页面 DataContext 只负责项目命令和页面状态；设置 DataContext 不会替换 Presentation。

GameShell 的返回标题和应用输入动作由组合入口提供回调；GameLaunchFlow 接收加载、游戏、失败三个呈现回调，不选择具体页面类型。加载呈现完成且准备完成后才开始播放；加载失败时取消并等待准备任务清理后执行失败呈现，避免 UI 回退先于资源释放。

`AddAvaloniaGameView` 仅装配应用显式映射的页面和 Gallery 类型，自动注册普通页面 VM/View 的 Scoped 服务，不要求完整固定页面列表。启用的游戏和截图机制分别校验 GamePage/ScreenshotDialog 控件契约；无映射的路由按实际 VM 全名报告错误。Sample 的 Gallery 缺失类型诊断由项目回调提供。

存档页组合 `SaveSlotCollection`，内置对象订阅槽位更新并释放预览，项目 VM 决定保存后返回及读档流程。SaveSlotCard 的激活按钮在预览图片下层；图片设置 IsHitTestVisible=false，保证应用按钮样式不会遮挡预览，图片区域点击仍能激活槽位，名称和高亮位于上层。Gallery 页面组合 `GalleryThumbnailCollection`、`GalleryImageState`、`GalleryAudioState`、`GalleryVideoState` 与 `GalleryScenePlayback`，内置对象负责预览、轮询、取消与资源释放，不选择具体应用页面。缩略图请求捕获本次取消源与行快照，旧请求不能回写替换后的列表。场景重播等待 pending startup 结束后才释放；已由启动取消路径释放的 run 不再留在 pending slot 中，避免重复释放。

Sample 使用配色库的 `GameTypography.UiFontFamily`，经 `GalNet.Theme.FontFamily` 资源应用于玩家根，跟随宿主默认字体。各平台显式中文回退统一优先 Noto Sans CJK SC / Noto Sans SC / Source Han Sans SC / Source Han Sans CN，不指定微软雅黑或苹方；未命中时仍由系统匹配。Sample 的 AppBuilder 配合 `WithInterFont()` 使用 `CreateFontManagerOptions("fonts:Inter#Inter")`，以自带 Inter 作为默认字体，并统一所有应用 Window 的字体；显式日志等宽字体保留。Inter 不含中文字形，目前未打包中文字体文件；中文需要已安装的 CJK 字体或宿主自行部署，目标平台实际显示仍需验收。Editor 自身字体不在本次范围。

需要返回行为的页面继承 `BackNavigablePageViewModel`，获得共享 `BackCommand`、受保护的 `Navigation` 和可重写的 `OnBack()`；默认通过导航历史返回。设置、存读档、鉴赏及其详情、关于页已接入；视频详情的 `OnBack()` 先停止播放。标题、加载和剧情页不默认开启返回。页面 XAML 继续绑定 `BackCommand`，无需绑定键盘事件。

`GameShell` 始终组合 `PageBackInputListener`，即使宿主未提供可配置输入设置也支持默认 Esc 返回。模块在所在 TopLevel 的 bubble 阶段处理未消费的纯 Esc，执行当前返回页面的同一个命令；快捷键录入、下拉框和其他已消费 Esc 的内部交互优先取消，不同时退页。按住 Esc 不连续退多页；窗口失活、卸载及 Dispose 清理监听/按键状态。壳外宿主控件不触发该返回行为。手柄页面返回也复用当前页的 `BackCommand`。

## 页面与组合

Sample 的标题、加载、游戏、设置、存读档、Gallery/详情/诊断及关于页全部位于项目 Views/ViewModels。项目 VM 定义业务命令和明确路由，复用内置呈现、存档行、缩略图、媒体和重播状态。

同一份 Sample 工程通过 GalNetUsePackages 条件切换内置源码与包依赖。独立复制清单包含平台入口、自有契约及必要构建附件，交付根用 GalNetPackageVersion 选择四个同版本 GalNet 包，不复制引擎 C# 工程或重写 csproj。具体复制、包源和构建步骤见[定制手册](../../manual/extensions/customize-sample.md)。

`AddAvaloniaGameView(configureViews, configureGalleryPages)` 为显式映射的普通 VM/View 注册 Scoped 服务，不要求固定全量页面。注册表不可变，映射按实际 VM 类型查找；导航到没有映射的类型会报告其全名。启用游戏/截图能力时分别要求 GamePage/ScreenshotDialog 行为控件，截图窗口每次构造并释放。Gallery 只按显式内容类型匹配，缺失类型由应用的回调显示诊断。

### 项目样式与配色

配色库仅提供 GamePalette 和 GameTypography，不包含页面和布局注册协议。SampleCustomization.CreateStyles() 创建独立的 Fluent 样式、语义配色及项目 Styles/Player.axaml、Dimensions.axaml。Sample 在自己的播放器根、导航壳和截图窗口应用这些资源；内置 GameShell 和截图服务不选择应用样式。新增页面基础通过模板源码表达，不引入运行期主题切换。

资源分为 GalNet.Palette.*Color、GalNet.Brush.* 语义颜色，以及应用赋值的 GalNet.Theme.* 尺寸/字体键。GamePalette 校验 21 个必需画刷；Fluent 颜色桥接要求对应 Color 或纯色画刷。控件库保留中性默认资源；根资源可覆盖，不能在多个根复用同一个 Styles 实例。

页面容器和返回按钮位置、对白人物名/正文字号等由项目 XAML/资源决定。当前默认页面保持顶部页头右侧返回、独立媒体区域；桌面默认“较小”档的人物名/正文为 26/22 设计字号。局部按钮可直接写 XAML、样式或 ControlTheme，不要求实现整套主题。

普通按钮的hover/pressed只改变中性背景：ControlHover/ControlPressed分别以TextPrimary颜色的0.08/0.16透明度叠加，前景和字重不切换；主要按钮保留自身基色，Mono按下为灰色而非纯黑。变体默认值在前、交互状态在后，禁用控件不显示hover。Sample范围的Button/ToggleButton取消基础主题的按压缩放与变换过渡，键盘焦点保留细边框。

### 玩家字号与富文本字号

Sample 的 `PlayerTypography` 提供 Small/Smaller/Larger/Large 四套 `PlayerFontSizes`，分别配置 Title、Heading、Body、Emphasis、Action、Auxiliary、Dialogue。默认桌面 Smaller、Android/iOS Larger；项目可修改各档数值和平台默认档。资源只应用于项目 surface、shell 及截图弹窗；标题/表单/工具栏/存档和媒体信息使用对应角色，ADV/NVL 使用 Dialogue，姓名使用 Emphasis，历史保留其原有字号层级并跟随对应角色。开场 Logo/公告演出保留独立排版，Editor 和调试工具不接入。每个最终资源值要求有限且在 1–512；一次应用先校验整套值再更新资源。

这些数值是语义字号：普通页面按逻辑单位使用，固定画布中的对白、角色名和选项按设计单位使用，场景缩放仍会影响它们的实际显示大小。普通页标题复用 Heading，返回复用 Action，设置分类使用独立语义字号（默认较小档为24），页脚复用 Auxiliary，不再由页面布局另行封顶。字号档位不代表最小触摸尺寸。场景坐标和画布比例保持不变，大字号长文本及移动端实际阅读效果须单独检查。

`RichTypewriterTextParser` 支持下列标签，大小写不敏感，可带单/双引号，使用 invariant 小数点：

| 写法 | 语义 |
| --- | --- |
| `<size=1.25em>文本</size>` | 父级字号的 1.25 倍 |
| `<size=125%>文本</size>` | 父级字号的 125% |
| `<size=+4>文本</size>` | 父级字号加 4 个设计单位 |
| `<size=-2>文本</size>` | 父级字号减 2 个设计单位 |
| `<size=24>文本</size>` | 绝对设计字号 24，不跟随玩家基础字号 |

带符号的无单位数字为偏移；em/% 表示比例本身，必须为正。嵌套字号相对于父级计算，结束恢复父级；例如基础 20，`+4` 内嵌 `150%` 得到 36。非法数值、非有限值、不支持的单位及无匹配起始标签的 `</size>` 保留字面文本。不扩展 span 属性语法。与粗体、斜体、颜色、换行、延迟和 skip 指令共存。

Core token 保留原位置构造/解构参数，新增 `FontSizeScale`（默认 1）和 `FontSizeOffset`（默认 0）init 属性；宿主按 `基础字号 × Scale + Offset` 解析。Avalonia 最终 Run 字号限制在 1–512，基础 FontSize 更新只修改当前 runs，不重启打字任务、不重触发完成事件；换行和测量由更新后的文本布局处理。ADV、NVL、历史共用相同解析/渲染路径，持久化正文仍保存原始标记。

### GamePage 模板契约

`GamePage` 是稳定的 TemplatedControl，拥有 `Scene`、`SceneHost`、`Dialogue`、`Nvl`、`Choice` 内容及输入行为。其 ControlTheme 必须包含 `PART_Scene`、`PART_Dialogue`、`PART_Nvl`、`PART_Choice` 四个 ContentPresenter，并用 TemplateBinding 呈现对应内容。不得创建第二套核心 presenter 或重复挂载内容；缺失或错误承载点在模板应用时明确报错，阻塞呈现前调用 EnsureLayout。

实现 IGameplayPage 的页面 VM 对应 View 必须继承 GamePage；DI 中的 GamePage 别名与页面工厂解析同一 Scoped 实例。只变更布局推荐覆盖 ControlTheme。模板重建及运行期主题切换不在当前契约内，NVL/打字机内部部件仍保存呈现状态。

页面识别按钮、SelectingItemsControl、TextBox 和 RangeBase 的交互；自定义交互区域设置 `GamePage.IsPlayerInteraction=true`，避免点击操作同时推进剧情。非交互区域继续调用 Presentation 的推进逻辑。

### 截图对话框契约

主题把 ScreenshotDialogViewModel 映射到 ScreenshotDialog 派生 View。XAML 加载后调用受保护的 InitializeDialog，布局必须提供 Preview(Image)、DirectoryBox/TextBox、FileNameBox/TextBox、IncludeUiBox(CheckBox)、BrowseButton/CancelButton/SaveButton(Button)、ErrorBlock(TextBlock)。缺失控件明确报错。基类管理选择目录、预览更新、保存、取消和 Bitmap 释放；服务通过 DI 创建独立实例，并加载当前主题样式。宿主可在 configureViews 中覆盖截图 View，构造参数可注入宿主服务。

默认截图服务需要同一 TopLevel 内的 Ursa `OverlayDialogHost`；普通窗口或移动单视图可用 `UrsaView` 提供宿主。调用按请求 Owner 的 TopLevel 身份选择弹层，不从其他玩家窗口借用宿主。

宿主可注册 `IAutomaticGameScreenshotDestination`，接收游戏名、自动生成的 PNG 名称、完整 PNG 字节与取消令牌。存在此服务时，`ScreenshotDialogViewModel.CanChooseDestination=false`，内置主题隐藏目录和文件名表单，保存只调用平台输出，不访问桌面图片目录。失败保留弹窗供重试；没有该服务时继续提供桌面路径选择。自动输出模式限制弹窗宽度和预览高度，并随 TopLevel 尺寸变化重新约束布局。自定义截图布局应按 `CanChooseDestination` 隐藏相关表单，并使预览行采用 Auto 尺寸。

`GameScreenshotRequest.CapturePreviewPngAsync` 是可选的预览回调，未提供时复用原 `CapturePngAsync`；正式保存始终调用后者。GameShell 的预览按原比例限制到 720×405，不放大；“包含对话框和操作栏”两种状态在当前弹窗内分别缓存，失败可重试。正式导出使用捕获目标的完整逻辑尺寸（场景与含 UI 的范围见移动页面说明），PNG 编码在后台完成。`SceneLayerHost.CaptureControl` 和有尺寸上限的 `CapturePng` 同时限制场景中间纹理与 CPU 效果处理像素，图层位置和原生图片尺寸仍使用原逻辑坐标；剧情截图入口的无参捕获保持原行为。

退出/返回标题确认通过顶层遮罩压暗背景并拦截指针输入；焦点循环与 GameShell 导航范围隔离键盘和手柄操作。宿主可用 `PlayerSurface.DisableContentDuringConfirmation` 控制是否同时禁用底层 PlayerContent（默认 `true`）；Sample 设为 `false`，避免底层控件进入禁用视觉状态。模态期间底层区域标记 modal-background，关闭时移除此标记。

### 自适应页面与工具栏

普通页面使用 Avalonia 已处理 DPI 的逻辑视口，以 1600×900 为尺寸参考。PlayerView 用一个 Viewbox 等比缩放同一个 GameShell：常规缩放为 clamp(min(width/1600,height/900),0.65,1.25)，移动端下限 0.7；移动端 RenderScaling <1.5 时增加 8%。PageDesign 的宽高为实际视口除以缩放值，保持实际纵横比，页面内部继续根据内容可用空间自适应。字号、按钮、间距和边距共同缩放，不再给每个元素独立计算窗口比例；字号资源本身仍使用玩家档位值，避免重复缩放。

只有首页根据视口纵横比切换水平对齐：横屏标题和菜单靠左，竖屏标题、进度提示和菜单居中。标题在上、菜单靠下的纵向构图及同一组按钮保持不变；标题允许自然换行，菜单宽 380、最小高 46、间距 10 均为统一缩放前的设计值，不因方向改变。根主体最大宽 1600。

其他普通页面没有独立横竖版。统一页边距为 48×36 设计单位，单行页头最小高 64、内容间距 20；普通按钮设计最小高 40，移动端 44。固定布局尺寸集中在 Dimensions.axaml，字号集中在 PlayerTypography；PlayerView 测量只调整 PageDesign 宽高，不覆写静态布局或字号资源。已删除旧 compact-player 样式分支。

设置页整体置于居中的最大 800 设计单位区域，标题、分类按钮、正文与页脚提示共用左边界。一级 Tab 左右各留 12 单位内边距，项间距 8；选中用粗体，不加持续底色或下划线，hover/pressed 保留轻灰反馈。标题模板预留粗体宽度，切换不移动相邻标题。操作页设备 Tab 使用 Action 字号、常规字重和浅灰选中底，不显示下划线；设备 Tab 正文 Padding 为 0，与分类面板左边界一致。分类保持单行、必要时横向滚动；正文独立滚动，各分类记住偏移。ScrollViewer 的 DataContext 复用时也恢复正确分类位置，原生键盘选择保留。正文显式 Stretch，避免表单按最短控件的 DesiredSize 收缩。SettingRow 统一管理标签列 260、列距 32、上下排时的间距 8；行最小高 44、行间距 16，仅行宽 <600 时自然上下重排；全宽项不保留空标签行。数字输入宽 160，下拉框 280–360，二者靠左；滑条最多 520 且保持可用轨道，内部编辑器与输入列起点对齐。输入绑定保持现有内容重排及捕获状态。

存档与图片、视频、场景鉴赏共用 AdaptiveCardGrid，按容器宽度/240 减列，最多使用 CardColumns（默认4）列。CardHeight 默认210，卡片外边距7、列距6、行距8，底部标题区域最小高84。三类鉴赏共用 GalleryCardTemplate，保留各页打开命令、名称提示及锁定隐藏名称；预览和标题在点击按钮上方绘制且不拦截命中。这些页面共用标题、右侧返回按钮及分隔线位置；设置页按自身居中表单宽度排列页头。

游戏页与场景鉴赏不套普通页面缩放，GameShell 在这两种活动页中保持实际视口。PlayerApplication 随导航切换同一 PageDesign 的尺寸，解绑时取消导航订阅；不复制页面、VM 或 presenter。游戏 scene/dialogue/NVL/choice 仍由内部 1920×1080 画布呈现，工具栏和包含 UI 的截图继续采用实际视口。启动可见性仍跟随原 GameCanvas，PlayerSurface 的确认、演出覆盖层和安全区所有权保持不变。

GamePage 的 scene/dialogue/NVL/choice 在内部 1920×1080 GameCanvasHost 中等比显示，历史是工具栏之上的全页阅读覆盖层。工具栏在实际视口居中，优先留在场景宽度内，按钮确有需要时才使用两侧黑边，总宽不超过视口减左右各 8 逻辑单位。操作条以不超过场景宽度的 320 逻辑单位为最小舒展宽度，按实际内容增长，不强行铺满场景。完整工具栏放不下时收纳为快进、自动、历史（可用时）、更多，更窄时历史进入更多；命令不丢失。放宽恢复完整工具栏时关闭更多菜单。

工具栏按钮最小宽 48、高 32；工具文字按画布比例折算（比例下限 0.5），限制为 12–18，菜单为 14–18 逻辑单位，不修改玩家字号档位。工具栏整体及按钮静止背景透明，文字固定浅色，轻微黑影仅作用于文字；Sample 的 hover、按下与 checked 背景分别使用半透明浅灰 `#A678818C`、`#A666707B`、`#A6818A96`。交互不改变字色、字重、字号，不使用下划线或按压缩放；键盘焦点使用预留的1像素边框。工具栏底部锚定显示场景的下沿，竖向黑边较大时也靠近对白，不远挂在视口底端；横屏无竖向黑边时贴近视口底部。对白与按钮区域保持约 2 逻辑单位净空；仅工具栏隐藏时恢复原 70 设计单位底边距。对白横向位置、宽度、字号和场景坐标不变。

更多菜单复用原命令，打开取消自动/快进，返回先关闭，切页/卸载清理。按钮 Flyout 打开期间 PlayerInputRouter 不分发剧情设备动作，不改写共享 IsInteractionBlocked；菜单命令仍可直接执行。工具栏透明空白区标记 IsPlayerInteraction，TouchInputRecognizer 排除该区域与 RangeBase，并清理待双击判定的单击，避免关闭菜单后延迟推进。

剧情触摸手势捕获参与的指针；指针捕获丢失时取消当前联系人和待判定单击，释放其余捕获，使系统中断后的下一次触摸从新手势开始。

仅场景 PNG 仍为设计空间（默认 1920×1080），存档预览仍为 480×270。含 UI 截图捕获完整 GameShell，所有平台均为当前内容视口逻辑像素尺寸，包含黑边和工具栏；预览继续限制 720×405。平台安全区不重复扣除。当前普通页面自适应没有改变对白的固定画布语义，小屏阅读字号仍受场景比例影响，独立竖屏阅读布局尚未实现；真实挖孔、系统手势和软键盘仍须设备验收。

### Gallery 页面

Gallery 使用独立的 `IGalleryPageRegistry`，以精确 Gallery `typeId` 映射到页面 ViewModel 和 View。`Add<TViewModel, TView>` 注册新类型，`Replace<TViewModel, TView>` 显式覆盖既有类型；多个 type ID 可以映射到同一页面。该注册同时写入普通 ViewModel→View registry 和 DI，构建后不可修改。

### 手柄焦点导航

`PlayerInputRouter` 将手柄输入限制在当前页面或活动交互范围内。普通页使用 GameShell 当前实际页面，不搜索转场中的旧页面；剧情分支、历史和跳过确认分别独占自己的控件范围。宿主通过 `GameShell.SetNavigationScope(control, cancel)` 将壳外模态临时接入，关闭时传 null；首次语言选择、退出/返回标题和默认截图覆盖层均接入。退出确认的底层控件禁用可由 `PlayerSurface.DisableContentDuringConfirmation` 配置；顶层遮罩负责阻断指针输入。自定义截图服务可使用 `GameScreenshotRequest.SetNavigationScope` 同样交接，并须在 finally 归还。

Start 是保留菜单键：剧情阅读时进入布局标记的工具栏，停止自动/按住快进并恢复隐藏 UI；再次 Start 或当前布局的取消键退出工具栏返回剧情。打开的 Flyout 优先接收方向/确认/取消；取消先关闭菜单，下一次再退出工具栏。剧情分支不能取消到后台剧情。菜单模式期间不分发剧情设备动作；指针交互退出手柄工具栏模式。旧偏好只移除包含 Start 的手柄绑定，其余配置保留；新默认不再用 Start 直接打开设置。

布局可使用 `PlayerNavigation.IsToolbar` 标记工具栏容器、`IsDefault` 指定默认焦点、`IsTarget=false` 排除内部编辑器。默认候选为实际可见/可用的按钮（含 Toggle/CheckBox）、下拉框、滑条、Tab、数值框和文本框，不写死数量或列数。方向按当前布局中心和行列交叠寻找目标，到边界循环；焦点使用 Directional 显示焦点提示并 BringIntoView。每个范围记住最近有效焦点，失效时回退默认/首项。肩键循环切换离焦点最近的 TabControl；方向斜推保持已有方向，不作为组合动作提交。

确认走控件原生激活路径，Click、Command、切换和 Flyout 各执行一次。滑条/数值框左右调整，数值边界不循环；上下切换控件。下拉框确认展开、方向预览、再次确认提交，取消或输入中断恢复；共享字段编辑器通过 `DefaultFieldEditors.IsSelectionPreview` 阻止预览写入设置，输入配置面板同时沿用 `IsPreviewingChoice`。Sample 存档卡的名称编辑器不参与手柄方向导航，保留键鼠改名。

`ChoiceList` 的 SelectedIndex/PreviewSelection 只改变高亮；CommitSelection、Enter/Space 或实际条目的点击才提交剧情选择。手柄焦点只在此列表中循环。历史无语音目标时上下滚动，有语音按钮时可方向选择重播；LT/RT 按视口翻页。失焦、断连、页面切换、卸载和释放清除连发与未提交输入。系统目录选择和文字输入继续沿用系统/键鼠，不提供模拟鼠标或软键盘；移动端原生手柄源尚未接入。

### 设置与界面文本

Sample 新增 `operation`（操作）分类，与游戏、画面、音频并列。它借用现有 `CustomSetting<PlayerInputConfiguration>` 和可释放编辑器租约，承载键鼠、手柄、触屏三个局部 Tab；`SettingItem.IsFullWidth` 允许此类编辑器跨设置行标签列展示。键盘/鼠标共用录入控件，手柄支持按钮组合，触屏通过 ComboBox 选择离散手势。偏好经原 SettingsCoordinator 保存到 `player.galnet.input.bindings`，不进入剧情快照。

设置项保持无逐行外框的布局。`InputBindingCapture` 使用 Button 样式键及 `player-input-binding` 类，将当前键值（如 Control）呈现为可点击的输入框：独立 1px 描边、主题底色与圆角、悬停文字/描边变色和键盘焦点提示；修改仅作用绑定控件，不给整行配置项加卡片。设置模板不再创建重复的可用性 Tooltip，避免空说明也弹出空白提示；不可用原因和错误仍以内联文字展示。不改变 SchemaForm 工厂、可用性或提交队列。

`PlayerInputSettings` 与输入路由属于 GameView，宿主实现原始设备适配。桌面 GameHost 使用 `GalNet.Input.Sdl3` 的固定版本 SDL3 后端，替换 XInput；中立 `StandardGamepadSnapshot` 位于 Presentation.Abstractions，包含本次连接身份、布局/按钮标签、标准按钮与归一化轴，不进入剧情状态。三主题共享面板行为，面板跟随后端可用性与设备标签更新；持久化仍使用 South/East 等物理位置代码，显示实际 A/B/X/Y、Cross/Circle 等标签及位置，不改变确认布局偏好。

`GamepadInputSession` 选择首个有效连接并保持到断连，不混合多设备。摇杆方向的按下/释放阈值分别为 0.5/0.35，扳机为 0.5/0.4，SDL 的 Y 正方向为向下。输入在窗口失活、解绑和断连时通过 Cancel 清空，避免把中断当成松开后提交动作；激活、重新连接或更换设备必须先观察到中立状态才重新接收按键。导航连发仍由 `PlayerInputRouter` 处理并随 Cancel 停止。

原生初始化、更新和释放在 Avalonia UI 线程；同线程多个 scope 共享输入租约，只使用 gamepad 及其隐式 joystick/events 子系统，不创建 SDL 窗口或接管渲染。数据源独占 SDL 输入及 0x600–0x6ff 事件范围，保留其他 SDL 事件；已有外部 input owner 时返回不可用，须由定制宿主显式协调。最后租约只退出自有子系统，不调用全局 SDL_Quit。私有 native 缺失、版本不匹配或初始化失败写入诊断并禁用手柄，键鼠继续可用；移动初始化尚未接入。Windows/WSLg 的虚拟设备自动化不等于真实手柄或 macOS 验收，部署与验证边界见发行手册。

`PlayerInputSettings.OpenHistoryAsync` 接入默认对白历史覆盖层，无回调时历史动作禁用；默认键鼠绑定为 WheelUp，旧偏好仅在缺少该动作且没有手势冲突时补入，显式清空保持无绑定。`IFirstEmptySlotSession.SaveFirstEmptyAsync` 表示普通槽位的首空槽保存，null 表示槽位已满；Sample 在写门内重新查询并保存，满槽打开存档页供玩家选择。该行为独立于单文件 quick-save。

默认宿主以 `DialogueHistoryJournal` 保存当前路线已完整显示的 ADV/NVL 文本，保留实际语言的姓名、富文本、进入前已读状态及可选语音引用；Quick/CatchUp 记录实际经过文本，调试重建不追加。新游戏清空，存档/读档恢复对应前缀；历史正文只在实际保存时进入 `GameSnapshot.Attachments[host.dialogue.history]`，状态表只编码计数游标。当前句按稳定边界重新执行，避免重复记录。默认三主题承载 GamePage.History（PART_History），列表虚拟化，每次打开定位末尾；正文行纯显示，不提供悬停、选择、焦点或点击行为，独立语音按钮可重播。打开取消自动/快速并屏蔽剧情输入，Escape/返回按钮关闭，关闭保持手动。

尚未包含历史模块的当前格式旧存档由宿主补入空历史表，从读档后开始记录；无法补回此前没有保存的正文。已存在游标的存档必须具备长度一致的历史正文；其他模块状态校验保持严格。

语音关联使用同 Group、同 raw entry 的紧邻编译原语身份（rawId#n 与 rawId#(n+1)），仅消费该次实际执行的 audio.play；条件跳过清除候选，不跨源或跨执行借用旧语音。对白原语音结束后显示重播按钮；历史行直接使用保存的资源/音轨/增益。`DialogueVoiceReplay` 以独立临时 voice 借用当前后端和轨道混音，不进入剧情音频队列；新的重播替换旧重播，补全文字、推进、选择、开启自动/快速/跳过、关闭历史、读档或结束运行均停止。迟到的音频事件按 voice ID/generation 隔离。

已读正文配色默认关闭，启用后显示颜色选择器，初始 #A8B8C8，经玩家设置队列保存。每次进入文本前捕获 WasRead，首次显示完成不跳色。`ReadTextStyle` 只替换 typewriter 默认色 runs，显式富文本颜色、粗体、斜体和姓名样式保留；修改设置不重启打字，ADV 与每段 NVL 分别保留进入时状态。

`PlayerInputRouter` 组合设备输入与手柄导航；`KeyboardMouseShortcutListener` 是可独立挂载/释放的非视觉监听模块，通过委托查询接收条件、提交动作和释放按住状态。它随播放器根控件 attach/detach 订阅所在 TopLevel 的键盘事件，鼠标事件仅订阅根控件；不要求游戏区域持有焦点，也不抢占焦点。文本、选择、范围编辑器和壳外宿主控件不触发游戏快捷键，工具栏按钮保留 Enter/Space 原生激活及鼠标交互，其余游戏快捷键仍可用。`GameActionDispatcher` 通过现有游戏 VM/会话命令执行设备无关动作，非运行游戏页、绑定捕获、选择和跳过确认期间不分发游戏动作。按住快进在按下时立即请求、松开时释放；后续组合键会先释放快进。键鼠、手柄和触屏共用执行器；重置、失焦、窗口失活或卸载会释放键鼠按住状态。

`SettingsPageViewModel` 通过 `IPlayerSettingsService.Categories` 获取只读分类及类型化设置项，通过 `Editors` 借用该宿主的字段工厂注册表，分类使用局部 TabControl，不创建导航子页面。数值/滑块、Bool、String、Choice 使用 SchemaForm 的共享默认工厂；Action 与自定义编辑器保留专属行为。Step 仅改变操作步长，不参与校验；动态可见性、可用性和原因 key 驱动界面更新。应用/保存由 SettingsCoordinator 串行协调，保存失败可重试而不重复应用。共享页面不读取配置文件。

内置 `Controls.SettingsCategories.Editors` 接收宿主 EditorRegistry；普通设置通过 `SettingItem<T>` 的 IFieldBinding 调用原有 apply/save 队列，类型化选项使用 ValueCodec 编码，标签与值分开。控件提供单字段表单与设置行的标签、说明、单位、可用性及错误；工厂注册或替换由宿主组合。旧 EditorTemplates/SettingEditor 和数值/选项 UI 转换代理已移除。自定义设置通过 `CustomSettingEditor` 创建独立控件租约并在 detach 时释放。

设置页的 Checkbox 左对齐。音频音量项通过 `SettingItem.InlineToggle` 引用同分类中的静音 BoolSetting；`DisplayItems` 仅负责合并显示，原 `Items`、设置 ID、可用性及 SettingsCoordinator 写入队列保持完整。静音按钮与实际滑块编辑器同行，以喇叭/斜杠喇叭区分状态，保留键盘焦点和无障碍名称。

设置和首次语言选择的 ComboBox 沿用 Fluent 模板，默认、悬停、焦点、按下、禁用和弹出列表状态显式映射到玩家配色；弹出列表使用 Surface 底色、Selection 选中底色和 TextPrimary 文字，不依赖系统主题提供文字对比。

操作设置的触屏手势和手柄确认布局以本地化 key 作为 ComboBox 数据项，由 ItemTemplate 分别创建选中区和弹出列表的文本控件，不共享 PlayerTextBlock 实例。关闭列表、提交设置或切换语言不应清空选中区文字；自定义编辑器卸载后仍正常释放本地化绑定。

Sample 使用单外框 `SaveSlotCard`：预览铺满卡片，底部半透明信息区只显示名称和时间；悬停或内部焦点高亮整卡。有效存档的名称支持直接编辑，Enter/失焦提交、Escape 放弃。宿主通过可选 `ISaveSlotNamingSession` 接入；空槽、坏档和不支持改名的宿主保持只读。文件存档沿用原 `Description` 字段保存名称，`ISaveSlotNamingService` 只修改此字段，时间、预览和剧情快照保持原值。默认 PlayerSession 覆盖槽位时保留名称；改名不改变“继续游戏”按时间选择最近存档的顺序。

GameShell 新生成的存档预览上限为 480×270；旧图按 480 宽度解码展示。默认会话整表刷新只发送一次集合 Reset，普通保存只替换目标槽位，其他行与已解码预览继续复用。`ISaveSlotInfoSource` 是存储的可选单槽元数据查询能力，PlayerSession 对未实现该接口的存储回退整表查询；文件存储实现直接查询并在后台序列化保存数据。

Sample 的窗口装饰颜色从玩家调色板映射到 CaptionButton/TitleBar 资源，不依赖系统明暗主题推断。退出确认在玩家根控件内显示主题弹窗，默认聚焦取消，Escape 取消并恢复先前焦点；遮罩阻断背景点击，焦点循环及导航范围隔离键盘和手柄，底层控件保持启用并由遮罩压暗。确认后沿用原会话停止和资源释放链。

最小化、最大化按钮悬停/按下使用中性灰色叠层；关闭按钮悬停红色、按下深红色，并使用白色图标。

共享页面的静态文案使用 `PlayerTextExtension`，未接入宿主本地化时显示默认英文。官方 Sample 提供 `LocalizedString` 并绑定其 `Value`，沿用 DynamicLocalization 的 Culture/Provider 变更通知机制；宿主设置标签也可以使用同样的动态对象。该绑定不会自动翻译任意业务字符串或剧情内容。

Sample 在会话初始化后将设置接入 Player 变量存储，恢复语言、文本速度、快速已读限制、快速/自动间隔、音量/静音及显示偏好。等待时间使用整数毫秒：快速默认 50、范围 0–5000、步长 10；自动默认 2000、范围 0–30000、步长 100；旧浮点秒值视为类型无效，回退默认值，不迁移。画面能力由宿主提供，桌面 Sample 支持窗口/无边框全屏与预设窗口尺寸，非独占全屏；全屏时禁用窗口尺寸。语言变更立即刷新绑定并排队持久化；清空玩家状态和销毁会话前等待写入完成。

`GalleryNavigationService` 不按资源类型推断页面：没有有内容类型时入口不可用，只有一个时直接打开该页，多个时先进入类型选择页；未注册页面会显示诊断页。CG、视频和音频是独立页面/媒体状态，不使用 renderer enum 或单一多媒体 ViewModel。

## 运行期呈现边界

游戏工具栏的快速/自动模式互斥且允许均关闭，再次点击已选模式可取消。正常状态背景透明，按下与选中只以灰色背景反馈。左键取消旧模式后，模式按钮按点击前状态决定开启/关闭，并从权威播放状态刷新选中绑定；工具栏操作不同时推进剧情。

Runtime 通过 `GalNet.Core.Execution` 中的 IPrimitiveDispatcher、Choice 与快进/播放控制协议接入宿主，不引用呈现程序集。`GalNet.Presentation.Abstractions` 提供文本、NVL、图层、转场、视频、效果等具体领域 presenter 协议及执行注册/恢复组合。`AvaloniaGamePresenters` 是页面侧 presenter 的组合器，分别暴露 dialogue/NVL/choice、layer、animation 和 particle 端口；它不是页面或执行 view。宿主通过 `CompositeGameView` 把这些端口组合成 Runtime 使用的 `IPrimitiveDispatcher`。`NullGameView` 供测试和无界面宿主使用。

各 presenter 共享 `IAvaloniaUiDispatcher`，但分别拥有自己的等待、动画和粒子生命周期。`AvaloniaGamePresenters` 通过宿主提供的 `IGamePageLayerFactory` 解析图层图像，因而共享页面不会自行推断资源路径。玩家推进从 `GamePageViewModel.AdvanceRequested` 直接连接宿主，不经过 dialogue presenter 或 presenter 集合转发。动画逐帧更新由呈现实现完成；Runtime 只维护可存档的场景状态和动画结束后的稳定属性。

默认 `AvaloniaGameSession` 使用中立 `GameSession`/`GameRun`、文件 `GameResourceScope`/`FilePlayerSession` 和 `AvaloniaGameResources` 分离会话协调、内容、玩家持久化与纹理租约。`AvaloniaGamePresentation` 集中创建和释放执行 view 与平台后端，创建失败与正常销毁使用同一条清理路径；单个对象释放失败仍继续释放其他对象。`AvaloniaGameRun` 只绑定页面操作、处理 Engine 状态通知和宿主反馈，不重复推断剧情终态。读档顺序固定为恢复 Runtime snapshot、重放持久展示、页面切换后首次调用 `AdvanceAsync`；reload 和清空玩家状态必须先停止并释放 runner，再释放资源或重建持久化状态。

默认 Avalonia 宿主将 `AvaloniaGamePresentation` 作为 Engine 的 `ICatchUpPresentation` 接入：开始时切入音频投影、缓存文本状态，并暂停/排空恢复出来的展示续跑；结束时清理旧 visual 资源，通过该代 scoped runtime/token 重建最终持久场景，再提交对白/NVL 历史及音频。`ICatchUpTextRecorder` 是可选的无中间渲染文本投影端口，已读文本的追帧调用不会被视为一次正常成功阅读。可见性及 NVL clear 同样延迟到最终同步。追帧不扩展存档格式，不保存 NVL 展示历史或媒体游标。

## Schema 字段表单

`GalNet.Game.Controls.Forms.SchemaForm` 位于 GalNet.Avalonia.Controls。宿主传入有序 Schema（FieldDescriptor 列表）、Registry 和 Binding（字段到 IFieldBinding 的函数），可选 DtoType 启用特定 DTO 成员映射。控件只创建、挂载和释放字段编辑器；不选择游戏主题，不持有保存/撤销服务。结构样式位于 `avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml`，只定义标签、编辑器与诊断的结构，没有颜色、间距或皮肤资源；宿主显式加载或提供自己的 Template/Theme。

EditorRegistry 以开放 EditorId 映射到工厂，提供 Register/Replace/RegisterDefault/Resolve。显式 ID 精确匹配；省略 ID 时按合法选项、注册的值类型及 enum/生成 DTO/列表/JSON 默认规则选择。重复注册明确报错；未知 ID 或工厂以 NotSupportedException 声明不支持时，SchemaForm 保留 editor.fallback 诊断并为该字段提供明确 JSON 回退。其他工厂异常仍按创建失败清理，不伪装为成功。默认集合由宿主调用 CreateDefault 创建；主题可贡献样式或工厂注册方法，由宿主显式组合，不拥有 registry/设置服务的生命周期。

默认 Text/Numeric 使用 TextBox 保存草稿，Enter/失焦时只提交有编辑的文本；JSON 使用带标识的多行 TextBox，Ctrl+Enter/失焦提交，按原始 JSON 表达字符串引号，Escape 恢复未提交草稿；Numeric 不经过 double/decimal 控件值转换，以保留大整数和未完成输入。Toggle/Choice/Slider 提交选定值，复用 Core 的 codec/校验和项目 JSON 配置。Slider 需要有限 min/max、正步长；decimal、不安全整数精度/CLR 范围或非整数整数滑块配置明确诊断。Step 仅决定滑块交互，不限制业务合法值。合法选项与标签分开；binding 的动态选项更新不会重建表单。

另有显式 `numericupdown` 与 `numerictext` 工厂；前者复用 NumericUpDown 主题的输入/上下按钮，保留原始文字并由 FieldEditorContext 提交，不以控件 Value 回写，避免模板初始化、舍入或钳制制造草稿。long/ulong/decimal 使用无损文本，超出可调整范围或非法步长可回退文本并返回工厂 Diagnostic。Editor 将默认 `numeric` 替换为此路径，并将 `slider` 替换为滑块加精确输入，其他宿主的默认 numeric/slider 行为保留。Registry.FormatIssue 允许宿主提供字段诊断文字；原始 ValueIssue 的代码与消息仍保留。浮点编辑文本的 `.3`/`-.3` 由 ValueCodec 规范化，JSON 源码不放宽；未完成数值前缀在输入时可中性展示，提交仍拒绝。

宿主可通过 `EditorRegistry.CreateDefault(emptyInputClearsOptionalFields: true)` 选择非字符串空文本恢复缺省的策略；必填无默认值仍拒绝，JSON 源码不套用此策略。FieldEditorContext.Registry 是借用的只读引用，自定义组合编辑器创建子 Context 时可以传递同一 registry，使输入策略和工厂组合一致。FieldEditorRow 提供可响应宽度的标签、编辑区、诊断和单重置布局；FieldEditorActions.Simplified 选择单重置行为，默认共享表单和 Sample 仍保留原动作集。

FieldValue 以未提供的 Json 和显式 JSON null 区分省略/null。Context 保留草稿和诊断；默认值只用于未提供状态的展示，未编辑的默认/null/空状态失焦不会写回。宿主可通过 CommitAsync 显式提交省略或 null；共享默认结构通过 FieldEditorActions 提供相应按钮，宿主自定义模板可自行组合。支持范围内对象/数组使用懒创建表单，其他类型明确使用 JSON；普通 CLR DtoBinding 没有 presence 时仍拒绝省略，不能静默写成 null。

IFieldBinding 是借用的宿主对象，提供 Read、Changed、CommitAsync、可用性/标签/可选选项，以及可选的中立 HostContext 宿主元数据和 CanReset 操作能力。DtoEditorContext 的作用域适配转发元数据与操作能力，FieldEditorContext 只读公开它们；导航仍需检查当前字段可用性，不通过元数据绕过绑定生命周期。CanReset 允许宿主禁止特定字段的通用重置，例如没有默认定义的 Timeline；不改变文件中的 presence 语义。除 Changed 可从后台发出外，接口调用与工厂/Context 操作均在 UI 线程。binding 负责保存、撤销、异步应用和业务校验，必须捕获对应项目/服务作用域并遵守 CancellationToken；Context 只隔离过期 UI 结果，不回滚宿主已完成的业务写入。

工厂返回独立、无父级的 FieldEditorLease；SchemaForm 在换 schema/registry/binding、卸载或创建失败时先取消所有 Context，再释放全部 lease，最后移除子控件；清理异常保留为 Diagnostics，不短路后续释放。控件不释放借用的 binding。工厂在返回 lease 前失败时，需自行清理尚未交出的临时资源。registry 注册更新后显式调用 RefreshEditors，普通值/选项/可用性通知不重建控件。现有 CustomSettingEditor 复用同一 FieldEditorLease，旧 SettingEditorLease 已移除。

Editor 的通用数值、布尔、文本、选项与 JSON 参数通过 SchemaEntryParameterEditorItemViewModel 使用完整值契约，提交替换 Entry.Parameters，提交通过项目作者意图记录历史并由统一保存链路持久化。Group 面板使用项目 scope 的 registry（直接构造时可显式传入）；EditorFieldEditors 组合选项标签本地化、Transform/颜色/Timeline 成员及类型工厂和场景目标控件。多行文本、资源选择、说话人/变量补全及表达式仍使用专属控件，其行布局与重置同样使用 FieldEditorRow。仅打开参数面板不会写入省略的默认值；生成表单直接保存作者 JSON，缺省与显式 null 可以无损往返。设置页和 Editor 各自提供表单模板，业务对象不进入项目 schema DLL。Editor 的具体操作策略见 [Group 属性编辑](../editor/groups.md#属性行与常用专属编辑器)。

`DtoBinding` 从生成的 IFieldDto 取得字段列表和成员访问，提供同一 IFieldBinding 协议，不再为每种 DTO 定义字段 ViewModel。普通提交调用 ValueCodec/ValueValidation 后写成员；提供 commit 回调时由宿主承担提交与数据同步。INotifyPropertyChanged 自动刷新；无通知 DTO 显式 Refresh。字段订阅随 Context 的 Changed 订阅建立/移除，Dispose 释放自身订阅，不释放借用 DTO。普通 CLR 成员不被当作 presence 容器。

Editor 中有 FieldDataType 且不需要旧专属字段路径的 Entry 通过 DtoEditorHost 承载参数区，未注册整 DTO 覆盖时使用 SchemaForm。EntryParameterFormBinding 直接读取参数字典并通过现有提交方法替换 Parameters，不创建或同步 DTO；外部修改、撤销/重做由 Parameters 变化通知刷新。省略与显式 null 保留在文档，默认值不在打开面板时写回。重新配置释放旧绑定，旧字段不能再提交。通用表单行模板在 Editor 面板内共享，专属资源/表达式等继续保留原路径。字段编辑校验当前候选及自定义跨字段规则，完整文档必填检查留在编译/执行阶段，以允许逐步修正不完整文档。

### DTO 控件构建

同一 EditorRegistry 另外提供类型化的三层注册：RegisterDto<T>/ReplaceDto<T> 替换整个参数区，RegisterMember<T>(成员表达式, factory)/ReplaceMember<T> 替换指定成员，RegisterDefault<T>/ReplaceDefault<T> 指定字段类型默认工厂。默认集合中已有的类型需要 ReplaceDefault，不允许重复 RegisterDefault。显式成员覆盖先于类型覆盖，类型覆盖先于内部 EditorId/default 适配；nullable 按基础类型选择工厂，原字段 codec、presence/null 语义保留。Freeze 后所有注册/替换返回错误；宿主在组合结束后显式冻结，未冻结的旧 registry 仍可按原方式显式刷新。

当前成员表达式只接受直接的生成 DTO 属性（有 Field 特性），用 JsonPropertyName 或与 generator 一致的 camelCase 取得 wire ID，不构造 DTO；嵌套路径、任意 CLR 属性、集合路径模板尚未实现。重复整 DTO/成员注册报错，替换必须明确调用 Replace。内部 EditorId 注册继续承接已有专属控件，不要求新的类型扩展作者使用字符串 ID。

DtoEditorItem 包含稳定 Id、可选 DtoType、冻结字段清单及借用 binding，可选传入原子 patch 提交委托。DtoEditorHost 对单项选择整 DTO 工厂或默认 SchemaForm；FormTheme 可为自动表单提供宿主皮肤。DtoPanel 将有序项列表映射到多个 Host，按项 Id 而非 Type 识别；相同类型多项可以并存，移动未替换的项不重建控件或清除草稿。ItemsSource 支持 UI 线程的集合通知，替换同一 Id 的项对象会重新建立上下文；重复/空项以 Diagnostics 拒绝本次刷新。DtoPanel 是顺序容器，不承诺长列表虚拟化；需要虚拟化或特殊选择/重排外壳的宿主可在自己的列表模板内使用同一 DtoEditorHost。

整 DTO 工厂获得 DtoEditorContext，可 Read 独立字段快照、CreateField、CreateDefaultForm(排除字段)，或 CommitAsync 提交字段 patch。混合表单直接创建默认表单，不再次走整 DTO 分派；其他字段仍遵循成员/默认类型映射。Editor 的 patch 在一次 Parameters 替换中验证并提交，因此多字段修改只产生一条项目历史；控件不以完整 DTO 反序列化/再序列化替换作者数据。没有宿主原子回调时只允许单字段提交，多字段 patch 整体拒绝，不能逐字段提交并假装原子成功。

Context 的生命周期包装取消旧提交、保留动态选项和 IFieldPreviewBinding 的能力；卸载、换项或创建失败先取消上下文并释放自建字段，再卸载内容与释放控件 lease，清理错误进入 Diagnostics。事件仅由实际订阅者退订一次，不释放借用宿主 binding。宿主提交回调必须遵守取消令牌，框架不回滚回调已完成的写入；工厂在交出 lease 前仍须负责自身临时控件资源。

Sample 设置页的普通设置使用 SettingItem.EditorItem/DtoEditorHost，保留设置应用/保存队列；Action/ICustomSetting 保留各自行为。Sample 的 CreateFieldEditors 在组合后冻结 registry。Editor Group 表单直接绑定作者 JSON，文本、资源、表达式和 Timeline 等专属编辑器按字段契约适配，不再用阶段计划说明当前能力；细节见[Group 规范](../editor/groups.md)。

StructuredFieldEditor 复用 Expander 主题（StyleKey 为 Expander，宿主样式使用 Expander.structured-field 类选择器），按展开创建、折叠释放子控件。通过 Core 的 ValueDocument / ValueStructureResolver 统一读取 DTO、显式描述的 JSON 对象、一维数组、List<T>/IList<T>/IReadOnlyList<T>/IEnumerable<T> 以及 Dictionary/IDictionary/IReadOnlyDictionary<string,T>。嵌套对象继续通过 DtoEditorHost 支持整 DTO 覆盖，默认内部表单继续成员/类型注册；公共结构解析器根据 JsonTypeInfo 映射实际 wire 名，不构造 DTO 来生成 UI。对象提交使用公共属性 patch，仅替换编辑成员，保留未知成员。没有结构适配的对象级 converter、未知对象、不支持的键类型、八层深度或超过 200 元素使用 JSON；用户也可显式切换结构/JSON。

通用对象使用单一可折叠 Header，字段行不重复显示相同标签；集合 Header 带数量，简单元素不额外显示 element 标签。字典为键/值行，键支持空串、数字及转义字符；新增、删除与改键是明确的结构操作，重复改键拒绝且保留当前输入。无效键名候选保留于当前控件，不作为跨视图的值草稿归档。ChildIndent、ItemSpacing 由宿主样式设置，统一缩进和组间距，不逐层插空白行。Transform/Flipbook 保留专用布局，子字段定义、读写和校验复用公共属性层；其他专用工厂覆盖顺序不变。

列表结构变化取消当前版本令牌，旧索引绑定和延迟提交失效；普通元素值编辑保留其余行控件。共享控件不实现保存或撤销，依旧提交一个根字段 JSON 值，由宿主负责原子修改及历史。FieldEditorActions 提供显式默认值、缺省、null 和草稿恢复，CanOmit/CanSetNull/HasDefault 决定入口，宿主用 StyledProperty 翻译提示；仅打开表单不写默认值。

PropertyFieldBinding 和通用结构子绑定转发 HostContext、可见性、宿主 Reset 策略及相对根字段的 PropertySegments；DTO scope 同样转发路径。列表序号和字典键通过 IsLabelLiteral 标记为字面标签，避免被当作本地化 key，同时普通字段 Header 保持本地化与字段名回退。导航优先匹配完整路径，避免同名深层字段误选。元数据解析器由控件/分析调用方持有和释放，不以全局静态缓存保留项目契约类型。

## 宿主接入约束

`GameLaunchFlow` 负责并行准备与加载呈现、成功切页后启动，以及取消/失败时等待准备任务收束。`StartNewGameAsync`、`ContinueAsync`、`LoadAsync` 适配普通与两阶段会话；带初始变量的启动要求 `IParameterizedGameSessionService`。加载页停留由项目 `showLoading` 回调提供，不是内置固定策略。即使准备回调忽略取消令牌，取消后也不能切入游戏或开始播放。

- 每次独立运行必须使用独立的游戏会话与页面 Scope；切换项目或重启预览时释放旧 Scope。
- 宿主负责实现 `IGamePageLayerFactory`、`IGameContentProvider`、存档、玩家变量、进度和媒体端口。
- 共享页面可以被替换或覆写；最终客户端的窗口、主题、资源定位和平台服务不进入共享页面项目。
- 新增页面时，需要补充 ViewModel、View、组合注册和导航测试。

## 资源与对象候选面板

共享 Forms 命名空间的 `ResourcePickerPanel` 接收不透明 ID、显示文本、详情、图标、可选缩略图与徽标候选，提供卡片搜索和键鼠选择事件，不解析资产/场景对象，也不保存数据。缩略图由宿主管理。Editor 的 Assets Picker 与场景目标选择控件复用这一组件，各自提供数据源及提交规则；项目状态查询保持在 Editor 服务中。

## Timeline 关键帧画布

共享 `GalNet.Avalonia.Controls` 的 `TimelineCanvas` 与 `TimelineCanvasModel` 提供整数帧标尺、播放头、可见行关键帧/折叠汇总绘制、多选/框选、水平拖动预览及缩放平移。模型通过委托请求开始、预览、提交、取消和加删键，通过事件请求定位；标尺拖动通过 DisplayFrame 显示本地预览，松手只请求一次定位，取消/失效恢复实际帧。控件不引用 Editor、作者 JSON、项目历史或游戏执行状态。宿主提供可见行、会话内键身份及版本失效，并协调相同高度的属性行。Editor 的无损修改与单历史规则见 [Timeline Dock](../editor/groups.md#timeline-dock)。

## 剧情视频图层

`AvaloniaVideoPresenter` 是宿主注入的 `IVideoPresenter` 实现，Sample 在会话构造时将它挂入推荐视频模块。它通过 `IAssetManager` 获取 video 资源并实体化临时媒体文件，LibVLC 像素回调按实际视频尺寸发布 `SceneFrameSource`。视频帧经过现有 `SceneLayerItem` 和场景合成管线，参与 Layer effect 与 ScenePost，而不是独立的原生 VideoView。

帧源租约保护渲染线程读取的纹理；被新帧替换或停止的源在安全时释放。UI 失效通知合并，播放完成/失败的 native callback 只发出信号，停止和释放播放器在回调线程之外完成。隐藏、替换、会话取消会停止对应视频，包括尚未完成的资源加载；异步会话销毁等待所有播放清理后释放 LibVLC。

当前实现使用 CPU 解码帧上传，帧尺寸上限 8192×8192；真实解码验证在 Windows LibVLC 后端完成。其他平台需提供可用的原生 LibVLC，缺失后端会诊断并安全完成剧情视频。Gallery 保持自己的媒体生命周期。

### 常用字段与宿主草稿

`[Common]` 是独立的 Core DTO 字段标记，经生成器进入 `EditorHints.IsCommon`（默认 false）；手写 schema 可以直接设置该提示。它不改变必填、默认值、JSON 或运行时语义。`DtoEditorHost.DisplayMode` 默认 `All`；`Common` 仅筛选本层根字段，保持声明顺序及绑定可见性。复杂对象继续使用既有编辑器，不递归强制筛选；自定义 DTO 工厂通过 `DtoEditorContext.DisplayMode` 决定自身展示，默认表单自动遵循该模式。

`IFieldBinding.Draft` 可选返回宿主拥有的 `FieldDraftState`。控件上下文同步输入和诊断，挂载结束只解除订阅；宿主决定草稿的保存、冲突和生命周期。ScopedBinding 转发该状态，结构化对象/列表和 Transform 子编辑器通过子路径取得独立草稿。输入法组合保护随控件租约登记，项目保存不能越过组合输入。没有宿主草稿的设置/游戏表单沿用控件本地生命周期。

需要来源版本保护的宿主在内容变化时调用 `FieldDraftState.CheckSource()`；一旦观察到冲突，即使源值随后恢复相同内容也保留冲突，直到明确清除草稿。


## 默认宿主扩展边界

默认页面服务以 TryAdd 注册。项目可替换接口实现，或将 TitlePageViewModel、SettingsPageViewModel、GamePageViewModel、GameShellViewModel 的 DI 别名映射到自己的派生类型，并注册该实际类型的 View。标题命令支持重载；标题/Shell VM 的 Dispose 可扩展。默认 GamePage 的四个呈现部件约束继续有效。

默认 Avalonia 会话依赖 IAvaloniaPlayerSettings 与 IPlayerStartup，不要求项目使用默认 GameSettings DTO 或自动字段 UI。设置接口只暴露模块输入及接入/保存/窗口生命周期，SettingsChanged 更新当前运行；IPlayerSettingsService 可供默认页面使用，手写页面可直接绑定项目模型。

AvaloniaGameHostOptions.AttachPlayer 借用已准备的玩家服务和 profile；AttachRun 借用每次 Prepare 后的 Engine。绑定由宿主按运行→玩家→资源的依赖顺序释放，同时实现 IAsyncDisposable 时优先异步排空。业务活动、覆盖层和自定义保存语义归项目服务/模块，不属于默认页面注册协议。

输入配置恢复只接受当前有效配置，不补缺失动作；无效 JSON、保留键或冲突绑定回退默认配置。缺失动作视为未绑定。
