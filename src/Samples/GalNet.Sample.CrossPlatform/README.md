# GalNet cross-platform sample

当前 CrossPlatform 已接入共享玩家，不再是欢迎页骨架。原 `GalNet.Sample.Avalonia` 保留桌面启动和开发工具；两种宿主都引用 `../GalNet.Sample.Player/` 的主视图、语言选择、本地化、截图实现与公共执行组合。

| 工程 | 职责与当前验证 |
| --- | --- |
| `GalNet.Sample.Player` | 共享源码库；不包含桌面入口或调试窗口 |
| CrossPlatform 共享 App | 会话组合、Activity 视图重挂载、scope 重建和关闭 |
| Desktop | Windows/Linux/macOS 入口；Windows/WSLg 启动、会话和正常退出已验证，macOS 验收延期 |
| Android | Asset 流安装和沙盒目录；Debug APK、ARM64/x86_64 Release Mono AOT 发布已验证，设备/完整媒体待验 |
| iOS | Bundle 流安装和沙盒目录入口；代码未构建/运行，验收延期 |
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

Android 构建示例：

```powershell
$android = 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Android/GalNet.Sample.CrossPlatform.Android.csproj'
dotnet build $android -c Debug -p:GalNetBuildMiniaudio=false -p:GalNetGamePackage='C:/path/game.galpak'
dotnet publish $android -c Release -p:UseMonoRuntime=true -p:PublishAot=false -p:RunAOTCompilation=true -p:GalNetBuildMiniaudio=false -p:GalNetGamePackage='C:/path/game.galpak'
```

本轮 Release 已实际完成 ARM64 和 x86_64 各 116 个程序集的 Mono AOT，没有全局吞掉链接/AOT 警告。`PublishAot` 是 NativeAOT 开关，与 `RunAOTCompilation` 的 Mono AOT 不同；当前不强制 NativeAOT。生成的是本地签名候选，生产签名和设备验收另行完成。

上面的 `GalNetBuildMiniaudio=false` 用于当前应用框架/发布验证，避免 Windows 宿主自动构建桌面音频库；完整移动播放仍需 P7 的 Android ABI 音频、VLC/视频/Gallery 部署和生命周期接入。Android/iOS 已显式排除桌面 SDL 原生资产，不能把 glibc Linux `.so` 当作 Android 库。APK 生成/AOT 编译成功不等于完整可游玩。

iOS 在有 Xcode 的 macOS 上使用对应 workload，并传入同样的 `GalNetGamePackage`；默认保留 Mono 运行时，静态音频/VLC/链接保留和签名后续验证。Windows 本轮没有构建 iOS。

## 宿主生命周期与定制

- 平台在启动前设置 `App.PrepareLaunchAsync`，返回内容、profile、截图目录；APK/bundle 路径不会直接传给目录内容加载器。
- `SamplePlayerSession` 由 UI 线程创建、初始化和异步释放；关闭会取消尚在等待语言选择的初始化，随后停止运行、解绑窗口、拆下视图、释放 scope 和 root。构造失败也执行异步清理。
- Activity 重建重新挂载现有视图，保留当前会话；系统音频焦点、中断、后台持久化和完整恢复仍由后续平台适配接入。
- `App.RestartAsync(clearPlayerState: true)` 重建当前应用中的会话，不启动第二个进程；关闭调用 `ShutdownAsync`。这两个入口由平台 UI 线程调用。
- 公共注册/执行和资源在 `SamplePlayerServices`，项目可在其基础上定制；原桌面的 `SampleCustomization` 只组合桌面特有服务。嵌入式 UI 文本属于 `typeof(SamplePlayerServices).Assembly`，不再属于旧 Sample App 程序集。

当前证据与剩余工作见 `features/F-20261029-05-cross-platform-applications/progress-summary.md` 和 `phase-plan.md`。
