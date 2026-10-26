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

Runtime 只依赖 `GalNet.Presentation.Abstractions` 中的 `IPrimitiveDispatcher` 与细分接口（文本、NVL、交互、图层、转场、音频、视频、效果）。`AvaloniaGamePresenters` 是页面侧 presenter 的组合器，分别暴露 dialogue/NVL/choice、layer、animation 和 particle 端口；它不是页面或执行 view。宿主通过 `CompositeGameView` 把这些端口组合成 Runtime 使用的 `IPrimitiveDispatcher`。`NullGameView` 供测试和无界面宿主使用。

各 presenter 共享 `IAvaloniaUiDispatcher`，但分别拥有自己的等待、动画和粒子生命周期。`AvaloniaGamePresenters` 通过宿主提供的 `IGamePageLayerFactory` 解析图层图像，因而共享页面不会自行推断资源路径。玩家推进从 `GamePageViewModel.AdvanceRequested` 直接连接宿主，不经过 dialogue presenter 或 presenter 集合转发。动画逐帧更新由呈现实现完成；Runtime 只维护可存档的场景状态和动画结束后的稳定属性。

默认 `AvaloniaGameSession` 使用中立 `GameSession`/`GameRun`、文件 `GameResourceScope`/`FilePlayerSession` 和 `AvaloniaGameResources` 分离会话协调、内容、玩家持久化与纹理租约。`AvaloniaGamePresentation` 集中创建和释放执行 view 与平台后端，创建失败与正常销毁使用同一条清理路径；单个对象释放失败仍继续释放其他对象。`AvaloniaGameRun` 只绑定页面操作、处理 Engine 状态通知和宿主反馈，不重复推断剧情终态。读档顺序固定为恢复 Runtime snapshot、重放持久展示、页面切换后首次调用 `AdvanceAsync`；reload 和清空玩家状态必须先停止并释放 runner，再释放资源或重建持久化状态。

默认 Avalonia 宿主将 `AvaloniaGamePresentation` 作为 Engine 的 `ICatchUpPresentation` 接入：开始时切入音频投影、缓存文本状态，并暂停/排空恢复出来的展示续跑；结束时清理旧 visual 资源，通过该代 scoped runtime/token 重建最终持久场景，再提交对白/NVL 历史及音频。`ICatchUpTextRecorder` 是可选的无中间渲染文本投影端口，已读文本的追帧调用不会被视为一次正常成功阅读。可见性及 NVL clear 同样延迟到最终同步。追帧不扩展存档格式，不保存 NVL 展示历史或媒体游标。

## Schema 字段表单

`GalNet.Game.Controls.Forms.SchemaForm` 位于 GalNet.Avalonia.Controls。宿主传入有序 Schema（FieldDescriptor 列表）、Registry 和 Binding（字段到 IFieldBinding 的函数），可选 DtoType 启用特定 DTO 成员映射。控件只创建、挂载和释放字段编辑器；不选择游戏主题，不持有保存/撤销服务。结构样式位于 `avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml`，只定义标签、编辑器与诊断的结构，没有颜色、间距或皮肤资源；宿主显式加载或提供自己的 Template/Theme。

EditorRegistry 以开放 EditorId 映射到工厂，提供 Register/Replace/RegisterDefault/Resolve。显式 ID 精确匹配；省略 ID 时按合法选项、注册的值类型及 enum/生成 DTO/列表/JSON 默认规则选择。重复注册明确报错；未知 ID 或工厂以 NotSupportedException 声明不支持时，SchemaForm 保留 editor.fallback 诊断并为该字段提供明确 JSON 回退。其他工厂异常仍按创建失败清理，不伪装为成功。默认集合由宿主调用 CreateDefault 创建；主题可贡献样式或工厂注册方法，由宿主显式组合，不拥有 registry/设置服务的生命周期。

默认 Text/Numeric 使用 TextBox 保存草稿，Enter/失焦时只提交有编辑的文本；JSON 使用带标识的多行 TextBox，Ctrl+Enter/失焦提交，按原始 JSON 表达字符串引号，Escape 恢复未提交草稿；Numeric 不经过 double/decimal 控件值转换，以保留大整数和未完成输入。Toggle/Choice/Slider 提交选定值，复用 Core 的 codec/校验和项目 JSON 配置。Slider 需要有限 min/max、正步长；decimal、不安全整数精度/CLR 范围或非整数整数滑块配置明确诊断。Step 仅决定滑块交互，不限制业务合法值。合法选项与标签分开；binding 的动态选项更新不会重建表单。

宿主可通过 `EditorRegistry.CreateDefault(emptyInputClearsOptionalFields: true)` 选择非字符串空文本恢复缺省的策略；必填无默认值仍拒绝，JSON 源码不套用此策略。FieldEditorContext.Registry 是借用的只读引用，自定义组合编辑器创建子 Context 时可以传递同一 registry，使输入策略和工厂组合一致。FieldEditorRow 提供可响应宽度的标签、编辑区、诊断和单重置布局；FieldEditorActions.Simplified 选择单重置行为，默认共享表单和 Sample 仍保留原动作集。

FieldValue 以未提供的 Json 和显式 JSON null 区分省略/null。Context 保留草稿和诊断；默认值只用于未提供状态的展示，未编辑的默认/null/空状态失焦不会写回。宿主可通过 CommitAsync 显式提交省略或 null；共享默认结构通过 FieldEditorActions 提供相应按钮，宿主自定义模板可自行组合。支持范围内对象/数组使用懒创建表单，其他类型明确使用 JSON；普通 CLR DtoBinding 没有 presence 时仍拒绝省略，不能静默写成 null。

IFieldBinding 是借用的宿主对象，提供 Read、Changed、CommitAsync、可用性/标签/可选选项，以及可选的中立 HostContext 宿主元数据和 CanReset 操作能力。DtoEditorContext 的作用域适配转发元数据与操作能力，FieldEditorContext 只读公开它们；导航仍需检查当前字段可用性，不通过元数据绕过绑定生命周期。CanReset 允许宿主禁止特定字段的通用重置，例如没有默认定义的 Timeline；不改变文件中的 presence 语义。除 Changed 可从后台发出外，接口调用与工厂/Context 操作均在 UI 线程。binding 负责保存、撤销、异步应用和业务校验，必须捕获对应项目/服务作用域并遵守 CancellationToken；Context 只隔离过期 UI 结果，不回滚宿主已完成的业务写入。

工厂返回独立、无父级的 FieldEditorLease；SchemaForm 在换 schema/registry/binding、卸载或创建失败时先取消所有 Context，再释放全部 lease，最后移除子控件；清理异常保留为 Diagnostics，不短路后续释放。控件不释放借用的 binding。工厂在返回 lease 前失败时，需自行清理尚未交出的临时资源。registry 注册更新后显式调用 RefreshEditors，普通值/选项/可用性通知不重建控件。现有 CustomSettingEditor 复用同一 FieldEditorLease，旧 SettingEditorLease 已移除。

Editor 的通用数值、布尔、文本、选项与 JSON 参数通过 SchemaEntryParameterEditorItemViewModel 使用完整值契约，提交替换 Entry.Parameters，提交通过项目作者意图记录历史并由统一保存链路持久化。Group 面板使用项目 scope 的 registry（直接构造时可显式传入）；EditorFieldEditors 组合选项标签本地化、Transform/颜色/Timeline 成员及类型工厂和场景目标控件。多行文本、资源选择、说话人/变量补全及表达式仍使用专属控件，其行布局与重置同样使用 FieldEditorRow。仅打开参数面板不会写入省略的默认值；生成表单直接保存作者 JSON，缺省与显式 null 可以无损往返。设置页和 Editor 各自提供表单模板，业务对象不进入项目 schema DLL。Editor 的具体操作策略见 [Group 属性编辑](editor-groups.md#属性行与常用专属编辑器)。

`DtoBinding` 从生成的 IFieldDto 取得字段列表和成员访问，提供同一 IFieldBinding 协议，不再为每种 DTO 定义字段 ViewModel。普通提交调用 ValueCodec/ValueValidation 后写成员；提供 commit 回调时由宿主承担提交与数据同步。INotifyPropertyChanged 自动刷新；无通知 DTO 显式 Refresh。字段订阅随 Context 的 Changed 订阅建立/移除，Dispose 释放自身订阅，不释放借用 DTO。普通 CLR 成员不被当作 presence 容器。

Editor 中有 FieldDataType 且不需要旧专属字段路径的 Entry 通过 DtoEditorHost 承载参数区，未注册整 DTO 覆盖时使用 SchemaForm。EntryParameterFormBinding 直接读取参数字典并通过现有提交方法替换 Parameters，不创建或同步 DTO；外部修改、撤销/重做由 Parameters 变化通知刷新。省略与显式 null 保留在文档，默认值不在打开面板时写回。重新配置释放旧绑定，旧字段不能再提交。通用表单行模板在 Editor 面板内共享，专属资源/表达式等继续保留原路径。字段编辑校验当前候选及自定义跨字段规则，完整文档必填检查留在编译/执行阶段，以允许逐步修正不完整文档。

### DTO 控件构建

同一 EditorRegistry 另外提供类型化的三层注册：RegisterDto<T>/ReplaceDto<T> 替换整个参数区，RegisterMember<T>(成员表达式, factory)/ReplaceMember<T> 替换指定成员，RegisterDefault<T>/ReplaceDefault<T> 指定字段类型默认工厂。默认集合中已有的类型需要 ReplaceDefault，不允许重复 RegisterDefault。显式成员覆盖先于类型覆盖，类型覆盖先于内部 EditorId/default 适配；nullable 按基础类型选择工厂，原字段 codec、presence/null 语义保留。Freeze 后所有注册/替换返回错误；宿主在组合结束后显式冻结，未冻结的旧 registry 仍可按原方式显式刷新。

当前成员表达式只接受直接的生成 DTO 属性（有 Field 特性），用 JsonPropertyName 或与 generator 一致的 camelCase 取得 wire ID，不构造 DTO；嵌套路径、任意 CLR 属性、集合路径模板尚未实现。重复整 DTO/成员注册报错，替换必须明确调用 Replace。内部 EditorId 注册继续承接已有专属控件，不要求新的类型扩展作者使用字符串 ID。

DtoEditorItem 包含稳定 Id、可选 DtoType、冻结字段清单及借用 binding，可选传入原子 patch 提交委托。DtoEditorHost 对单项选择整 DTO 工厂或默认 SchemaForm；FormTheme 可为自动表单提供宿主皮肤。DtoPanel 将有序项列表映射到多个 Host，按项 Id 而非 Type 识别；相同类型多项可以并存，移动未替换的项不重建控件或清除草稿。ItemsSource 支持 UI 线程的集合通知，替换同一 Id 的项对象会重新建立上下文；重复/空项以 Diagnostics 拒绝本次刷新。DtoPanel 是顺序容器，不承诺长列表虚拟化；需要虚拟化或特殊选择/重排外壳的宿主可在自己的列表模板内使用同一 DtoEditorHost。

整 DTO 工厂获得 DtoEditorContext，可 Read 独立字段快照、CreateField、CreateDefaultForm(排除字段)，或 CommitAsync 提交字段 patch。混合表单直接创建默认表单，不再次走整 DTO 分派；其他字段仍遵循成员/默认类型映射。Editor 的 patch 在一次 Parameters 替换中验证并提交，因此多字段修改只产生一条项目历史；控件不以完整 DTO 反序列化/再序列化替换作者数据。没有宿主原子回调时只允许单字段提交，多字段 patch 整体拒绝，不能逐字段提交并假装原子成功。

Context 的生命周期包装取消旧提交、保留动态选项和 IFieldPreviewBinding 的能力；卸载、换项或创建失败先取消上下文并释放自建字段，再卸载内容与释放控件 lease，清理错误进入 Diagnostics。事件仅由实际订阅者退订一次，不释放借用宿主 binding。宿主提交回调必须遵守取消令牌，框架不回滚回调已完成的写入；工厂在交出 lease 前仍须负责自身临时控件资源。

内置设置主题的普通设置改用 SettingItem.EditorItem/DtoEditorHost，继续提供原单字段 SchemaForm 的皮肤与设置应用/保存队列；Action/ICustomSetting 保留各自行为。Sample 的 CreateFieldEditors 组合结束后冻结 registry。Editor 的 Dock 创建 Group 面板时传入项目 registry；目前依赖旧资源/表达式等专属字段的 Entry 继续走原路径。P6b 补充普通嵌套结构表单；所有专属字段迁移、手势合并协议、状态查询与 timeline/曲线仍未包含。

StructuredFieldEditor 复用 Expander 主题（StyleKey 为 Expander，宿主样式使用 Expander.structured-field 类选择器），按展开创建、折叠释放子控件。支持有生成静态 Schema 的 DTO，以及一维数组、List<T>/IList<T>/IReadOnlyList<T>/IEnumerable<T>。嵌套对象继续通过 DtoEditorHost 支持整 DTO 覆盖，默认内部表单继续成员/类型注册；根据 JsonTypeInfo 映射实际 wire 名，不构造 DTO 来生成 UI。对象提交合并当前 JSON，仅替换编辑成员，保留未知成员。自定义 converter、无 schema 的对象、字典、八层深度或超过 200 元素使用 JSON；用户也可显式切换结构/JSON。

列表结构变化取消当前版本令牌，旧索引绑定和延迟提交失效；普通元素值编辑保留其余行控件。共享控件不实现保存或撤销，依旧提交一个根字段 JSON 值，由宿主负责原子修改及历史。FieldEditorActions 提供显式默认值、缺省、null 和草稿恢复，CanOmit/CanSetNull/HasDefault 决定入口，宿主用 StyledProperty 翻译提示；仅打开表单不写默认值。

## 宿主接入约束

- 每次独立运行必须使用独立的游戏会话与页面 Scope；切换项目或重启预览时释放旧 Scope。
- 宿主负责实现 `IGamePageLayerFactory`、`IGameContentProvider`、存档、玩家变量、进度和媒体端口。
- 共享页面可以被替换或覆写；最终客户端的窗口、主题、资源定位和平台服务不进入共享页面项目。
- 新增页面时，需要补充 ViewModel、View、组合注册和导航测试。

## 资源与对象候选面板

共享 Forms 命名空间的 `ResourcePickerPanel` 接收不透明 ID、显示文本、详情、图标、可选缩略图与徽标候选，提供卡片搜索和键鼠选择事件，不解析资产/场景对象，也不保存数据。缩略图由宿主管理。Editor 的 Assets Picker 与场景目标选择控件复用这一组件，各自提供数据源及提交规则；项目状态查询保持在 Editor 服务中。

## Timeline 关键帧画布

共享 `GalNet.Avalonia.Controls` 的 `TimelineCanvas` 与 `TimelineCanvasModel` 提供整数帧标尺、播放头、可见行关键帧/折叠汇总绘制、多选/框选、水平拖动预览及缩放平移。模型通过委托请求开始、预览、提交、取消和加删键，通过事件请求定位；标尺拖动通过 DisplayFrame 显示本地预览，松手只请求一次定位，取消/失效恢复实际帧。控件不引用 Editor、作者 JSON、项目历史或游戏执行状态。宿主提供可见行、会话内键身份及版本失效，并协调相同高度的属性行。Editor 的无损修改与单历史规则见 [Timeline Dock](editor-groups.md#timeline-dock)。

## 剧情视频图层

`AvaloniaVideoPresenter` 是宿主注入的 `IVideoPresenter` 实现，Sample 在会话构造时将它挂入推荐视频模块。它通过 `IAssetManager` 获取 video 资源并实体化临时媒体文件，LibVLC 像素回调按实际视频尺寸发布 `SceneFrameSource`。视频帧经过现有 `SceneLayerItem` 和场景合成管线，参与 Layer effect 与 ScenePost，而不是独立的原生 VideoView。

帧源租约保护渲染线程读取的纹理；被新帧替换或停止的源在安全时释放。UI 失效通知合并，播放完成/失败的 native callback 只发出信号，停止和释放播放器在回调线程之外完成。隐藏、替换、会话取消会停止对应视频，包括尚未完成的资源加载；异步会话销毁等待所有播放清理后释放 LibVLC。

当前实现使用 CPU 解码帧上传，帧尺寸上限 8192×8192；真实解码验证在 Windows LibVLC 后端完成。其他平台需提供可用的原生 LibVLC，缺失后端会诊断并安全完成剧情视频。Gallery 保持自己的媒体生命周期。

### 常用字段与宿主草稿

`[Common]` 是独立的 Core DTO 字段标记，经生成器进入 `EditorHints.IsCommon`（默认 false）；手写 schema 可以直接设置该提示。它不改变必填、默认值、JSON 或运行时语义。`DtoEditorHost.DisplayMode` 默认 `All`；`Common` 仅筛选本层根字段，保持声明顺序及绑定可见性。复杂对象继续使用既有编辑器，不递归强制筛选；自定义 DTO 工厂通过 `DtoEditorContext.DisplayMode` 决定自身展示，默认表单自动遵循该模式。

`IFieldBinding.Draft` 可选返回宿主拥有的 `FieldDraftState`。控件上下文同步输入和诊断，挂载结束只解除订阅；宿主决定草稿的保存、冲突和生命周期。ScopedBinding 转发该状态，结构化对象/列表和 Transform 子编辑器通过子路径取得独立草稿。输入法组合保护随控件租约登记，项目保存不能越过组合输入。没有宿主草稿的设置/游戏表单沿用控件本地生命周期。

需要来源版本保护的宿主在内容变化时调用 `FieldDraftState.CheckSource()`；一旦观察到冲突，即使源值随后恢复相同内容也保留冲突，直到明确清除草稿。
