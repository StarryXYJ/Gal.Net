# 组合、生命周期与所有权

## 内容、资源与宿主组合

宿主在组合期建立并冻结一对 `IResourceTypeCatalog`、`IGalleryTypeCatalog`。同一实例必须传入开发/安装内容 provider、资源 provider、Preview 和 exporter；catalog 不是从内容文件反向创建的。这样资源 DTO、PAK、Gallery 编译和页面选择能对插件类型使用同一身份表。

```text
开发项目 ── ProjectGameContentProvider ── .meta -> GalleryCatalog
           LocalFileProvider(Assets/) ───── GUID -> raw bytes

安装目录 ── InstalledGameContentProvider ─ game.pak / localization.pak / 外置 I18n
           PakFileProvider(Assets/Paks/**/*.pak) ─ GUID -> PAK bytes
```

`.galpak` 是带 `pak/` 外层的 ZIP 运输容器。v3 固定入口 game.pak 包含安装清单、content 描述、设置、人物和 Gallery 等内部文档；localization.pak 每种语言一个逻辑文本条目。`GameInstallation` 验证并安装后，Storage 从文档包构造中立内容对象，Graph/Group 从普通资源 PAK 加载。文档包使用 Assets 的独立固定 document 类型目录，不贡献普通资源 provider，也不受资源补丁替换。旧 v1/v2 galpak 与 content v2 散文档读取保持兼容。桌面 Sample 默认选择应用同级 `pak/`，应用根与托管依赖所在的 `lib/` 分离；此定位策略属于 Sample 组合根，不进入共享内容加载器。详见 [资源](../content/assets.md) 与 [文件格式](../content/file-formats.md)。

## 游戏运行

可选 `GalNet.Debugging` 组件提供本机长度帧 JSON 连接、会话/内容身份校验、App 服务和借用的运行绑定。组件可以引用 Runtime，Runtime 不反向依赖通信。Editor 与开发版 Sample 已提供外部调试接入，见[调试状态契约](../editor/debugging.md)；玩家构建可完全关闭。

`GameEngine` 持有 `IGameRuntime`，解释图、条目、Choice、skip batch 和快照；它不认识具体 UI。Engine 统一报告运行状态，负责停止、取消执行 scope 和等待内部 continuation 退出，不拥有执行 view 或平台后端。`IPrimitiveDispatcher` 解析 entry schema、创建 primitive instance 并调用 presenter 端口。影响场景的 primitive 先写入 `GameState`，再通知呈现端；读取快照后由宿主调用注册式 StateRestoreSession。

Avalonia 展示按领域拆为 dialogue/choice、layer、animation 和 particle presenter；`AvaloniaGamePresenters` 只创建并暴露这些 presenter，`CompositeGameView` 组合独立的 schema catalog 与执行模块，提供 Runtime 使用的 `IPrimitiveDispatcher`。`GameHost.GameSession` 串行化准备、开始、存档回调、运行替换与停止，拥有可替换的 `IGameRun`；默认 `GameRun` 组合 Engine 与展示恢复，不注册任何 Builtins，也不要求 Layer Presenter。`PlayerSession` 协调玩家持久化，`PlayerPreferences` 和 `PlayerStartup` 提供偏好与首次启动规则。文件实现通过 `GameResourceScope` 和 `FilePlayerSession` 组合。

`Avalonia.GameHost` 提供可直接使用的 `AvaloniaGameSession`、页面绑定 `AvaloniaGameRun`、展示组合 `AvaloniaGamePresentation`、纹理租约 `AvaloniaGameResources` 以及默认设置/窗口/手柄适配。`AvaloniaGameHostOptions` 由项目提供 catalog 和执行注册，预加载策略可替换；`AvaloniaGamePresentationOptions` 可替换 Layer 工厂与音频后端，新建适配对象的所有权交给展示会话。Sample 只保留启动参数、项目策略、本地化资源配置与调试扩展；默认本地化服务归 Avalonia.GameHost，中立 JSON 文本源适配归 GameHost。

会话、显示能力和 Gallery 路径契约位于 `Presentation.Abstractions.Application`。剧情音频的混音/DSP 位于 `Media.Audio`，原生设备/解码适配位于 `Media.Miniaudio`，均不引用 Avalonia。Miniaudio 使用 UTF-8 路径和静态原生回调，提供 Windows x64/ARM64 构建选择及跨平台 CMake 入口；当前实机验证 Windows x64，其他平台需要宿主部署对应原生库并验收。`Media.LibVlc` 保留视频/Gallery 的原生运行时，接受平台初始化工厂；剧情视频通过 `IVideoPresenter` 和 `AvaloniaGamePresentationOptions.CreateVideoPresenter` 替换，缺省仍为 Avalonia LibVLC 像素输出。自定义视频适配器由展示会话拥有，优先异步释放。释放顺序为运行/Engine → 展示恢复与执行 view → 平台后端 → Gallery 媒体与玩家持久化 → 纹理租约 → 内容资源。

Gallery catalog 是平台无关的静态内容。宿主把其自动生成的 `gallery_<item-id>_unlocked` Player bool 注册到变量服务；`GalleryDataSource` 合并 catalog 与当前 Player 值。Gallery 解锁不存于独立 UI 状态或存档槽。

Avalonia GameView 通过精确 Gallery `typeId` 的 `IGalleryPageRegistry` 选择页面。零个有内容类型时入口不可用，一个类型直达，多类型显示选择页；没有页面映射时进入诊断页，绝不按资源类型 fallback。Gallery 媒体的路径适配由宿主 resolver 完成，并且必须以 `AssetManager` acquire 的内容为来源。
