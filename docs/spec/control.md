# Control 层设计

## 目标与边界

`GalNet.Control` 是 GalNet 的 Avalonia 默认 UI 实现。它负责将 UI 模板构造成真实的 View 和 ViewModel，并提供运行时场景、媒体、转场及控件适配；它不读取项目目录、不保存 UI 文件，也不拥有编辑器状态。

插件只依赖 `GalNet.Control.Abstraction`。宿主（Editor、启动器或未来独立播放器）负责把模板、当前项目的实例 Provider、调色板和会话服务组合到 DI 容器。

```
Core                 实例数据模型：TemplateId、配置、颜色覆盖
Control.Abstraction  插件契约：模板、实例 Provider、导航、调色板、AXAML 扩展
Control              内置模板、Avalonia View/VM、运行时表现层
Editor.Shared        UI 项目文件、共享调色板、加载/保存
Editor               编辑 UI 与预览宿主
```

`GameContent` 只携带剧情图和资源根；UI 项目不是游戏内容的一部分。

## 三层模型

Widget 与 Screen 都分为三层。

| 层 | 职责 | 位置 |
| --- | --- | --- |
| 类别 | 定义最小行为和类别键，例如按钮、对话、标题、设置、存读档 | `Control.Abstraction`（插件契约） |
| 模板 | 定义配置语义、默认值、颜色/实例引用校验、交互，并构造真实 View/VM | `Control/UI` 或插件程序集 |
| 实例 | 仅保存 `Id`、`TemplateId`、配置 JSON、颜色覆盖；不保存 Avalonia 对象或服务 | `Core/UI`，由宿主持久化 |

实例像 Material，模板像 Shader：同一 Widget 实例可在多个 Screen 中构造出独立的可视对象。Screen 实例由类别路由键选择，默认键为 `title`、`game`、`settings`、`save-load`、`gallery`、`about`。

## 插件契约与 DI

`GalNet.Control.Abstraction.UI` 提供：

- `IWidgetTemplate`、`IScreenTemplate`：模板 ID、类别、校验和 `Build`。
- `IWidgetInstanceProvider`、`IScreenInstanceProvider`：按 ID 或 Screen 类别键提供实例。
- `IColorPalette`：`INotifyPropertyChanged` + `IBrush this[string key]`。
- `WidgetBuildContext`、`ScreenBuildContext`：当前服务作用域、实例 Provider、调色板、导航器和会话参数。
- `WidgetPresentation`、`ScreenPresentation`：真实 `Control View` 与 ViewModel 的配对结果。
- `IGameScreenNavigator`：可绑定的 `Current` Screen Presentation。

模板由宿主显式注册为 DI 服务；`TemplateRegistry` 由 `IEnumerable<IWidgetTemplate>` 与 `IEnumerable<IScreenTemplate>` 建索引，重复模板 ID 必须失败。模板可以在构造函数中注入稳定服务；每次构造 Screen/Widget 时，都从当前游戏或预览的服务作用域取得会话服务。

内置 Title、Game、Settings、SaveLoad、Gallery、About 都是独立的 Screen 模板工厂。模板使用 `ActivatorUtilities` 创建 View，设置 `DataContext`，设置继承调色板，并返回 Presentation。

## 构建与导航

`GamePageHostViewModel` 创建 `IGameScreenNavigator` 并初始导航至 `title`。导航器的工作流为：

1. 以类别键从 `IScreenInstanceProvider` 解析默认 Screen 实例。
2. 用实例的 `TemplateId` 从 `IScreenTemplateRegistry` 找到模板。
3. 以当前 `ScreenBuildContext` 调用模板，取得新 `ScreenPresentation`。
4. 更新 `Current`，并维护回退栈。

宿主 View 只需绑定实际 View：

```xml
<ContentControl Content="{Binding Navigator.Current.View}" />
```

不使用 `InternalNav`、VM→View 注册表、当前页面事件或 `Activator.CreateInstance`。Screen VM 直接注入 `IGameScreenNavigator`，按路由键导航或回退。

## Screen 页面路由

`GameFlowFactory.BuildScreen()` 按路由键分发创建：

| 路由键 | ViewModel | 说明 |
| --- | --- | --- |
| `title` | `GameStartViewModel` / `TextMenuTitleViewModel` | 标题页（根据预设选择模板） |
| `game` | `GameRunViewModel` | 游戏运行页（核心游玩界面） |
| `settings` | `SettingsViewModel` | 设置页 |
| `save-load` | `SaveLoadViewModel` | 存档/读档页（通过参数区分模式） |
| `gallery` | `GalleryViewModel` | 鉴赏页 |
| `about` | `AboutViewModel` | 关于页（Markdown 渲染） |

## Overlay 覆盖层

`ScreenshotDialog` 是覆盖层对话框，不参与 Screen 路由。通过 `OverlayDialog.ShowCustomAsync<>()` 弹出，用于截图保存等场景。

## 调色板与绑定

`PaletteScope.Palette` 是继承型 Avalonia 附加属性。模板将当前 `IColorPalette` 设到 Screen 根 View，子树中的控件可通过 `PaletteBinding.Create(control, key)` 绑定 Brush；AXAML 可写为：

```xml
Background="{ui:PaletteBrush Background0}"
Foreground="{ui:PaletteBrush FontColor0}"
```

调色板实现只要在任意颜色变化时触发 `PropertyChanged("Item[]")`。绑定代理会重新读取索引器，已显示的 View/Widget 无需重建或重新导航。

`ProjectColorPalette` 位于 `Editor.Shared`，由 `FileUiProjectProvider` 的项目内存状态驱动。颜色面板修改 `ColorItem` 时必须立刻写入该共享 palette、通知绑定并保存 UI 文件。Control 不实现磁盘 Provider。

### 内置语义色键

内置模板和插件应优先使用 `UiColorKeys`，而不是复制 `#AARRGGBB` 字面量：

| 角色 | 键 |
| --- | --- |
| 品牌/强调 | `PrimaryColor`、`PrimaryColorHover`、`HighlightColor` |
| 背景层级 | `Background0`、`Background1`、`Background2`、`HighlightBackground` |
| 文字层级 | `FontColor0`、`FontColor1`、`FontColor2`、`FontHighlightColor` |
| 状态与轮廓 | `BorderColor`、`DisabledColor`、`DangerColor` |

Screen 根会把 `FontColor0` 作为继承前景色，普通文字不应逐个硬编码；需要弱化、强调或危险状态时再显式绑定对应键。Slider、CheckBox、返回按钮、存档卡片、运行期 Widget 和游戏命令栏均应使用这些键。

游戏命令栏是例外：无论常态、悬浮或按下，背景始终透明；只改变前景色（常态 `FontColor1`、悬浮 `PrimaryColor`、按下 `HighlightColor`）。

## UI 项目存储

项目文件由 Editor.Shared 负责：

```
UI/
  ui.json
  WidgetInstance/*.json
  ScreenInstance/*.json
```

`ui.json` 包含 `Colors` 和"类别键 → 默认 Screen 实例 ID"的 `DefaultViews`。实例文件仅保存数据；模板代码由宿主 DI 提供。本层不承诺插件程序集自动扫描，也不提供 UI 实例可视化编辑器。

## 约束

- 模板不得缓存颜色字符串或手工订阅颜色事件；使用 palette Binding。
- Control 不得直接加载 `UI/`、创建 `UiProject` 或保存项目文件。
- 缺失实例、模板或类别不匹配必须给出验证错误，不使用硬编码页面回退。
- 新模板必须声明自己的配置、颜色引用和 Widget/Screen 引用校验。
- 未来插件可添加类别、模板及色键；宿主必须显式注册模板，保持 ID 唯一。