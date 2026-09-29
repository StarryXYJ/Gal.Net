# Avalonia 游戏页面

## 当前范围

`GalNet.Avalonia.GameView` 是官方共享的 Avalonia 游戏页面宿主。它提供可替换的 `GameShell`、标题、加载、游戏、存读档、设置、鉴赏、关于和截图页面，并通过组合期注册表完成 ViewModel 到 View 的映射。

页面宿主不读取游戏文件，也不持有项目目录。宿主负责提供游戏内容、资源、存档、玩家变量、进度和媒体实现；编辑器预览与官方 Avalonia Sample 在各自的组合根中装配这些服务。

```text
宿主 / 编辑器预览
  └─ GameShell
       ├─ IGameNavigationService
       ├─ 标题、加载、存读档、设置、鉴赏、关于页面
       └─ GamePage + AvaloniaGamePageView
            ├─ Dialogue / Layer / Animation / Particle presenter
            └─ CompositeGameView ── Runtime.GameEngine
```

`IGameNavigationService` 只管理一个游戏 Scope 内的页面状态和回退历史。`IPageViewRegistry` 在组合期建立不可变的 ViewModel→View 映射，`IPageViewFactory` 从同一 Scope 解析页面并设置 `DataContext`。页面导航参数通过 `IActivatablePageViewModel<TArgs>` 传递，不注册到 DI。

## 页面与组合

默认页面由 `AddAvaloniaGameViewPages()` 注册：

| 页面 | 作用 |
| --- | --- |
| `TitlePage` / `TitlePageViewModel` | 开始、继续和进入其他页面 |
| `LoadingPage` / `LoadingPageViewModel` | 游戏会话加载状态 |
| `GamePage` / `GamePageViewModel` | 游戏场景、对话、选择和交互 |
| `SaveSlotsPage` / `SaveSlotsPageViewModel` | 存档槽和读写操作 |
| `SettingsPage` / `SettingsPageViewModel` | 运行期设置 |
| `GalleryPage` / `GalleryPageViewModel` | 已解锁内容 |
| `AboutPage` / `AboutPageViewModel` | 宿主提供的关于内容 |

宿主可以在 `AddAvaloniaGameViewPages()` 的注册回调中覆盖默认 View，或者追加自定义页面。注册表构建后不可修改。

### Gallery 页面

Gallery 使用独立的 `IGalleryPageRegistry`，以精确 Gallery `typeId` 映射到页面 ViewModel 和 View。`Add<TViewModel, TView>` 注册新类型，`Replace<TViewModel, TView>` 显式覆盖既有类型；多个 type ID 可以映射到同一页面。该注册同时写入普通 ViewModel→View registry 和 DI，构建后不可修改。

`GalleryNavigationService` 不按资源类型推断页面：没有有内容类型时入口不可用，只有一个时直接打开该页，多个时先进入类型选择页；未注册页面会显示诊断页。CG、视频和音频是独立页面/媒体状态，不使用 renderer enum 或单一多媒体 ViewModel。

## 运行期呈现边界

Runtime 只依赖 `GalNet.Presentation.Abstractions` 中的 `IGameView` 与细分接口（文本、交互、图层、转场、音频、视频、效果）。`AvaloniaGamePageView` 是页面侧 presenter 的组合器，分别暴露 dialogue/choice、layer、animation 和 particle 端口；宿主通过 `CompositeGameView` 把这些端口组合成 Runtime 使用的 `IGameView`。`NullGameView` 供测试和无界面宿主使用。

各 presenter 共享 `IAvaloniaUiDispatcher`，但分别拥有自己的等待、动画和粒子生命周期。`AvaloniaGamePageView` 通过宿主提供的 `IGamePageLayerFactory` 解析图层图像，因而共享页面不会自行推断资源路径。动画逐帧更新由呈现实现完成；Runtime 只维护可存档的场景状态和动画结束后的稳定属性。

官方 Sample 使用 `SampleGameResourceScope`、`SampleSaveSession`、`SampleGameRunner` 和 `PersistentSceneRestorer` 分离资源、玩家持久化、运行编排与读档展示重放。读档顺序固定为恢复 Runtime snapshot、重放持久展示、页面切换后启动 prepared run；reload 和清空玩家状态必须先停止并释放 runner，再释放资源或重建持久化状态。

## 宿主接入约束

- 每次独立运行必须使用独立的游戏会话与页面 Scope；切换项目或重启预览时释放旧 Scope。
- 宿主负责实现 `IGamePageLayerFactory`、`IGameContentProvider`、存档、玩家变量、进度和媒体端口。
- 共享页面可以被替换或覆写；最终客户端的窗口、主题、资源定位和平台服务不进入共享页面项目。
- 新增页面时，需要补充 ViewModel、View、组合注册和导航测试。
