# Control 与默认游戏页面

## 当前范围

`GalNet.Control` 是当前默认游戏 UI 的 Avalonia 实现。它提供固定的内置页面、运行期游戏 View 和页面流工厂；它不是 Widget/Screen 模板市场，也不从项目目录直接读取或保存 UI 文件。

页面外观由 `UiProject` 中每种页面的“预设 ID + 字符串设置覆盖”决定。宿主创建 `GameFlowOptions`，传入游戏内容、`UiProject`、可选的资源、存档、变量和进度服务，再由 `GameFlowFactory` 组合页面与一次游戏运行。

```
宿主 / 编辑器预览
  └─ GameFlowFactory
       ├─ GamePageHostViewModel + GameScreenNavigator
       ├─ 标题、设置、存读档、鉴赏、关于页面
       └─ 游戏页面 + DefaultGameView + GameRunViewModel
            └─ Runtime.GameEngine
```

`GalNet.Avalonia.GameView` 是另一套较轻量的独立页面宿主：它用 `IGameNavigationService`、`GameShell` 和不可变的 ViewModel→View 注册表承载标题、游戏、存档、设置、鉴赏和关于页面。编辑器预览使用这套页面宿主与 `AvaloniaGamePageView`，而不是直接让 Runtime 依赖 Avalonia。

## 内置页面和路由

`GameScreenNavigator` 保存当前页面和回退栈；`GameFlowFactory.BuildScreen()` 只接受下列路由键：

| 路由键 | 页面 / ViewModel | 说明 |
| --- | --- | --- |
| `title` | `GameStartViewModel` 或 `TextMenuTitleViewModel` | 开始、继续、设置、鉴赏、关于与退出 |
| `game` | `GameRunViewModel` | 创建或恢复 `GameEngine`，并承载默认游戏 View |
| `settings` | `SettingsViewModel` | 运行期设置 |
| `save-load` | `SaveLoadViewModel` | 默认读取；参数为 `"save"` 时进入保存模式 |
| `gallery` | `GalleryViewModel` | 已解锁内容 |
| `about` | `AboutViewModel` | 读取可选 Markdown 资源 |

未知路由会抛出错误，不存在历史文档中所述的“按类别查找自定义 Screen 实例后回退”的机制。截图是游戏运行页的覆盖层，由 `ScreenshotDialog` 通过 Ursa 的 `OverlayDialog` 打开，不参与页面导航。

## UI 预设

`GalNet.Control.Abstraction.UI` 当前的稳定契约是预设元数据和设置 schema：

- `IUiPagePreset`：预设 ID、适用的 `UiPageKind`、编辑器本地化名称/说明键，以及默认设置；
- `IUiPresetRegistry`：按页面查找预设、按 ID 获取预设和取得默认预设；
- `UiSettingDefinition`：设置键、类型、默认值、数值范围、选项或资源筛选器；
- `IGameScreenNavigator`：固定内置页面的绑定式导航状态。

内置预设包括两个标题页预设（按钮菜单与文字菜单）以及 Game、Settings、SaveLoad、Gallery、About 各一个默认预设。`GameFlowFactory` 总是先取得预设默认值，再叠加项目中的设置，最后转换为非持久化的 `*UiConfiguration`。解析过程不修改 `UiProject`，页面也不读取 JSON 或文件系统。

当前 `UiSettingType` 支持 `Text`、`Integer`、`Float`、`Color`、`Asset`、`Boolean` 与 `Select`。资源型设置只保存资源 ID；由宿主传入的 `IAssetManager` 负责实际解析。

## 运行期呈现边界

Runtime 只依赖 `GalNet.Presentation.Abstractions` 中的 `IGameView` 与细分接口（文本、交互、图层、转场、音频、视频、效果）。`DefaultGameView` 是 Control 内的默认组合实现；`NullGameView` 与 Headless 呈现用于测试或无界面宿主。

实际 Avalonia 游戏页面可由 `AvaloniaGamePageView` 实现图层、文本和输入端口。它通过宿主提供的 `IGamePageLayerFactory` 解析图层图像，因而共享页面不会自行推断资源路径。`animate` 的逐帧视觉更新由该呈现实现完成；Runtime 仅提交动画结束后的场景属性。

## 宿主接入约束

- 宿主负责提供 `IGameContentProvider`、`UiProject` 和按需的资源/玩家数据服务；Control 不拥有项目目录。
- 每次独立运行必须使用独立的游戏会话与页面作用域，不能复用上一次 `GameRunViewModel`。
- 扩展页面 View 映射时，在 `AddAvaloniaGameViewPages()` 的注册回调中完成；注册表构建后不可修改。
- 新增固定页面时，需要同时补充路由分支、预设 schema、页面 View/ViewModel、资源解析与导航测试。

旧版文档中关于 `IWidgetTemplate`、`IScreenTemplate`、`IColorPalette`、`UI/ui.json`、`WidgetInstance` 和 `ScreenInstance` 的描述不是当前实现的一部分。
