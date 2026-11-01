# 定制跨平台 Sample

## 应用开场

`SampleCustomization.CreateOpeningSequence` 的有序工厂列表定义开场，默认两页均为每次启动显示且可跳过：内置 PNG、`Gal.Net` 和 `made with`，以及虚构声明与闪烁画面提示。每页 0.5 秒淡入、0.8 秒停留、0.6 秒淡出；布局在 `Views/EngineLogoPage.axaml` 和 `Views/DisclaimerPage.axaml`，文案在项目 Localization。最早的开场使用应用内置资源，在平台内容安装和玩家宿主创建前挂载，不依赖游戏 PAK。

开场 VM 位于 Sample 的 `Opening/`，继承 `OpeningPageViewModel`，只读配置为 `Id`、`CanSkip`、`ShowPolicy`（EveryLaunch/OncePerProfile/Disabled）。`OpeningSequenceView` 的 DataTemplates 提供 VM→View 映射。普通图片可复用页面和构造参数；特殊视频或控件演出提供自己的 VM/View。服务显示页面并等待视图布局后调用 `StartAsync(token)`；该方法返回不等于结束，页面通过受保护的 `Finish()` 报告完成。`FadingOpeningPageViewModel` 示例等待 View 提供的可取消动画后完成；项目自定义页也可等待视频事件或 `Task.Delay(duration, token)`。

开场不显示跳过按钮；鼠标左键或 Esc 跳过当前可跳过页。平台返回或服务的 SkipRemainingCommand 可跳过剩余可跳过页，不越过不可跳过页。正常完成、跳过、失败、重启及关闭均由服务幂等释放页面：取消 token、等待 StartAsync 收束，再调用 StopAsync 释放媒体、计时器和事件。页面自己的全部异步工作必须跟随 token 并被等待；不能启动无人持有的计时任务。Finish 只作用于当前展示实例，旧页回调不能结束新页。切页等待清理，不向下一页传递长按键重复输入。

OncePerProfile 使用 profile 下 `opening-pages.txt` 保存已处理 ID，展示后正常结束或允许跳过才记录；未展示便略过和取消不记录。更换 ID 可重新展示。默认 Sample 没有首次限定的演出；首次语言选择仍使用现有玩家偏好机制，在整个开场结束后单独出现，不属于可跳过列表。

开场和游戏准备独立运行。开场结束后，已有宿主可先显示首页，开始/继续/读档/设置/鉴赏等待会话就绪；关于和退出可用。平台准备尚未完成时保留纯背景与小型进度动效。准备失败会中止开场并显示错误，不等待演出播完。进入剧情所用 LoadingPage 与应用开场分开，返回标题和 Activity 重建不重新启动开场。

## 项目页面与宿主

`GalNet.Sample.CrossPlatform` 是正式维护的示例，共享主项目通过 `SampleCustomization` 注册页面、文案、契约和执行模块，Desktop/Android/iOS 项目提供平台入口。旧 Avalonia 桌面示例已退出版本控制，本机副本位于 Git 忽略的 `local-only/`，不再维护。

全部应用页面及其 VM 已位于主项目的 `Views/` 和 `ViewModels/`。改标题菜单编辑 `Views/TitlePage.axaml`；改开始/继续前的业务编辑 `ViewModels/TitlePageViewModel.cs`；改工具栏和承载布局编辑 `Views/GamePage.axaml`。设置、存读档、鉴赏及其详情同样直接编辑对应页面与 VM。语言、退出/返回确认及音乐公告布局在项目的 `LanguageSelectionView.axaml`、`PlayerView.axaml`；截图布局在 `ScreenshotDialog.axaml`。

项目 VM 组合内置状态：播放通过 `GamePresentationState`，存档行通过 `SaveSlotCollection`，鉴赏预览与播放通过 `GalleryThumbnailCollection`、`GalleryAudioState`、`GalleryVideoState`、`GalleryImageState`、`GalleryScenePlayback`。项目决定页面路由，内置状态负责取消、媒体及 Bitmap 释放。无需派生旧默认页面 VM或复制播放与资源管理逻辑。

项目命令通过 `GameLaunchFlow.StartNewGameAsync`（可带初始变量）、`ContinueAsync`、`LoadAsync` 调用会话；内置协调器识别已有两阶段启动能力并保证准备/呈现完成后开始播放。项目无需重复判断 `IPreparedGameSessionService`。直接使用底层 `RunAsync` 时，加载页的最短停留由传入的 `showLoading` 回调决定，协调器不固定展示时长。

`SampleCustomization.MinimumLoadingDwell` 和 `AudioAnnouncementDuration` 分别定义项目加载停留和音乐公告时长，默认 350ms/2.8 秒。前者允许零且拒绝负数，后者必须为正；公告时长可通过 `PlayerSurface.AudioAnnouncementDuration` 在代码/XAML 中设置，通用宿主默认 3 秒。退出确认、输入屏蔽和清理仍由内置机制维护。

`SampleCustomization` 集中注册页面、加载/返回回调、配色、本地化、契约和执行模块。`AddAvaloniaGameView` 只注册明确列出的页面和机制；普通页面的一条 VM→View 映射会自动注册两者的 Scoped 服务，不要求完整的固定页面列表。Gallery 另按 `cg/audio/video/scene` 等精确内容类型注册；缺失类型由项目诊断页显示。配色、尺寸及默认样式分别由 PlayerPalette、项目 Styles/Dimensions.axaml 和 Styles/Player.axaml 提供。

游戏布局映射也用于场景重播：主游戏保持 scoped 控件，重播新建同一映射类型，并向构造函数注入独立 `GamePresentationState`/项目游戏 VM；导航等其他服务仍借用当前 scope。自定义视图应通过这两个构造依赖或 Presentation/DataContext 读取本次状态，不应缓存主游戏状态作为全局单例。

直接接入宿主时，调用 `AvaloniaPlayerHost.CreateAsync(options, configure)`，注册 `IPlayerApplication` 提供根表面与启动/返回策略。Sample 的 `Views.PlayerView` 继承无页面布局的 `PlayerSurface` 行为基类，加载项目 XAML 后调用 `InitializeSurface()`；保留命名控件以复用确认取消、焦点恢复、输入屏蔽及公告清理。平台项目负责窗口或单视图挂载及生命周期，宿主负责 scope 和会话释放。

包内内容流安装使用 `Storage.FileSystem.BundledGameInstaller`；稳定 profile 目录通过 `GameProfilePaths.GetProfileDirectory(content, profileRoot)` 计算，应用自行提供绝对根。项目截图 primitive 负责自己的 schema/参数，通用 `SceneScreenshotWriter.SaveAsync` 负责捕获与安全写入，两者不要求框架引用项目契约。

通用 UI 文案由 GameHost 拥有；主项目的 `Localization/*.json` 保存项目标题/说明等覆盖。`AvaloniaPlayerLocalization.FromEmbeddedResources([typeof(SampleCustomization).Assembly])` 加载通用 UI 后叠加项目资源；内容和外部翻译仍按既定文化优先级覆盖，换资源程序集不改变 profile 身份或内容契约。

## 调试构建与玩家发行

CrossPlatform Sample 通过 MSBuild 属性 `EnableGameDebugging` 控制游戏调试能力，启用时定义 `GALNET_GAME_DEBUG`。Debug 配置默认开启，Release 默认关闭；也可以显式构建带调试能力的优化版本：

```powershell
dotnet build src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop -c Release -p:EnableGameDebugging=true
```

关闭时不编译调试服务、存档辅助工具、日志窗口和菜单，不引用 `GalNet.Avalonia.Debugging` 或 `GalNet.Debugging`，也不接入 Avalonia Developer Tools；普通文件日志继续保留。独立的 `EnableLaunchArguments` 定义 `GALNET_LAUNCH_ARGS`，默认跟随 `EnableGameDebugging`：桌面玩家版不编译内容路径、`--profile` 与调试参数解析，传入参数明确报错；开发版允许覆盖内容路径/profile。也可以在关闭游戏调试时显式开启普通参数；开启游戏调试则必须开启参数能力。

通用调试位于可选 `GalNet.Avalonia.Debugging`。通过 `AddGameDebugTools` 注册，宿主在 `GameDebugHostOptions` 中提供重建/重启回调和可选的打开日志目录动作；项目特有 replay 策略也留在项目配置。Sample 的桌面窗口统一使用项目内的 `UrsaWindow`，调试菜单挂在 `LeftContent`，日志使用独立 Ursa 窗口；不要用整行内容菜单覆盖标题栏拖动区和窗口按钮。开发版启动前或内容加载失败后仍保留日志入口，游戏就绪后才启用清空数据和调试存档。CrossPlatform 在重建 scope 前解绑旧调试菜单、事件及日志窗口。

自定义宿主在初始化完成后设置 `GameDebugViewModel.IsGameReady`，以控制清空数据和存档菜单的可用状态；日志和资源重载入口不依赖该状态。

移动端使用包裹游戏视图的 `DebugToolsOverlay`，确认与日志不需要额外窗口，隐藏打开目录入口；资源重载通过原进程内的 scope 重建完成。移动远程调试的传输与邀请入口尚未验收。窗口装饰、调色板和桌面调试接线留在各 Sample 项目，不增加共享玩家窗口基类。

本地化使用 `AvaloniaPlayerLocalization.FromEmbeddedResources` 时，将可选调试程序集放在项目资源程序集之前加载，使项目仍可覆盖通用文案；关闭调试时不加载该程序集。Sample 的启用接线可直接作为示例。

所有构建无内容参数时默认加载应用同级 `pak/`，不依赖当前工作目录。发行依赖位于 `lib/`，程序明确区分程序集根与应用根。缺失或损坏内容通过启动窗口报告。默认 profile 为用户 `LocalApplicationData/GalNet/<SHA-256>/`：摘要输入是 `project:` 加 game.pak 内的 projectId（旧安装读取 content.json），与安装路径和 buildId 无关；开发目录缺少内容身份时使用 `directory:` 加目录绝对路径。日志位于 profile 的 `logs/`。此策略由 Sample 显式传入共享会话，旧 profile 不自动迁移或删除。

原 Sample 的 `ConfigureServices` 参数 `enableDebug` 只允许在已经编入调试能力的构建中抑制服务注册，不能给玩家构建重新启用调试。普通关闭模式使用独立的 `packages.lock.NoGameDebugging.json`；切换开关后必须重新 restore，不能复用另一种依赖图的 `--no-restore` 输出。两个宿主 Desktop 发布的开发版使用 `packages.lock.Desktop.<rid>.Debugging.json`，玩家版使用 `packages.lock.Desktop.<rid>.json`，同时传入全局 `GalNetDesktopPublishTarget=<rid>`。原 Sample 的 WindowsReadyToRun 开发版使用 `packages.lock.WindowsReadyToRun.Debugging.json`。

仓库发行脚本默认 `-SampleFlavor Player`，显式关闭游戏调试和启动参数；`-SampleFlavor Developer` 使用带调试和参数能力的 Release 构建，ZIP 名追加 `-developer`。两种发行包都排除 PDB；外部调试通过协议，不提供源码级单步调试。两者均不改变契约程序集的编译参数和严格字节身份，也不改变 PAK 覆盖顺序或补丁策略。

默认宿主的扩展目标是让项目自己实现功能。手机、小游戏、商城等可以使用项目服务、XAML、primitive 与状态恢复组合，不需要给框架增加同名业务系统。

## 推荐改造顺序

从源码建立独立产品时，在仓库根执行 `python scripts/samples/export-template.py --output C:/work/MyGame`，选择尚不存在的输出目录。脚本按 Sample 的 `Template/files.json` 原样复制共享应用、平台入口、自有契约、Branding、移动 native 源码和必要构建附件，并记录文件来源与哈希；不复制引擎 C# 工程、测试和 CI，也不重写工程 XML。

进入输出目录，配置可取得所选 GalNet 版本的 NuGet 源，再按根 README 在 restore/build/publish 时传入 `-p:GalNetPackageVersion=<version>`。交付根设置 `GalNetUsePackages=true`，应用使用同版本 `GalNet.Avalonia` 和可选 `GalNet.Avalonia.Debugging`，契约使用 `GalNet.Contracts` 和构建期 `GalNet.SchemaGenerator`；仓库内默认仍使用源码引用。保留交付目录层级以复用 Import、资源和 native 相对路径。模板不附游戏内容，也不负责重命名和自动升级合并。

只改标题按钮通常只需修改一份 XAML；新增行为再修改对应 VM。新增普通页面需要 View/XAML、VM、组合映射和调用入口。换配色修改 `SampleCustomization.PlayerPalette`，单按钮完全自定义可直接写局部 `ControlTheme`。修改这些文件不会要求复制播放、取消、媒体或存档机制。

CLI/Editor、应用和内容必须使用同一套冻结契约产物。跨机器或跨 OS 重新编译相同源码不等于程序集字节一致；先确定实际交付的契约，再让导出工具与玩家共同消费它。

1. **建立项目契约。** 复制或引用 `GalNet.Sample.Contracts`，在 `ProjectSchemas.Modules` 中列出项目允许的 `IEntryModule`。构建后用 `scripts/contracts/build-project-contracts.ps1` 部署到内容项目的 `Contracts/`。CLI/Editor 与玩家必须消费同一契约集合。
2. **配置运行组合。** 在共享主项目的 `SampleCustomization` 中配置 catalog、执行模块、资源/玩家存储、音频 host 和项目启动策略。把项目专属 primitive 注册在组合入口，不为一条业务能力修改通用 Session 或关闭链。
3. **定制外观。** 直接编辑项目内页面、按钮和局部样式，通过 `PlayerPalette` 调整配色。普通页面全部属于项目；新页面在 `SampleCustomization` 加一条 VM→View 映射。按钮可直接声明自己的 Avalonia `ControlTheme`，无需建立整套主题协议。
4. **扩展项目功能。** schema/DTO 与 composite 放在契约项目；执行模块放在 Avalonia 或项目执行程序集。参数 DTO 用 `GenerateFieldSchema`/字段特性生成 descriptor，平台控件仅在页面/设置工厂中处理。

## 上线前检查

- Project `Contracts/` 是与当前 GalNet API 匹配的可信构建产物；重新构建游戏或 CLI 不会自动更新该目录。
- 自定义 entry 已在 CLI 校验/编译、Avalonia 运行和保存恢复路径中测试；Headless 缺少平台实现时应清晰报错。
- 项目实际使用的页面已注册，`GamePage` 布局可在目标分辨率和输入方式下工作。
- 退出、切换项目和读档均由会话链路释放旧的 presentation、媒体与资源。

配色和字体入口见[Themes README](../../../src/Presentation/GalNet.Avalonia.Themes/README.md)；运行与页面的精确约束见[Avalonia 游戏页面规范](../../spec/presentation/game-view.md)。
