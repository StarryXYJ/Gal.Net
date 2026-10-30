# 平台支持与验证范围

本页描述当前源码与已有验证记录的范围，不表示每个平台都已有公开发行包。构建成功、原生窗口/媒体运行和最终分发验收是不同层次。

| 平台或能力 | 当前范围与限制 |
| --- | --- |
| Windows x64 桌面 | 主要开发/普通 CI 环境；已有 Editor、Sample、原生音视频及退出流程验证记录 |
| Linux x64 | 已有 WSLg 原生 SDK 构建、严格合包及 Sample 会话/退出验证；完整媒体、最低发行基线和原生桌面仍须按对应候选验收 |
| macOS Intel/ARM | 有 Desktop 工程、锁图和 CI 矩阵定义；实际运行、签名/分发验收尚不能由配置存在推导 |
| Android | 已接入共享玩家、私有目录和包内 galpak；需要目标音频库、workload 与 SDK。APK 构建/native 检查不替代设备生命周期、媒体与完整游戏验收 |
| iOS | 有共享宿主及包内内容接入；需要 Apple 工具链及平台原生库，实机/签名/AOT 验收另行进行 |
| Browser | 保留模板工程；桌面媒体、文件系统与动态契约加载不能直接用于浏览器，不作为完整 Web 玩家承诺 |
| 桌面手柄 | SDL3 后端已有 Windows/WSLg 自动化与虚拟设备验证；真实手柄及其他平台验收仍需完成，缺库时应保留键鼠能力 |
| NativeAOT | Headless 引擎闭包已有验证；不能据此声称完整 Avalonia 玩家、视频后端或 iOS 已完成 NativeAOT |

构建跨平台应用先阅读 [CrossPlatform 工程说明](../../../src/Samples/GalNet.Sample.CrossPlatform/README.md)和[后端接入](../extensions/media-and-platform-backends.md)。平台项目拥有可写目录、生命周期和原生部署策略；跨平台共享 UI 不承担所有系统适配。

维护本页时应以对应构建、实跑或验收证据为准。历史证据及剩余矩阵见[跨平台 feature](../../../features/F-20261029-05-cross-platform-applications/progress-summary.md)，不能把旧候选自动当作包含最新源码变更。
