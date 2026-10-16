# Avalonia 游戏页面

## 当前范围

`GalNet.Avalonia.GameView` 是官方共享的 Avalonia 游戏页面宿主。它提供 GameShell、GamePage、截图对话框行为基类及共享 ViewModel/服务；普通页面布局由主题提供，通过组合期注册表完成 ViewModel 到 View 的映射。

页面宿主不读取游戏文件，也不持有项目目录。宿主负责提供游戏内容、资源、存档、玩家变量、进度和媒体实现；编辑器预览与官方 Avalonia Sample 在各自的组合根中装配这些服务。

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

内置三布局统一使用 Themes 的 `GameTypography.UiFontFamily`，经 `GalNet.Theme.FontFamily` 资源应用于玩家根。Windows 优先微软雅黑 UI，macOS 优先苹方简体，其他平台优先 Noto Sans CJK SC 等已安装中文字体。Sample 的 AppBuilder 使用 `CreateFontManagerOptions()` 配置同一中文回退链，并统一所有应用 Window 的字体；显式日志等宽字体保留。未下载或打包字体文件，其他平台实际字体依赖与渲染效果仍需验收；Editor 自身字体不在本次范围。

需要返回行为的页面继承 `BackNavigablePageViewModel`，获得共享 `BackCommand`、受保护的 `Navigation` 和可重写的 `OnBack()`；默认通过导航历史返回。设置、存读档、鉴赏及其详情、关于页已接入；视频详情的 `OnBack()` 先停止播放。标题、加载和剧情页不默认开启返回。页面 XAML 继续绑定 `BackCommand`，无需绑定键盘事件。

`GameShell` 始终组合 `PageBackInputListener`，即使宿主未提供可配置输入设置也支持默认 Esc 返回。模块在所在 TopLevel 的 bubble 阶段处理未消费的纯 Esc，执行当前返回页面的同一个命令；快捷键录入、下拉框和其他已消费 Esc 的内部交互优先取消，不同时退页。按住 Esc 不连续退多页；窗口失活、卸载及 Dispose 清理监听/按键状态。壳外宿主控件不触发该返回行为。手柄页面返回也复用当前页的 `BackCommand`。

## 页面与组合

共享 ViewModel 由 `AddAvaloniaGameViewPages(theme)` 注册，普通 View 由所选主题提供：

| 页面 | 作用 |
| --- | --- |
| `TitlePageViewModel` | 开始、继续和进入其他页面 |
| `LoadingPageViewModel` | 游戏会话加载状态 |
| `GamePage` / `GamePageViewModel` | 游戏场景、对话、选择和交互 |
| `SaveSlotsPageViewModel` | 存档槽和读写操作 |
| `SettingsPageViewModel` | 运行期设置 |
| `GalleryPageViewModel` | 已解锁内容 |
| `AboutPageViewModel` | 宿主提供的关于内容 |

宿主可以在 `AddAvaloniaGameViewPages(theme)` 的注册回调中覆盖主题 View，或者追加自定义页面。注册表构建后不可修改。

### 布局主题与独立配色

`IGameLayoutTheme` 位于 GameView.Composition，提供 `CreateStyles()` 和 `RegisterViews(IPageViewRegistryBuilder)`。`AddAvaloniaGameViewPages(theme: theme)` 主题参数必需，按共享 GamePage 宿主、主题页面、宿主 Gallery 回调、宿主 View 回调顺序注册，随后检查全部必需映射及 GamePage/ScreenshotDialog 基类契约；缺失时列出诊断并拒绝初始化。普通 View 自动补充 Scoped DI 注册，截图对话框每次新建并释放。共享 ViewModel 与业务服务继续由 GameView/宿主注册。

可选 `GalNet.Avalonia.Themes` 集合提供 Classic、Minimal、Paper 布局和 Ocean、Rose、Mono 配色，布局与配色任意组合。配置由开发者在启动代码决定，运行期固定。GameShell 获取 DI 中的主题，并把资源与样式加载到游戏根；Sample 和 Editor 预览均显式选择 Classic；GameView 不再提供默认页面或玩家样式。独立语言选择与截图弹窗接入同一主题资源。官方布局使用玩家根内的 Fluent 模板和所选配色，宿主 Semi、编辑器、调试 UI 与系统文件选择器保持各自作用域。

资源分为 `GalNet.Color.*` 基础色、`GalNet.Palette.*Color` 语义色、`GalNet.Brush.*` 语义画刷和 `GalNet.Theme.*` 布局尺寸。Controls 提供中性画刷回退；Palette 提供完整配色；内置布局不重定义公共语义色，宿主可通过玩家根 Resources 覆盖。GamePalette 检查 21 个必需画刷；官方 Fluent 桥接需对应 Color 资源或可解析的纯色画刷。

普通页保持 UserControl + 共享 ViewModel。三套内置布局分别提供全部普通页面的 XAML View，包含标题、设置、存档、关于、加载、Gallery 入口/列表/详情和诊断页；GameView 不保留普通页面回退；最终映射必须包含全部内置页面与 ScreenshotDialogViewModel。替换单页无需实现完整主题。配置和扩展示例见 [Themes README](../../src/Presentation/GalNet.Avalonia.Themes/README.md)。

页面容器不绘制外框。同一主题内保持一致的返回位置：Classic 顶部页头右侧、Minimal 侧栏底部、Paper 居中标题左侧。图片按比例缩放至独立内容区，不覆盖返回操作。`GalNet.Theme.PageMaxWidth`、`PageHeaderMinHeight`、`BackMinWidth`、`BackMinHeight` 可调整整体宽度与返回点击区域；这些尺寸属于玩家设计画布，实际显示受宿主缩放影响。

对白 `DialogueFontSize` 与 `SpeakerFontSize` 独立配置，内置主题人物名比正文大 4 个设计单位（Classic/Paper 为 26/22，Minimal 为 24/20）；中性回退为 22/18。

### GamePage 模板契约

`GamePage` 是稳定的 TemplatedControl，拥有 `Scene`、`SceneHost`、`Dialogue`、`Nvl`、`Choice` 内容及输入行为。其 ControlTheme 必须包含 `PART_Scene`、`PART_Dialogue`、`PART_Nvl`、`PART_Choice` 四个 ContentPresenter，并用 TemplateBinding 呈现对应内容。不得创建第二套核心 presenter 或重复挂载内容；缺失或错误承载点在模板应用时明确报错，阻塞呈现前调用 EnsureLayout。

GamePageViewModel 的自定义 View 必须继承 GamePage；DI 中的 GamePage 别名与页面工厂解析同一 Scoped 实例。只变更布局推荐覆盖 ControlTheme。模板重建及运行期主题切换不在当前契约内，NVL/打字机内部部件仍保存呈现状态。

页面识别按钮、SelectingItemsControl、TextBox 和 RangeBase 的交互；自定义交互区域设置 `GamePage.IsPlayerInteraction=true`，避免点击操作同时推进剧情。非交互区域继续调用共享 ViewModel 的推进逻辑。

### 截图对话框契约

主题把 ScreenshotDialogViewModel 映射到 ScreenshotDialog 派生 View。XAML 加载后调用受保护的 InitializeDialog，布局必须提供 Preview(Image)、DirectoryBox/TextBox、FileNameBox/TextBox、IncludeUiBox(CheckBox)、BrowseButton/CancelButton/SaveButton(Button)、ErrorBlock(TextBlock)。缺失控件明确报错。基类管理选择目录、预览更新、保存、取消和 Bitmap 释放；服务通过 DI 创建独立实例，并加载当前主题样式。宿主可在 configureViews 中覆盖截图 View，构造参数可注入宿主服务。

### Gallery 页面

Gallery 使用独立的 `IGalleryPageRegistry`，以精确 Gallery `typeId` 映射到页面 ViewModel 和 View。`Add<TViewModel, TView>` 注册新类型，`Replace<TViewModel, TView>` 显式覆盖既有类型；多个 type ID 可以映射到同一页面。该注册同时写入普通 ViewModel→View registry 和 DI，构建后不可修改。

### 设置与界面文本

Sample 新增 `operation`（操作）分类，与游戏、画面、音频并列。它借用现有 `CustomSetting<PlayerInputConfiguration>` 和可释放编辑器租约，承载键鼠、手柄、触屏三个局部 Tab；`SettingItem.IsFullWidth` 允许此类编辑器跨设置行标签列展示。键盘/鼠标共用录入控件，手柄支持按钮组合，触屏通过 ComboBox 选择离散手势。偏好经原 SettingsCoordinator 保存到 `player.galnet.input.bindings`，不进入剧情快照。

设置项保持无逐行外框的布局。`InputBindingCapture` 使用 Button 样式键及 `player-input-binding` 类，将当前键值（如 Control）呈现为可点击的输入框：独立 1px 描边、主题底色与圆角、悬停文字/描边变色和键盘焦点提示；修改仅作用绑定控件，不给整行配置项加卡片。设置模板不再创建重复的可用性 Tooltip，避免空说明也弹出空白提示；不可用原因和错误仍以内联文字展示。不改变 SchemaForm 工厂、可用性或提交队列。

`PlayerInputSettings` 与输入路由属于 GameView，宿主实现原始设备适配。Sample 的 Windows 手柄后端为 XInput，非 Windows 未提供原生手柄后端；三主题共享面板行为和现有设置页外壳。`PlayerInputSettings.OpenHistoryAsync` 是未来对白历史页的可选入口，无回调时历史动作禁用。`IFirstEmptySlotSession.SaveFirstEmptyAsync` 表示普通槽位的首空槽保存，null 表示槽位已满；Sample 在写门内重新查询并保存，满槽打开存档页供玩家选择。该行为独立于单文件 quick-save。

`PlayerInputRouter` 组合设备输入与手柄导航；`KeyboardMouseShortcutListener` 是可独立挂载/释放的非视觉监听模块，通过委托查询接收条件、提交动作和释放按住状态。它随播放器根控件 attach/detach 订阅所在 TopLevel 的键盘事件，鼠标事件仅订阅根控件；不要求游戏区域持有焦点，也不抢占焦点。文本、选择、范围编辑器和壳外宿主控件不触发游戏快捷键，工具栏按钮保留 Enter/Space 原生激活及鼠标交互，其余游戏快捷键仍可用。`GameActionDispatcher` 通过现有游戏 VM/会话命令执行设备无关动作，非运行游戏页、绑定捕获、选择和跳过确认期间不分发游戏动作。键鼠、手柄和触屏共用执行器；重置、失焦、窗口失活或卸载会释放键鼠按住状态并取消延迟触发。

`SettingsPageViewModel` 通过 `IPlayerSettingsService.Categories` 获取只读分类及类型化设置项，通过 `Editors` 借用该宿主的字段工厂注册表，分类使用局部 TabControl，不创建导航子页面。数值/滑块、Bool、String、Choice 使用 SchemaForm 的共享默认工厂；Action 与自定义编辑器保留专属行为。Step 仅改变操作步长，不参与校验；动态可见性、可用性和原因 key 驱动界面更新。应用/保存由 SettingsCoordinator 串行协调，保存失败可重试而不重复应用。共享页面不读取配置文件。

内置主题的 `Common.SettingsCategories.Editors` 接收宿主 EditorRegistry；普通设置通过 `SettingItem<T>` 的 IFieldBinding 调用原有 apply/save 队列，类型化选项使用 ValueCodec 编码，标签与值分开。主题提供单字段表单模板与统一设置行的标签、说明、单位、可用性及错误；工厂注册或替换由宿主组合。旧 EditorTemplates/SettingEditor 和数值/选项 UI 转换代理已移除。自定义设置通过 `CustomSettingEditor` 创建独立控件租约并在 detach 时释放。

共享页面的静态文案使用 `PlayerTextExtension`，未接入宿主本地化时显示默认英文。官方 Sample 提供 `LocalizedString` 并绑定其 `Value`，沿用 DynamicLocalization 的 Culture/Provider 变更通知机制；宿主设置标签也可以使用同样的动态对象。该绑定不会自动翻译任意业务字符串或剧情内容。

Sample 在会话初始化后将设置接入 Player 变量存储，恢复语言、文本速度、快速已读限制、快速/自动间隔、音量/静音及显示偏好。等待时间使用整数毫秒：快速默认 50、范围 0–5000、步长 10；自动默认 2000、范围 0–30000、步长 100；旧浮点秒值视为类型无效，回退默认值，不迁移。画面能力由宿主提供，桌面 Sample 支持窗口/无边框全屏与预设窗口尺寸，非独占全屏；全屏时禁用窗口尺寸。语言变更立即刷新绑定并排队持久化；清空玩家状态和销毁会话前等待写入完成。

`GalleryNavigationService` 不按资源类型推断页面：没有有内容类型时入口不可用，只有一个时直接打开该页，多个时先进入类型选择页；未注册页面会显示诊断页。CG、视频和音频是独立页面/媒体状态，不使用 renderer enum 或单一多媒体 ViewModel。

## 运行期呈现边界

游戏工具栏的快速/自动模式互斥且允许均关闭，再次点击已选模式可取消。两模式未选中时保持透明，仅选中时显示主题色背景；其他工具栏按钮平时透明，悬浮使用淡色背景，按下加深。左键取消旧模式后，模式按钮按点击前状态决定开启/关闭，并从权威播放状态刷新选中绑定；工具栏操作不同时推进剧情。

Runtime 只依赖 `GalNet.Presentation.Abstractions` 中的 `IGameView` 与细分接口（文本、NVL、交互、图层、转场、音频、视频、效果）。`AvaloniaGamePresenters` 是页面侧 presenter 的组合器，分别暴露 dialogue/NVL/choice、layer、animation 和 particle 端口；它不是页面或执行 view。宿主通过 `CompositeGameView` 把这些端口组合成 Runtime 使用的 `IGameView`。`NullGameView` 供测试和无界面宿主使用。

各 presenter 共享 `IAvaloniaUiDispatcher`，但分别拥有自己的等待、动画和粒子生命周期。`AvaloniaGamePresenters` 通过宿主提供的 `IGamePageLayerFactory` 解析图层图像，因而共享页面不会自行推断资源路径。玩家推进从 `GamePageViewModel.AdvanceRequested` 直接连接宿主，不经过 dialogue presenter 或 presenter 集合转发。动画逐帧更新由呈现实现完成；Runtime 只维护可存档的场景状态和动画结束后的稳定属性。

官方 Sample 使用 `SampleGameResourceScope`、`SampleSaveSession`、`SampleGameRunner` 和 展示执行抽象的 `StateRestoreSession` 分离资源、玩家持久化、页面运行编排与读档展示重放。`SampleGamePresentation` 集中创建和释放执行 view 与平台后端，创建失败与正常销毁使用同一条清理路径；单个对象释放失败仍继续释放其他对象。Runner 只绑定页面操作、处理 Engine 状态通知和宿主反馈，不重复推断剧情终态。读档顺序固定为恢复 Runtime snapshot、重放持久展示、页面切换后首次调用 `AdvanceAsync`；reload 和清空玩家状态必须先停止并释放 runner，再释放资源或重建持久化状态。

Sample 将 `SampleGamePresentation` 作为 Engine 的 `ICatchUpPresentation` 接入：开始时切入音频投影、缓存文本状态，并暂停/排空恢复出来的展示续跑；结束时清理旧 visual 资源，通过该代 scoped runtime/token 重建最终持久场景，再提交对白/NVL 历史及音频。`ICatchUpTextRecorder` 是可选的无中间渲染文本投影端口，已读文本的追帧调用不会被视为一次正常成功阅读。可见性及 NVL clear 同样延迟到最终同步。追帧不扩展存档格式，不保存 NVL 展示历史或媒体游标。

## Schema 字段表单

`GalNet.Game.Controls.Forms.SchemaForm` 位于 GalNet.Avalonia.Controls。宿主传入有序 Schema（FieldDescriptor 列表）、Registry 和 Binding（字段到 IFieldBinding 的函数）。控件只创建、挂载和释放字段编辑器；不选择游戏主题，不持有保存/撤销服务。结构样式位于 `avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml`，只定义标签、编辑器与诊断的结构，没有颜色、间距或皮肤资源；宿主显式加载或提供自己的 Template/Theme。

EditorRegistry 以开放 EditorId 映射到工厂，提供 Register/Replace/RegisterDefault/Resolve。显式 ID 精确匹配；省略 ID 时按合法选项、注册的值类型及 enum/JSON 默认规则选择。重复注册、未知 ID 和不支持类型均明确报错，不静默替换自定义 ID。默认集合由宿主调用 CreateDefault 创建；主题可贡献样式或工厂注册方法，由宿主显式组合，不拥有 registry/设置服务的生命周期。

默认 Text/Numeric/JSON 使用 TextBox 保存草稿，Enter/失焦时只提交有编辑的文本；Numeric 不经过 double/decimal 控件值转换，以保留大整数和未完成输入。Toggle/Choice/Slider 提交选定值，复用 Core 的 codec/校验和项目 JSON 配置。Slider 需要有限 min/max、正步长；decimal、不安全整数精度/CLR 范围或非整数整数滑块配置明确诊断。Step 仅决定滑块交互，不限制业务合法值。合法选项与标签分开；binding 的动态选项更新不会重建表单。

FieldValue 以未提供的 Json 和显式 JSON null 区分省略/null。Context 保留草稿和诊断；默认值只用于未提供状态的展示，未编辑的默认/null/空状态失焦不会写回。宿主可通过 CommitAsync 显式提交省略或 null，默认结构未提供统一的删除/null 操作按钮。对象/数组以 JSON 文本编辑，不承诺任意嵌套控件。

IFieldBinding 是借用的宿主对象，提供 Read、Changed、CommitAsync、可用性/标签/可选选项。除 Changed 可从后台发出外，接口调用与工厂/Context 操作均在 UI 线程。binding 负责保存、撤销、异步应用和业务校验，必须捕获对应项目/服务作用域并遵守 CancellationToken；Context 只隔离过期 UI 结果，不回滚宿主已完成的业务写入。

工厂返回独立、无父级的 FieldEditorLease；SchemaForm 在换 schema/registry/binding、卸载或创建失败时先取消所有 Context，再释放全部 lease，最后移除子控件；清理异常保留为 Diagnostics，不短路后续释放。控件不释放借用的 binding。工厂在返回 lease 前失败时，需自行清理尚未交出的临时资源。registry 注册更新后显式调用 RefreshEditors，普通值/选项/可用性通知不重建控件。现有 CustomSettingEditor 复用同一 FieldEditorLease，旧 SettingEditorLease 已移除。

Editor 的通用数值、布尔、文本、选项与 JSON 参数通过 SchemaEntryParameterEditorItemViewModel 使用完整值契约，提交替换 Entry.Parameters，继续由 workspace 历史与保存链路观察。每个 Group 面板拥有独立 registry，EditorFieldEditors 仅扩展选项标签本地化，转换与提交复用默认工厂。多行文本、资源选择、说话人/变量补全及表达式仍使用现有专属控件。仅打开参数面板不会写入省略的默认值；字符串文档不能无损表示的值（如显式 null 字符串）明确拒绝，原始工具路径可处理。设置页和 Editor 各自提供表单模板，业务对象不进入项目 schema DLL。

`DtoBinding` 从生成的 IFieldDto 取得字段列表和成员访问，提供同一 IFieldBinding 协议，不再为每种 DTO 定义字段 ViewModel。普通提交调用 ValueCodec/ValueValidation 后写成员；提供 commit 回调时由宿主承担提交与数据同步。INotifyPropertyChanged 自动刷新；无通知 DTO 显式 Refresh。字段订阅随 Context 的 Changed 订阅建立/移除，Dispose 释放自身订阅，不释放借用 DTO。普通 CLR 成员不被当作 presence 容器。

Editor 中有 FieldDataType 的 Entry 用一个 SchemaForm 承载全部字段。EntryParameterFormBinding 直接读取参数字典并通过现有提交方法替换 Parameters，不创建或同步 DTO；外部修改、撤销/重做由 Parameters 变化通知刷新。省略与显式 null 保留在文档，默认值不在打开面板时写回。重新配置释放旧绑定，旧字段不能再提交。通用表单行模板在 Editor 面板内共享，专属资源/表达式等继续保留原路径。字段编辑校验当前候选及自定义跨字段规则，完整文档必填检查留在编译/执行阶段，以允许逐步修正不完整文档。

## 宿主接入约束

- 每次独立运行必须使用独立的游戏会话与页面 Scope；切换项目或重启预览时释放旧 Scope。
- 宿主负责实现 `IGamePageLayerFactory`、`IGameContentProvider`、存档、玩家变量、进度和媒体端口。
- 共享页面可以被替换或覆写；最终客户端的窗口、主题、资源定位和平台服务不进入共享页面项目。
- 新增页面时，需要补充 ViewModel、View、组合注册和导航测试。

## 剧情视频图层

`AvaloniaVideoPresenter` 是宿主注入的 `IVideoPresenter` 实现，Sample 在会话构造时将它挂入推荐视频模块。它通过 `IAssetManager` 获取 video 资源并实体化临时媒体文件，LibVLC 像素回调按实际视频尺寸发布 `SceneFrameSource`。视频帧经过现有 `SceneLayerItem` 和场景合成管线，参与 Layer effect 与 ScenePost，而不是独立的原生 VideoView。

帧源租约保护渲染线程读取的纹理；被新帧替换或停止的源在安全时释放。UI 失效通知合并，播放完成/失败的 native callback 只发出信号，停止和释放播放器在回调线程之外完成。隐藏、替换、会话取消会停止对应视频，包括尚未完成的资源加载；异步会话销毁等待所有播放清理后释放 LibVLC。

当前实现使用 CPU 解码帧上传，帧尺寸上限 8192×8192；真实解码验证在 Windows LibVLC 后端完成。其他平台需提供可用的原生 LibVLC，缺失后端会诊断并安全完成剧情视频。Gallery 保持自己的媒体生命周期。
