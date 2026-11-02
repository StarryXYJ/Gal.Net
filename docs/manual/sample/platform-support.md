# 平台支持与验证范围

本页描述当前源码与已有验证记录的范围，不表示每个平台都已有公开发行包。构建成功、原生窗口/媒体运行和最终分发验收是不同层次。

| 平台或能力 | 当前范围与限制 |
| --- | --- |
| Windows x64 桌面 | 当前单一 Sample 的 Player/Developer 发布及契约验证通过；仓库外定制模板在真实窗口完成剧情、存读档/预览/改名、scope 重建与确认退出。历史外部调试记录继续保留，Editor 完整交互和最终公开发行另验 |
| Linux x64 | 当前模板经原生 SDK 发布 Player/Developer，并在 Ubuntu 24.04.3 / WSLg 完成独立进程启动/落盘/确认退出；仓库外包消费者完成真实窗口存读档与 scope 重建，使用经核验的私有 VLC。WSLg 不能替代原生 Linux 桌面或完整发行验收 |
| macOS Intel/ARM | 有 Desktop 工程、锁图和 CI 矩阵定义；实际运行、签名/分发验收尚不能由配置存在推导 |
| Android | 当前模板完成正常 NDK、双 ABI 各 114 Mono AOT、完整内容嵌入和 16 KB ELF 对齐检查；本轮没有连接设备，迁移后的挂载/后台重进/返回/暂停落盘尚未真机验证。旧 UI 的 ARM64 更新及媒体记录不能替代本轮验收；生产签名等专项另验 |
| iOS | 有共享宿主及包内内容接入；项目方向策略由宿主请求系统方向更新。需要 Apple 工具链及平台原生库，方向切换、实机/签名/AOT 验收另行进行 |
| Browser | 保留模板工程；桌面媒体、文件系统与动态契约加载不能直接用于浏览器，不作为完整 Web 玩家承诺 |
| 桌面手柄 | SDL3 后端已有 Windows/WSLg 自动化与虚拟设备验证；真实手柄及其他平台验收仍需完成，缺库时应保留键鼠能力 |
| NativeAOT | Headless 引擎闭包已有验证；不能据此声称完整 Avalonia 玩家、视频后端或 iOS 已完成 NativeAOT |

构建跨平台应用先阅读 [CrossPlatform 工程说明](../../../src/Samples/GalNet.Sample.CrossPlatform/README.md)和[后端接入](../extensions/media-and-platform-backends.md)。平台项目拥有可写目录、生命周期和原生部署策略；跨平台共享 UI 不承担所有系统适配。

默认 Avalonia Sample 从项目 `galnet.project` 配置读取方向策略。桌面按项目方向调整窗口初始比例；Android 和 iOS 由原生宿主应用方向请求。`Unforced` 时只以默认方向作启动偏好，玩家可按设备能力旋转。玩家偏好中没有项目方向设置。当前源代码已接线，设备方向行为仍需相应平台实机验证。

维护本页时应以对应构建、实跑或验收证据为准。历史证据及剩余矩阵见[跨平台 feature](../../../features/F-20261029-05-cross-platform-applications/progress-summary.md)，不能把旧候选自动当作包含最新源码变更。

当前应用 UI 归属调整及各层证据见[单项目模板总结](../../../features/F-20261031-02-sample-ui-template/summary.md)。
