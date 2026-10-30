# 环境与依赖

先选择使用已有发行包，或从源码运行。发行包的可用版本以项目 Release 为准；当前源码中的新功能不一定已进入旧发行包。

| 路径 | 前提 |
| --- | --- |
| 自包含桌面发行包 | 对应操作系统与架构，完整解压并保留 lib、docs 等目录 |
| framework-dependent Windows 发行包 | 安装 .NET 10 Runtime 的最新修补版本；无需 SDK |
| 从源码构建 | `global.json` 指定的 .NET SDK 10.0.401；rollForward 被关闭 |
| Windows 原生音频源码构建 | Visual Studio C++ Build Tools、Windows SDK 及目标架构工具链 |
| Linux/macOS 原生适配 | 对应平台的原生库与工具链；参阅后端手册 |
| Android/iOS/Browser 工程 | 对应 .NET workload、平台 SDK 及原生依赖；不能由桌面成功推导实机支持 |

从仓库根目录检查 SDK：

```powershell
dotnet --version
```

应显示 `10.0.401`。首次还原需要能访问 NuGet；Avalonia BuildServices 需要可写用户缓存。完整 solution 含移动与浏览器宿主，日常桌面验证优先使用共享 CI 入口生成的桌面过滤器，或直接构建需要的项目。

继续阅读：[运行 Sample](run-sample.md)、[源码构建](build-from-source.md)、[平台支持与验证范围](../manual/sample/platform-support.md)、[原生后端接入](../manual/extensions/media-and-platform-backends.md)。
