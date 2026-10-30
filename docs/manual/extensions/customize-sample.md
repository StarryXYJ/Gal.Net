# 定制 Avalonia Sample

`GalNet.Sample.Avalonia` 是桌面组合根示例，保留启动参数、项目选择和可选调试策略。通用玩家视图、语言选择和 scope/会话协调位于 `Avalonia.GameHost`；`GalNet.Sample.Player` 当前只保留两个 Sample 共用的临时项目配置与契约执行适配。它尚未删除，两个主项目独立配置的迁移仍待完成。

从 `SamplePlayerServices` 调整共享玩家配置；桌面 `SampleCustomization` 继续提供组合适配入口。平台项目负责内容/profile 路径、窗口或单视图挂载及生命周期。不要把某个平台的 App、退出链和 DI scope 整份复制到其他入口。

直接接入通用宿主时，调用 `AvaloniaPlayerHost.CreateAsync(options, configure)`，在 `configure` 中注册页面/主题、本地化、设置与启动服务。可以注册 `PlayerView` 工厂，在该实例的 Resources/Styles 应用自己的主题和文案资源；通用视图不会自动引用 Sample 资源或选择固定配色。窗口宿主也可以直接创建该视图并复用已有会话。

包内内容流安装使用 `Storage.FileSystem.BundledGameInstaller`；稳定 profile 目录通过 `GameProfilePaths.GetProfileDirectory(content, profileRoot)` 计算，应用自行提供绝对根。项目截图 primitive 负责自己的 schema/参数，通用 `SceneScreenshotWriter.SaveAsync` 负责捕获与安全写入，两者不要求框架引用项目契约。

## 调试构建与玩家发行

Sample 通过 MSBuild 属性 `EnableGameDebugging` 控制游戏调试能力，启用时定义 `GALNET_GAME_DEBUG`。Debug 配置默认开启，Release 默认关闭；也可以显式构建带调试能力的优化版本：

```powershell
dotnet build src/Samples/GalNet.Sample.Avalonia -c Release -p:EnableGameDebugging=true
```

关闭时不编译调试服务、存档辅助工具、日志窗口和菜单，不引用 `GalNet.Debugging`，也不接入 Avalonia Developer Tools。独立的 `EnableLaunchArguments` 定义 `GALNET_LAUNCH_ARGS`，默认跟随 `EnableGameDebugging`：玩家版不编译内容路径、`--profile` 与调试参数解析，传入参数明确报错；开发版允许覆盖内容路径/profile。也可以在关闭游戏调试时显式开启普通参数；开启游戏调试则必须开启参数能力。

所有构建无内容参数时默认加载应用同级 `pak/`，不依赖当前工作目录。发行依赖位于 `lib/`，程序明确区分程序集根与应用根。缺失或损坏内容通过启动窗口报告。默认 profile 为用户 `LocalApplicationData/GalNet/<SHA-256>/`：摘要输入是 `project:` 加 game.pak 内的 projectId（旧安装读取 content.json），与安装路径和 buildId 无关；开发目录缺少内容身份时使用 `directory:` 加目录绝对路径。日志位于 profile 的 `logs/`。此策略由 Sample 显式传入共享会话，旧 profile 不自动迁移或删除。

`ConfigureServices` 的 `enableDebug` 只允许在已经编入调试能力的构建中抑制服务注册，不能给玩家构建重新启用调试。Release 关闭模式使用独立的 `packages.lock.NoGameDebugging.json`；切换开关后必须重新 restore，不能复用另一种依赖图的 `--no-restore` 输出。WindowsReadyToRun 玩家版使用该 profile 的独立锁文件，开发版使用 `packages.lock.WindowsReadyToRun.Debugging.json`。

仓库发行脚本默认 `-SampleFlavor Player`，显式关闭游戏调试、启动参数和 PDB；`-SampleFlavor Developer` 使用带调试和参数能力的 Release 构建并保留符号，ZIP 名追加 `-developer`。两者均不改变契约程序集的编译参数和严格字节身份，也不改变 PAK 覆盖顺序或补丁策略。

默认宿主的扩展目标是让项目自己实现功能。手机、小游戏、商城等可以使用项目服务、XAML、primitive 与状态恢复组合，不需要给框架增加同名业务系统。

## 推荐改造顺序

1. **建立项目契约。** 复制或引用 `GalNet.Sample.Contracts`，在 `ProjectSchemas.Modules` 中列出项目允许的 `IEntryModule`。构建后用 `scripts/contracts/build-project-contracts.ps1` 部署到内容项目的 `Contracts/`。CLI/Editor 与玩家必须消费同一契约集合。
2. **配置运行组合。** 在共享玩家的 `SamplePlayerServices` 中配置 catalog、执行模块、资源/玩家存储、音频 host 和项目启动策略；桌面入口通过 `SampleCustomization` 适配。把项目专属 primitive 注册在组合入口，不为一条业务能力修改通用 Session 或关闭链。
3. **定制外观。** 通过 `AddAvaloniaGameViewPages` 选择 `ClassicTheme`、`MinimalTheme` 或 `PaperTheme`，也可在 `configureViews` 覆盖单个页面。运行期主题切换不受支持。
4. **扩展项目功能。** schema/DTO 与 composite 放在契约项目；执行模块放在 Avalonia 或项目执行程序集。参数 DTO 用 `GenerateFieldSchema`/字段特性生成 descriptor，平台控件仅在页面/设置工厂中处理。

## 上线前检查

- Project `Contracts/` 是与当前 GalNet API 匹配的可信构建产物；重新构建游戏或 CLI 不会自动更新该目录。
- 自定义 entry 已在 CLI 校验/编译、Avalonia 运行和保存恢复路径中测试；Headless 缺少平台实现时应清晰报错。
- 主题的普通页面映射完整，`GamePage` 布局可在目标分辨率和输入方式下工作。
- 退出、切换项目和读档均由会话链路释放旧的 presentation、媒体与资源。

主题的可复制示例在[Themes README](../../../src/Presentation/GalNet.Avalonia.Themes/README.md)；运行与页面的精确约束见[Avalonia 游戏页面规范](../../spec/presentation/game-view.md)。
