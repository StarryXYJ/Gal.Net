# GalNet cross-platform sample

当前 CrossPlatform 已接入游戏会话，不再是欢迎页骨架。本项目是后续正式维护的图形 Sample，通过共享主项目的 `SampleCustomization` 注册页面、项目文案、契约、执行模块与目录策略。旧 Avalonia 桌面 Sample 已从仓库移除，本机副本保留在 Git 忽略的 `local-only/src/Samples/GalNet.Sample.Avalonia/`，不再同步或维护。

主项目的 `Views/` 和 `ViewModels/` 拥有标题、加载、游戏、设置、存读档、鉴赏及详情、关于、截图、语言选择和确认/公告布局。按钮与页面命令直接编辑项目源码；普通页面只需在 `SampleCustomization` 注册一次 VM→View 映射，VM/View 自动注册为 Scoped，Gallery 另按实际内容类型注册。`PlayerApplication` 提供应用启动/返回策略。

Sample 内置英语、简体中文、日语和韩语界面。`Localization/` 的日语和韩语资源也覆盖共享播放器界面；游戏正文由游戏内容单独提供，缺少所选语言时按游戏的语言回退规则显示。

内置 `GamePresentationState`、`SaveSlotCollection`、Gallery 的缩略图/媒体/重播状态及 `PlayerSurface` 保留播放、异步取消、预览释放、焦点和输入屏蔽机制。项目页面组合这些对象，不复制机制，也不派生旧默认页面 VM。场景鉴赏使用项目游戏布局和独立呈现状态，不能通过菜单进入主会话存读档/设置。

整套主题与旧页面适配已删除。配色库只提供语义颜色和字体回退；应用样式与尺寸位于项目 Styles/。会话/scope 所有权来自 `GalNet.Avalonia.GameHost`，内容安装及 profile 身份计算来自 `GalNet.Storage.FileSystem`。

| 工程 | 职责与当前验证 |
| --- | --- |
| `SampleCustomization` | 本主项目的页面、配色、本地化、契约与执行组合配置 |
| CrossPlatform 共享 App | 会话组合、Activity 视图重挂载、scope 重建和关闭 |
| Desktop | Windows/Linux/macOS 入口；Windows/WSLg 启动、会话和正常退出已验证，macOS 验收延期 |
| Android | ARM64 真机已安装启动，语言页、剧情视频叠加立绘、声音与触摸推进通过基础 smoke；存读档/Gallery/生命周期完整验收待补 |
| iOS | Bundle 内容、静态音频/VLC、AVAudioSession 与生命周期已接入；尚未在 Mac 构建/运行 |
| Browser | 保留原工程，不在本 feature 支持范围内 |

SDK 固定 `10.0.401`，Avalonia `12.0.5`；仓库开发默认引用内置源码工程。独立交付仍使用同一份 Sample，按明确清单复制必要构建附件，由交付根配置切换为同版本包引用，无需手改工程 XML。

## 复制为独立项目

在仓库根执行以下命令，输出目录必须尚不存在：

```powershell
python scripts/samples/export-template.py --output C:/work/MyGame
```

进入输出目录，按根 README 配置包源，并在 restore/build/publish 时传入 `-p:GalNetPackageVersion=<version>`。该版本必须已在所选包源中可取得；复制命令不会发布包。文件清单位于 `Template/files.json`，输出 `template-manifest.json` 记录文件来源和哈希。

交付包含共享应用、平台入口、自有契约、Branding、移动 native 源码及必要 MSBuild 附件，不包含引擎 C# 工程、仓库测试和 CI。保留最小目录层级以复用已有相对路径。应用页面和 VM 仍只有一份；标题按钮、命令、新页面、局部 ControlTheme 和配色均在项目中定制。模板不附游戏内容，内容导出必须使用目标应用实际构建的完整契约集合。

## 桌面开发

桌面 CrossPlatform 使用 `UrsaWindow`。Ursa 绘制标题栏装饰，提供最小化、最大化/还原、关闭按钮及系统拖动区域；CrossPlatform 的按钮/标题栏颜色与玩家调色板一致。Headless 不生成原生窗口装饰，桌面交互需在实际窗口验证。

如项目要改用 Avalonia 原生窗口装饰，只需在本项目将 `MainWindow.axaml` 根元素和 code-behind 基类从 `UrsaWindow` 改回 `Window`，并移除 Ursa caption 资源；共享宿主和其他 Sample 不受影响。

在仓库根执行：

```powershell
dotnet run --project src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop -- '导出的内容目录' --profile '独立玩家目录'
```

默认从应用布局定位 `pak`，profile 按内容 projectId 定位至用户目录；换内容缓存版本或移动安装位置不会重置同一项目的进度。显式 `--profile` 用于开发/验证。正式合包使用 Desktop profile 和目标 RID，并继续遵守严格契约字节规则。

## 可选调试

Debug 默认开启 `EnableGameDebugging`，Release 默认关闭，`EnableLaunchArguments` 默认跟随调试开关。Desktop 玩家版拒绝启动参数；开发版接受内容目录、`--profile` 和外部调试邀请。启用调试必须同时启用参数能力。切换开关须重新 restore；普通玩家图使用 `packages.lock.NoGameDebugging.json`，Desktop 开发图使用 `packages.lock.Desktop.<rid>.Debugging.json`。

可选 `GalNet.Avalonia.Debugging` 提供通用菜单、日志、调试存档及外部会话适配，普通宿主没有反向依赖。`SampleCustomization.CreateDebugOptions` 配置项目 replay 策略；App 提供原进程内的 scope 重建和桌面打开日志目录动作。桌面与 Avalonia Sample 一样将菜单挂到 `UrsaWindow.LeftContent`，日志使用独立 Ursa 窗口；不会在游戏内容顶部插入整行菜单覆盖拖动区或窗口按钮。开发版启动前或内容加载失败后仍可查看日志，游戏就绪后才启用清空数据和调试存档。重建 scope 前解绑旧菜单、事件及日志窗口。

移动端使用 `DebugToolsOverlay`，无需额外窗口，且隐藏打开目录入口；系统返回先取消确认/关闭日志。Activity 重建迁移整个容器。关闭调试后排除可选 Avalonia 调试 UI，普通日志继续写入 profile；基础 Engine 包仍包含调试核心程序集，不能据此声称整个调试核心已从包中移除。

移动端调试 UI 的自动化验证不代表 Android/iOS 外部连接、邀请传输或设备验收已经完成。独立复制入口也不提供重命名或自动升级合并功能。

## 移动内容与 Mono AOT

正式发行使用共享编译模式 `GalNetPlayerMode=Developer/Player/Bound`：Developer 可调试并加载外部内容；Player 不可调试但可加载外部内容；Bound 不可调试，也不接受外部路径参数。Bound 从已验证的 `GalNetGamePackage` 提取 `GalNetBoundProjectId` 编译进宿主，并由共享会话在加载内容前校验；不绑定 buildId，因此同一项目可更新内容。其他项目可使用 Editor 的 `Tools/bind-game.ps1` 从自己的包生成绑定配置，详见[发行手册](../../../docs/manual/publishing/engine-release.md)。

`GalNetGamePackage` 指向完整 v4 galpak：Android 打为 `assets/game.galpak`，iOS 打为 bundle 中的 `game.galpak`。平台打开普通资产流，`BundledGameInstaller` 不要求其可寻址，先在沙盒缓存临时文件，再通过正式安装器验证并安装到归档 SHA256 对应的目录。相同 projectId 的不同内容版本使用相同 profile；取消/损坏输入不覆盖已安装内容。

Android 需要 SDK 36、NDK r29（本轮 `29.0.14206865`）及 CMake/Ninja。NDK 和工具路径可显式传入；不再使用禁用移动音频的框架探针作为完整候选。仓库公共 net10.0 依赖仍传入 `GalNetBuildMiniaudio=false`，由 Android 宿主的 `AndroidNativeAudio.targets` 为目标 ABI 构建并打包真正的 `.so`：

```powershell
$android = 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Android/GalNet.Sample.CrossPlatform.Android.csproj'
dotnet publish $android -c Release -p:UseMonoRuntime=true -p:PublishAot=false -p:RunAOTCompilation=true -p:GalNetBuildMiniaudio=false -p:GalNetAndroidNdkDirectory='C:/Android/sdk/ndk/29.0.14206865' -p:GalNetCMakePath='C:/tools/cmake.exe' -p:GalNetNinjaPath='C:/tools/ninja.exe' -p:GalNetGamePackage='C:/path/game.galpak'
```

ARM64 真机发行可给上述命令增加 `-p:RuntimeIdentifier=android-arm64`，避免 APK 同时携带模拟器使用的 x86_64 载荷。Android 工程会把单数 RID 同步到 VLC 包需要的 `RuntimeIdentifiers`；不应靠漏打 VLC、关闭 AOT 或删除游戏内容来缩小体积。包检查脚本支持 `--abi arm64-v8a`，默认仍检查双 ABI；同时报告 APK、解包载荷和每个 ABI 的原生体积。

安卓交互截图由平台注册 `IAutomaticGameScreenshotDestination`，自动命名并写入系统相册的 `Pictures/<游戏名>`，弹窗不显示目录和文件名，预览按当前屏幕约束。Android 10 及以上使用 MediaStore 的 pending/publish 流程，不申请存储权限；Android 6–9 的保存请求包含旧式存储权限处理，拒绝或保存失败时保留错误和重试入口。截图失败会删除本次未完成的 MediaStore 条目。旧系统权限分支和 iOS 相册输出仍需对应设备验收；项目截图 primitive 的沙盒输出策略不受交互截图改动影响。

P7a Release 在 ARM64/x86_64 各完成 115 程序集 Mono AOT，包含真实 GameTestCase 全部内容及两种架构的 miniaudio、LibVLC、C++ 运行库。没有全局压制裁剪/AOT/XA0141 警告。`PublishAot` 与 `RunAOTCompilation` 的 Mono AOT 不同；NativeAOT 不作为前置。Android 的 LibVLC 固定官方 `VideoLAN.LibVLC.Android 3.7.0-beta`（内含 3.0.22-rc1）：此前稳定包 3.6.5 的 native LOAD 只有 4 KB 对齐；当前候选的全部 ELF LOAD 及 APK zipalign 均通过 16 KB 检查。此预发布依赖和设备播放结果继续追踪，不能据此宣布商店发布完成。

Android/iOS 均排除桌面 SDL 资产。`probes/mobile/verify-android-package.py` 位于本 feature 中，只检查完整资产流、两种 ABI、媒体/音频、AOT、ELF 对齐和 SHA256，报告中的 `deviceTested=false` 不代表独立设备报告。2026-10-30 的新候选在 `GalNet.Sample.CrossPlatform.Android/bin/Release/net10.0-android/org.galnet.sample-Signed.apk`，双 ABI 各 117 程序集 Mono AOT；在 REDMI K80 Pro / Android 16 / ARM64 已通过基础启动、剧情视频与立绘合成，用户确认声音/触摸正常。完整移动验收仍待补，证据见 feature 执行记录及 `out/android-device-validation/device.json`。

Android 宿主使用 `FilesDir.CanonicalPath` 取得沙盒根，再执行安装器的严格目录链接检查。不要通过关闭 `RequireNoLinks` 解决平台路径别名问题。内容准备早于 profile 日志初始化，失败时同时向标准错误输出完整异常，便于从 logcat 定位。

Rider 需要安装 JetBrains 的 Android Support 插件才能生成 Android 运行配置；.NET Android workload 本身不会提供 Rider 插件。小米/Redmi 真机除 USB 调试外，还需开启 USB 安装并及时允许手机上的安装确认；`INSTALL_FAILED_USER_RESTRICTED` 应先检查手机安装限制和确认弹窗。

iOS 在装有 Xcode/对应 workload 的 Mac 上构建，传入 `GalNetGamePackage`、`GalNetBuildMiniaudio=false` 和可执行的 `GalNetCMakePath`。`IosNativeAudio.targets` 为 ios-arm64 / iossimulator-arm64 / iossimulator-x64 构建静态音频并 ForceLoad；共享托管适配在 iOS 从主程序解析 native 符号。宿主引用官方 iOS LibVLC 3.6.1，默认使用 Mono 运行时。静态链接、SDK/API、AOT 和签名尚未在 Mac 验证，不能把源代码接线记为成功发布。

## 宿主生命周期与定制

- 平台在启动前设置 `App.PrepareLaunchAsync`，返回内容、profile、截图目录；APK/bundle 路径不会直接传给目录内容加载器。
- `AvaloniaPlayerHost` 由 UI 线程创建、初始化和异步释放；关闭会取消尚在等待语言选择的初始化，随后停止运行、解绑窗口、拆下视图、恢复本地化绑定、释放 scope 和 root。构造失败也执行异步清理。
- Activity 重建重新挂载现有视图。前后台、Android 音频焦点/耳机拔出和 iOS 音频中断/路由事件协调同一会话；iOS 后台检查点申请有限后台执行时间。
- 暂停时停止 Gallery，串行保存稳定剧情检查点、进度与偏好，再释放剧情媒体并回到标题；恢复后由“继续”选择最新有效的检查点或手动存档。检查点位于独立 `profile/resume/saves`，不覆盖玩家槽；开始新游戏、显式读档或清空状态会移除旧检查点。不是任意媒体时间点的无缝恢复，也不依赖进程正常退出。
- 系统返回通过 Avalonia `TopLevel.BackRequested` 进入页面 BackCommand，以执行页面清理；剧情页打开设置。Android/iOS 隐藏程序退出菜单；Android 标题页的系统返回保存后台状态并退到桌面，保留可再次打开的共享会话，不永久 Shutdown App。桌面继续显示退出确认，再次返回取消确认。
- Android 使用 InsetsManager 的 edge-to-edge 与隐藏系统栏设置收起顶部状态栏/底部导航栏；安全区仍由 TopLevel 自动保护刘海/挖孔。16:9 设计画布等比缩放到扩大的区域，长屏侧边留白不裁剪或拉伸消除。iOS 显示与设备行为尚未验证。
- 剧情视频、Gallery 播放与缩略图共享 LibVLC CPU 像素回调，不创建 Gallery 原生子窗口；Windows/Linux 各 5 项真实解码、缩略图和释放用例通过。
- `App.RestartAsync(clearPlayerState: true)` 重建当前应用中的会话，不启动第二个进程；关闭调用 `ShutdownAsync`。这两个入口由平台 UI 线程调用。
- 项目注册/执行在主项目的 `SampleCustomization`；通用宿主为 `AvaloniaPlayerHost`，视图不选择 Sample 主题或文案程序集。通用 UI 文案在 GameHost，项目文案在本项目的 `Localization`；`FromEmbeddedResources` 按顺序合并，内容及外部翻译继续保留原覆盖和文化回退规则。

当前证据与剩余工作见 `features/F-20261029-05-cross-platform-applications/progress-summary.md` 和 `phase-plan.md`。
