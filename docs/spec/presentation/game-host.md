# 游戏宿主与玩家会话

`GameHost` 组合内容、运行、玩家状态和持久化规则；`Avalonia.GameHost` 提供默认 Avalonia 会话与平台适配。Runtime 仍独立负责剧情执行，宿主不复制 Engine 状态机。

## 会话边界

`PlayerSession` 持有玩家级内容与服务；`GameSession`/运行接口协调新游戏、读档和终止。`IGameRun`/`GameRun` 拥有一次运行的执行与呈现资源。替换运行必须遵守候选快照预检、停止旧运行、创建新运行及恢复的实际顺序；失败不应被描述为任意平台资源可自动回滚。

`AvaloniaGameHostOptions.AttachPlayer` 与 `AttachRun` 接收借用对象。返回的绑定由宿主释放，支持异步清理；运行绑定在 Engine 停止后结束，玩家绑定在所有运行结束后、玩家存储和资源释放前结束。

## Avalonia 玩家宿主

Sample 的应用开场由项目 `OpeningSequence` 持有有序页面工厂，页面继承 `OpeningPageViewModel` 并异步释放；服务负责顺序、跳过及展示实例隔离，具体页面负责演出，View 负责纯视觉动画。App 在平台内容准备前挂载开场，与会话初始化并行；首次语言选择等待开场结束。首页可在会话就绪前显示，依赖会话的入口必须同时检查就绪状态。演出跳过不取消内容准备；关闭、重启或演出结束会取消并等待页面工作收束。通用宿主及 Runtime 不认识具体 Logo 或提示页。

`Avalonia.GameHost` 提供 `AvaloniaPlayerHost`、无页面布局的 `Views.PlayerSurface` 行为基类和首次语言选择机制。Sample 使用自己的 PlayerView/LanguageSelectionView XAML。`AvaloniaPlayerHost.CreateAsync(options, configure)` 由调用方注册应用 UI、本地化、设置、启动服务及平台后端。`IPlayerApplication` 提供 Control 表面、挂载/解绑、语言选择、启动状态及返回/退出策略；宿主不解析具体标题或游戏页面 VM，`View` 返回 Control，退出确认通过 `ConfirmExitAsync` 委托给应用。外部调试启动同样通过该应用的 `StartNewGameAsync`，不会绕过项目自己的开始命令。项目可以接入自己的表面而不解析内置 PlayerView/GameShell/TitlePageViewModel；应用必须显式注册 IPlayerApplication，不再提供固定页面回退。

`AvaloniaPlayerHost` 在 UI 线程创建和操作，建立 DI 根与玩家 scope，挂载视图；初始化可等待用户选择语言。释放先取消初始化并等待结束，再停止会话、解绑窗口/视图、恢复借用的本地化解析器、释放 scope 与服务根。构造失败同样清理已创建的异步服务。暂停停止 Gallery/剧情媒体并落盘检查点，回到标题；标题页根据退出能力返回宿主或显示确认，不把 Activity 重建等同于新建剧情运行。

宿主创建期间先等待后台完成 LibVLC 实例构造，再分阶段回到 UI 解析设置、会话和页面。`CreateAsync` 的取消必须等待原生工厂实际返回后才释放 scope。会话的内容检查、catalog 创建、解码注册、纹理预载及玩家/鉴赏资源准备在一个串行后台流程中完成，成功后一次性交接所有权并在 UI 发布状态；失败或取消在准备侧清理已取得的资源。相关项目回调不得访问 UI。SDL 的初始化、轮询和释放仍受同一 UI 线程约束；Sample 在开场和会话均完成后才启用它，显示设置提前绑定。

`Storage.FileSystem.BundledGameInstaller` 接受宿主拥有的内容流，将其复制、计算 hash 并交给正式安装器校验到分版缓存；不会关闭调用方的流。`GameProfilePaths.GetProfileDirectory(content, profileRoot)` 根据项目身份计算稳定目录，应用名与操作系统默认根由宿主决定。`SceneScreenshotWriter.SaveAsync` 捕获借用的 GamePage 并以不覆盖的方式发布 PNG，拒绝路径穿越/目录链接；项目 entry schema 和文件名业务约束由项目执行适配提供。

当前维护的 CrossPlatform Sample 在单个应用项目内通过 `SampleCustomization` 注册页面、项目契约/执行模块及目录策略，平台项目提供最小入口。全部应用页面及其 VM、截图、语言和确认/公告布局位于 Sample 的 Views/ViewModels；游戏 VM 组合 scoped GamePresentationState，不继承内置页面 VM。PlayerApplication 决定默认页面、平台返回和退出；宿主继续负责 scope、初始化取消与释放。复杂存档行、预览和媒体/重播状态留在内置层，场景重播组合项目游戏布局与独立呈现状态。旧主题及页面适配已删除，应用样式只在 Sample 源码中维护。

`AvaloniaPlayerLocalization.FromEmbeddedResources` 先加载 GameHost 的通用 UI 文案，再按顺序加载调用方提供的资源程序集。项目标题/说明由项目资源拥有，通用 UI 不包含 Sample 身份或调试文案。AttachGame 保留原始的精确文化来源，由最终 resolver 统一处理翻译覆盖、父文化和默认语言回退，避免内层提前回退遮住外部译文。

运行状态与存档见[运行时状态](../runtime/state-and-save.md)，依赖和释放规则见[生命周期](../architecture/lifecycles.md)。使用与定制分别见 [Sample 手册](../../manual/sample/README.md)和[扩展入口](../../manual/extensions/customize-sample.md)。

## 可选 Avalonia 调试

`GalNet.Avalonia.Debugging` 复用 `Avalonia.GameHost` 与协议层 `GalNet.Debugging`，提供调试命令、快照辅助、500 行有界日志、日志视图/桌面窗口以及外部调试会话适配。普通 GameHost/GameView 不反向引用该模块，也不依赖调试协议。模块不引用 Sample 或项目契约。

组合根调用 `AddGameDebugTools(GameDebugHostOptions)`，提供必需的 `RestartAsync` 和可选的 `OpenLogDirectory`；未提供打开目录能力时隐藏对应入口。项目指令的 replay 策略由宿主传入，内置策略可通过 `CreateBuiltinReplayPolicy()` 扩充。清空玩家状态、资源重载和快照保存借用正常会话及其命令/存档门禁。`AvaloniaGameSession.WithSnapshotAsync<T>` 与 `RefreshSlotsAsync` 是中立的会话扩展入口，不专属调试。

`DebugToolsOverlay` 可包裹现有游戏视图，在同一 TopLevel 内显示菜单、日志和确认；系统返回优先关闭调试确认/日志。释放取消未完成确认、解绑事件并拆下内容；Activity 重建重挂载整个调试容器，保留原玩家视图。`DebugLogView` 仅在视觉树挂载期间订阅日志，异步 UI 更新使用挂载代次隔离。桌面宿主也可自行组合 `DebugMenuView` 与 `DebugLogWindow`。

`AvaloniaExternalDebugSession` 负责连接、断开、运行绑定和异步释放；桌面 `DebugLaunchArguments` 使用实际契约文件验证邀请中的 SHA256。移动端可嵌入调试 UI，但传输与邀请入口仍须由平台实现并单独验收。构建开关在组合根决定是否引用整个模块，不能靠运行时注册为玩家版补回调试能力。

## 对白历史与重播

默认宿主提供从当前路线新游戏开始的历史；存读档通过宿主会话 API 一并保存/恢复。GamePageViewModel.OpenHistoryCommand 和输入 open.history（默认 WheelUp）打开覆盖层，CloseHistoryCommand 返回。自定义 GamePage 模板可增加 PART_History ContentPresenter，并以 TemplateBinding History 承载共享面板；原有四个必需承载点仍保持兼容。

历史 journal 在 GameHost，实际文本/音频执行观察在 Avalonia.GameHost。默认组合注册 host.dialogue.history 计数状态；实际保存时才附加完整历史前缀到 GameSnapshot.Attachments，不应把正文复制到每次稳定快照。直接使用 Engine 的项目若需要完整历史，应自行接入对应宿主保存/恢复，而非期待 Engine 解释附加数据。

同源相邻 audio.play 和文本以 rawId#n 的稳定编译身份关联；手写不同源音频不会自动变为对白语音。重播借用后端及原 track 的混音，以独立临时 voice 播放，不重新执行剧情。有效推进包括补全文字都会停止；单纯滚动历史或调整音量不会停止。

默认设置增加 enableReadTextColor 与 readTextColor 玩家偏好，初始关闭、颜色 #A8B8C8。ReadTextStyle 的可继承 Foreground/WasRead 只作用于 TypewriterTextBlock 普通色片段；自定义布局无需改写富文本内容。ColorView 的 Fluent 样式由设置编辑器在自身作用域加载。

默认设置还提供四档 `FontSizePreset`，以稳定枚举名称保存到玩家变量 `player.galnet.settings.fontSize`，沿用设置应用/保存队列。`AvaloniaPlayerSettingsService` 的 `defaultFontSize` 参数决定无有效保存值时的默认档（未指定时 Smaller），不负责具体字体数值。Sample 按平台传入默认档，由项目 `PlayerTypography` 定义各语义字号。恢复仅读取，未知名称、数值型或错误类型回退到平台默认，不写回默认值；恢复结束发出 SettingsChanged，应用更新局部资源。应用卸载时解除字号订阅；无须重建页面或修改剧情状态。
