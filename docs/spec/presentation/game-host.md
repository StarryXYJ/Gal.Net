# 游戏宿主与玩家会话

`GameHost` 组合内容、运行、玩家状态和持久化规则；`Avalonia.GameHost` 提供默认 Avalonia 会话与平台适配。Runtime 仍独立负责剧情执行，宿主不复制 Engine 状态机。

## 会话边界

`PlayerSession` 持有玩家级内容与服务；`GameSession`/运行接口协调新游戏、读档和终止。`IGameRun`/`GameRun` 拥有一次运行的执行与呈现资源。替换运行必须遵守候选快照预检、停止旧运行、创建新运行及恢复的实际顺序；失败不应被描述为任意平台资源可自动回滚。

`AvaloniaGameHostOptions.AttachPlayer` 与 `AttachRun` 接收借用对象。返回的绑定由宿主释放，支持异步清理；运行绑定在 Engine 停止后结束，玩家绑定在所有运行结束后、玩家存储和资源释放前结束。

## Avalonia 玩家宿主

`Avalonia.GameHost` 提供 `AvaloniaPlayerHost`、`Views.PlayerView` 与首次语言选择 UI。`AvaloniaPlayerHost.CreateAsync(options, configure)` 由调用方注册项目页面/主题、本地化、设置、启动服务及平台后端；宿主不选择 Sample 的主题、项目契约或本地化程序集。视图默认可以独立使用，也可以通过 DI 替换工厂，并由项目提供资源和样式。

`AvaloniaPlayerHost` 在 UI 线程创建和操作，建立 DI 根与玩家 scope，挂载视图；初始化可等待用户选择语言。释放先取消初始化并等待结束，再停止会话、解绑窗口/视图、恢复借用的本地化解析器、释放 scope 与服务根。构造失败同样清理已创建的异步服务。暂停停止 Gallery/剧情媒体并落盘检查点，回到标题；标题页根据退出能力返回宿主或显示确认，不把 Activity 重建等同于新建剧情运行。

`Storage.FileSystem.BundledGameInstaller` 接受宿主拥有的内容流，将其复制、计算 hash 并交给正式安装器校验到分版缓存；不会关闭调用方的流。`GameProfilePaths.GetProfileDirectory(content, profileRoot)` 根据项目身份计算稳定目录，应用名与操作系统默认根由宿主决定。`SceneScreenshotWriter.SaveAsync` 捕获借用的 GamePage 并以不覆盖的方式发布 PNG，拒绝路径穿越/目录链接；项目 entry schema 和文件名业务约束由项目执行适配提供。

当前两个 Sample 仍通过 `GalNet.Sample.Player` 的临时配置层接入上述机制。该工程保留项目主题、本地化资源、契约注册、默认目录策略和小型委托，不再拥有通用视图、语言选择、会话释放及包流安装实现。两主项目的独立配置和该临时工程的删除仍属于后续迁移。

运行状态与存档见[运行时状态](../runtime/state-and-save.md)，依赖和释放规则见[生命周期](../architecture/lifecycles.md)。使用与定制分别见 [Sample 手册](../../manual/sample/README.md)和[扩展入口](../../manual/extensions/customize-sample.md)。

## 对白历史与重播

默认宿主提供从当前路线新游戏开始的历史；存读档通过宿主会话 API 一并保存/恢复。GamePageViewModel.OpenHistoryCommand 和输入 open.history（默认 WheelUp）打开覆盖层，CloseHistoryCommand 返回。自定义 GamePage 模板可增加 PART_History ContentPresenter，并以 TemplateBinding History 承载共享面板；原有四个必需承载点仍保持兼容。

历史 journal 在 GameHost，实际文本/音频执行观察在 Avalonia.GameHost。默认组合注册 host.dialogue.history 计数状态；实际保存时才附加完整历史前缀到 GameSnapshot.Attachments，不应把正文复制到每次稳定快照。直接使用 Engine 的项目若需要完整历史，应自行接入对应宿主保存/恢复，而非期待 Engine 解释附加数据。

同源相邻 audio.play 和文本以 rawId#n 的稳定编译身份关联；手写不同源音频不会自动变为对白语音。重播借用后端及原 track 的混音，以独立临时 voice 播放，不重新执行剧情。有效推进包括补全文字都会停止；单纯滚动历史或调整音量不会停止。

默认设置增加 enableReadTextColor 与 readTextColor 玩家偏好，初始关闭、颜色 #A8B8C8。ReadTextStyle 的可继承 Foreground/WasRead 只作用于 TypewriterTextBlock 普通色片段；自定义布局无需改写富文本内容。ColorView 的 Fluent 样式由设置编辑器在自身作用域加载。
