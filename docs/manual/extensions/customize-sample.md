# 定制 Avalonia Sample

`GalNet.Sample.Avalonia` 是桌面组合根示例，保留启动参数、项目选择和可选调试策略；CrossPlatform 的共享主项目负责其 Desktop/Android/iOS 组合。两个主项目各有 `SampleCustomization`，直接消费通用库和项目契约，不互相引用。

在所选主项目的 `SampleCustomization` 中调整主题/配色、页面映射、字段编辑器、HostOptions、catalog 和执行模块；少量相似配置允许分别维护。通用玩家视图、语言选择和 scope/会话协调位于 `Avalonia.GameHost`。平台项目负责内容/profile 路径、窗口或单视图挂载及生命周期，不复制通用资源释放或检查点机制。

直接接入通用宿主时，调用 `AvaloniaPlayerHost.CreateAsync(options, configure)`，在 `configure` 中注册页面/主题、本地化、设置与启动服务。可以注册 `PlayerView` 工厂，在该实例的 Resources/Styles 应用自己的主题和文案资源；通用视图不会自动引用 Sample 资源或选择固定配色。窗口宿主也可以直接创建该视图并复用已有会话。

包内内容流安装使用 `Storage.FileSystem.BundledGameInstaller`；稳定 profile 目录通过 `GameProfilePaths.GetProfileDirectory(content, profileRoot)` 计算，应用自行提供绝对根。项目截图 primitive 负责自己的 schema/参数，通用 `SceneScreenshotWriter.SaveAsync` 负责捕获与安全写入，两者不要求框架引用项目契约。

通用 UI 文案由 GameHost 拥有；主项目的 `Localization/*.json` 保存项目标题/说明等覆盖。`AvaloniaPlayerLocalization.FromEmbeddedResources([typeof(SampleCustomization).Assembly])` 加载通用 UI 后叠加项目资源；内容和外部翻译仍按既定文化优先级覆盖，换资源程序集不改变 profile 身份或内容契约。

## 调试构建与玩家发行

两个 Sample 通过 MSBuild 属性 `EnableGameDebugging` 控制游戏调试能力，启用时定义 `GALNET_GAME_DEBUG`。Debug 配置默认开启，Release 默认关闭；也可以显式构建带调试能力的优化版本：

```powershell
dotnet build src/Samples/GalNet.Sample.Avalonia -c Release -p:EnableGameDebugging=true
```

关闭时不编译调试服务、存档辅助工具、日志窗口和菜单，不引用 `GalNet.Avalonia.Debugging` 或 `GalNet.Debugging`，也不接入 Avalonia Developer Tools；普通文件日志继续保留。独立的 `EnableLaunchArguments` 定义 `GALNET_LAUNCH_ARGS`，默认跟随 `EnableGameDebugging`：桌面玩家版不编译内容路径、`--profile` 与调试参数解析，传入参数明确报错；开发版允许覆盖内容路径/profile。也可以在关闭游戏调试时显式开启普通参数；开启游戏调试则必须开启参数能力。

通用调试位于可选 `GalNet.Avalonia.Debugging`。通过 `AddGameDebugTools` 注册，宿主在 `GameDebugHostOptions` 中提供重建/重启回调和可选的打开日志目录动作；项目特有 replay 策略也留在项目配置。两个 Sample 的桌面窗口统一使用项目内的 `UrsaWindow`，调试菜单挂在 `LeftContent`，日志使用独立 Ursa 窗口；不要用整行内容菜单覆盖标题栏拖动区和窗口按钮。开发版启动前或内容加载失败后仍保留日志入口，游戏就绪后才启用清空数据和调试存档。CrossPlatform 在重建 scope 前解绑旧调试菜单、事件及日志窗口。

自定义宿主在初始化完成后设置 `GameDebugViewModel.IsGameReady`，以控制清空数据和存档菜单的可用状态；日志和资源重载入口不依赖该状态。

移动端使用包裹游戏视图的 `DebugToolsOverlay`，确认与日志不需要额外窗口，隐藏打开目录入口；资源重载通过原进程内的 scope 重建完成。移动远程调试的传输与邀请入口尚未验收。窗口装饰、调色板和桌面调试接线留在各 Sample 项目，不增加共享玩家窗口基类。

本地化使用 `AvaloniaPlayerLocalization.FromEmbeddedResources` 时，将可选调试程序集放在项目资源程序集之前加载，使项目仍可覆盖通用文案；关闭调试时不加载该程序集。两个 Sample 的启用接线可直接作为示例。

所有构建无内容参数时默认加载应用同级 `pak/`，不依赖当前工作目录。发行依赖位于 `lib/`，程序明确区分程序集根与应用根。缺失或损坏内容通过启动窗口报告。默认 profile 为用户 `LocalApplicationData/GalNet/<SHA-256>/`：摘要输入是 `project:` 加 game.pak 内的 projectId（旧安装读取 content.json），与安装路径和 buildId 无关；开发目录缺少内容身份时使用 `directory:` 加目录绝对路径。日志位于 profile 的 `logs/`。此策略由 Sample 显式传入共享会话，旧 profile 不自动迁移或删除。

原 Sample 的 `ConfigureServices` 参数 `enableDebug` 只允许在已经编入调试能力的构建中抑制服务注册，不能给玩家构建重新启用调试。普通关闭模式使用独立的 `packages.lock.NoGameDebugging.json`；切换开关后必须重新 restore，不能复用另一种依赖图的 `--no-restore` 输出。两个宿主 Desktop 发布的开发版使用 `packages.lock.Desktop.<rid>.Debugging.json`，玩家版使用 `packages.lock.Desktop.<rid>.json`，同时传入全局 `GalNetDesktopPublishTarget=<rid>`。原 Sample 的 WindowsReadyToRun 开发版使用 `packages.lock.WindowsReadyToRun.Debugging.json`。

仓库发行脚本默认 `-SampleFlavor Player`，显式关闭游戏调试和启动参数；`-SampleFlavor Developer` 使用带调试和参数能力的 Release 构建，ZIP 名追加 `-developer`。两种发行包都排除 PDB；外部调试通过协议，不提供源码级单步调试。两者均不改变契约程序集的编译参数和严格字节身份，也不改变 PAK 覆盖顺序或补丁策略。

默认宿主的扩展目标是让项目自己实现功能。手机、小游戏、商城等可以使用项目服务、XAML、primitive 与状态恢复组合，不需要给框架增加同名业务系统。

## 推荐改造顺序

1. **建立项目契约。** 复制或引用 `GalNet.Sample.Contracts`，在 `ProjectSchemas.Modules` 中列出项目允许的 `IEntryModule`。构建后用 `scripts/contracts/build-project-contracts.ps1` 部署到内容项目的 `Contracts/`。CLI/Editor 与玩家必须消费同一契约集合。
2. **配置运行组合。** 在所选主项目的 `SampleCustomization` 中配置 catalog、执行模块、资源/玩家存储、音频 host 和项目启动策略。把项目专属 primitive 注册在组合入口，不为一条业务能力修改通用 Session 或关闭链。
3. **定制外观。** 通过 `AddAvaloniaGameViewPages` 选择 `ClassicTheme`、`MinimalTheme` 或 `PaperTheme`，也可在 `configureViews` 覆盖单个页面。运行期主题切换不受支持。
4. **扩展项目功能。** schema/DTO 与 composite 放在契约项目；执行模块放在 Avalonia 或项目执行程序集。参数 DTO 用 `GenerateFieldSchema`/字段特性生成 descriptor，平台控件仅在页面/设置工厂中处理。

## 上线前检查

- Project `Contracts/` 是与当前 GalNet API 匹配的可信构建产物；重新构建游戏或 CLI 不会自动更新该目录。
- 自定义 entry 已在 CLI 校验/编译、Avalonia 运行和保存恢复路径中测试；Headless 缺少平台实现时应清晰报错。
- 主题的普通页面映射完整，`GamePage` 布局可在目标分辨率和输入方式下工作。
- 退出、切换项目和读档均由会话链路释放旧的 presentation、媒体与资源。

主题的可复制示例在[Themes README](../../../src/Presentation/GalNet.Avalonia.Themes/README.md)；运行与页面的精确约束见[Avalonia 游戏页面规范](../../spec/presentation/game-view.md)。
