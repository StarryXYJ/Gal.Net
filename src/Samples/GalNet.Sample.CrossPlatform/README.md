# GalNet cross-platform sample

当前 CrossPlatform 已接入共享玩家，不再是欢迎页骨架。原 `GalNet.Sample.Avalonia` 保留桌面启动和开发工具；两种宿主都引用 `../GalNet.Sample.Player/` 的主视图、语言选择、本地化、截图实现与公共执行组合。

| 工程 | 职责与当前验证 |
| --- | --- |
| `GalNet.Sample.Player` | 共享源码库；不包含桌面入口或调试窗口 |
| CrossPlatform 共享 App | 会话组合、Activity 视图重挂载、scope 重建和关闭 |
| Desktop | Windows/Linux/macOS 入口；Windows/WSLg 启动、会话和正常退出已验证，macOS 验收延期 |
| Android | ARM64 真机已安装启动，语言页、剧情视频叠加立绘、声音与触摸推进通过基础 smoke；存读档/Gallery/生命周期完整验收待补 |
| iOS | Bundle 内容、静态音频/VLC、AVAudioSession 与生命周期已接入；尚未在 Mac 构建/运行 |
| Browser | 保留原工程，不在本 feature 支持范围内 |

SDK 固定 `10.0.401`，Avalonia `12.0.5`；子解决方案导入根目录的中央包版本，避免复制一份依赖版本表。当前工程仍依赖仓库中的共享库/契约工程；可独立生成的模板是剩余工作，不能直接把这个子目录复制出去当作完整模板。

## 桌面开发

在仓库根执行：

```powershell
dotnet run --project src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop -- '导出的内容目录' --profile '独立玩家目录'
```

默认从应用布局定位 `pak`，profile 按内容 projectId 定位至用户目录；换内容缓存版本或移动安装位置不会重置同一项目的进度。显式 `--profile` 用于开发/验证。正式合包使用 Desktop profile 和目标 RID，并继续遵守严格契约字节规则。

## 移动内容与 Mono AOT

`GalNetGamePackage` 指向完整 v4 galpak：Android 打为 `assets/game.galpak`，iOS 打为 bundle 中的 `game.galpak`。平台打开普通资产流，`BundledGameInstaller` 不要求其可寻址，先在沙盒缓存临时文件，再通过正式安装器验证并安装到归档 SHA256 对应的目录。相同 projectId 的不同内容版本使用相同 profile；取消/损坏输入不覆盖已安装内容。

Android 需要 SDK 36、NDK r29（本轮 `29.0.14206865`）及 CMake/Ninja。NDK 和工具路径可显式传入；不再使用禁用移动音频的框架探针作为完整候选。仓库公共 net10.0 依赖仍传入 `GalNetBuildMiniaudio=false`，由 Android 宿主的 `AndroidNativeAudio.targets` 为目标 ABI 构建并打包真正的 `.so`：

```powershell
$android = 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Android/GalNet.Sample.CrossPlatform.Android.csproj'
dotnet publish $android -c Release -p:UseMonoRuntime=true -p:PublishAot=false -p:RunAOTCompilation=true -p:GalNetBuildMiniaudio=false -p:GalNetAndroidNdkDirectory='C:/Android/sdk/ndk/29.0.14206865' -p:GalNetCMakePath='C:/tools/cmake.exe' -p:GalNetNinjaPath='C:/tools/ninja.exe' -p:GalNetGamePackage='C:/path/game.galpak'
```

P7a Release 在 ARM64/x86_64 各完成 115 程序集 Mono AOT，包含真实 GameTestCase 全部内容及两种架构的 miniaudio、LibVLC、C++ 运行库。没有全局压制裁剪/AOT/XA0141 警告。`PublishAot` 与 `RunAOTCompilation` 的 Mono AOT 不同；NativeAOT 不作为前置。Android 的 LibVLC 固定官方 `VideoLAN.LibVLC.Android 3.7.0-beta`（内含 3.0.22-rc1）：此前稳定包 3.6.5 的 native LOAD 只有 4 KB 对齐；当前候选的全部 ELF LOAD 及 APK zipalign 均通过 16 KB 检查。此预发布依赖和设备播放结果继续追踪，不能据此宣布商店发布完成。

Android/iOS 均排除桌面 SDL 资产。`probes/mobile/verify-android-package.py` 位于本 feature 中，只检查完整资产流、两种 ABI、媒体/音频、AOT、ELF 对齐和 SHA256，报告中的 `deviceTested=false` 不代表独立设备报告。2026-10-30 的新候选在 `GalNet.Sample.CrossPlatform.Android/bin/Release/net10.0-android/org.galnet.sample-Signed.apk`，双 ABI 各 117 程序集 Mono AOT；在 REDMI K80 Pro / Android 16 / ARM64 已通过基础启动、剧情视频与立绘合成，用户确认声音/触摸正常。完整移动验收仍待补，证据见 feature 执行记录及 `out/android-device-validation/device.json`。

Android 宿主使用 `FilesDir.CanonicalPath` 取得沙盒根，再执行安装器的严格目录链接检查。不要通过关闭 `RequireNoLinks` 解决平台路径别名问题。内容准备早于 profile 日志初始化，失败时同时向标准错误输出完整异常，便于从 logcat 定位。

Rider 需要安装 JetBrains 的 Android Support 插件才能生成 Android 运行配置；.NET Android workload 本身不会提供 Rider 插件。小米/Redmi 真机除 USB 调试外，还需开启 USB 安装并及时允许手机上的安装确认；`INSTALL_FAILED_USER_RESTRICTED` 应先检查手机安装限制和确认弹窗。

iOS 在装有 Xcode/对应 workload 的 Mac 上构建，传入 `GalNetGamePackage`、`GalNetBuildMiniaudio=false` 和可执行的 `GalNetCMakePath`。`IosNativeAudio.targets` 为 ios-arm64 / iossimulator-arm64 / iossimulator-x64 构建静态音频并 ForceLoad；共享托管适配在 iOS 从主程序解析 native 符号。宿主引用官方 iOS LibVLC 3.6.1，默认使用 Mono 运行时。静态链接、SDK/API、AOT 和签名尚未在 Mac 验证，不能把源代码接线记为成功发布。

## 宿主生命周期与定制

- 平台在启动前设置 `App.PrepareLaunchAsync`，返回内容、profile、截图目录；APK/bundle 路径不会直接传给目录内容加载器。
- `SamplePlayerSession` 由 UI 线程创建、初始化和异步释放；关闭会取消尚在等待语言选择的初始化，随后停止运行、解绑窗口、拆下视图、释放 scope 和 root。构造失败也执行异步清理。
- Activity 重建重新挂载现有视图。前后台、Android 音频焦点/耳机拔出和 iOS 音频中断/路由事件协调同一会话；iOS 后台检查点申请有限后台执行时间。
- 暂停时停止 Gallery，串行保存稳定剧情检查点、进度与偏好，再释放剧情媒体并回到标题；恢复后由“继续”选择最新有效的检查点或手动存档。检查点位于独立 `profile/resume/saves`，不覆盖玩家槽；开始新游戏、显式读档或清空状态会移除旧检查点。不是任意媒体时间点的无缝恢复，也不依赖进程正常退出。
- 系统返回通过 Avalonia `TopLevel.BackRequested` 进入页面 BackCommand，以执行页面清理；剧情页打开设置，标题显示退出确认，再次返回取消确认。安全区由 TopLevel 自动应用；没有窗口时桌面显示设置不可用，iOS 隐藏程序退出菜单。
- 剧情视频、Gallery 三种主题及缩略图共享 LibVLC CPU 像素回调，不创建 Gallery 原生子窗口；Windows/Linux 各 5 项真实解码、缩略图和释放用例通过。
- `App.RestartAsync(clearPlayerState: true)` 重建当前应用中的会话，不启动第二个进程；关闭调用 `ShutdownAsync`。这两个入口由平台 UI 线程调用。
- 公共注册/执行和资源在 `SamplePlayerServices`，项目可在其基础上定制；原桌面的 `SampleCustomization` 只组合桌面特有服务。嵌入式 UI 文本属于 `typeof(SamplePlayerServices).Assembly`，不再属于旧 Sample App 程序集。

当前证据与剩余工作见 `features/F-20261029-05-cross-platform-applications/progress-summary.md` 和 `phase-plan.md`。
