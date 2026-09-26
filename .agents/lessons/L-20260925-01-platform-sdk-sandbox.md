# 全解决方案构建会因平台 SDK 枚举越出受限工作区而失败

状态：candidate  
来源 Feature：F-20260922-01-entry-instance-runtime

## 现象

在受限 workspace 中执行 `dotnet build GalNet.slnx` 时，普通 .NET、Editor、Avalonia Desktop、Headless 和测试项目已经成功编译，但 Android、Browser 和 iOS launcher 项目同时报 `MSB4184`。错误发生在 `ToolLocationHelper.GetPlatformSDKLocation`，并显示无权访问 `C:\Users\Starry\AppData\Local\Microsoft SDKs`。

## 根因

这些平台项目的 MSBuild SDK 探测会枚举用户目录中的 Microsoft SDK 安装位置。该目录不属于当前 workspace 的可读范围，因此失败发生在平台 SDK 定位阶段，与正在实现的业务代码和项目间引用无关。

## 如何发现

同一次 solution build 中，Gallery 影响的 Core、Storage、Editor、Desktop Sample、Headless Sample 和测试项目均输出成功 DLL；只有 Android、Browser 和 iOS 项目在相同的 SDK 路径枚举处失败。随后定向 `GeneralTest` 构建和 228 项测试全部通过。

## 正确做法

先阅读完整 solution build 输出，确认失败是否仅限平台 SDK 探测。业务变更验证应继续构建受影响的普通 .NET、Desktop、Headless 和测试项目，并把未验证的平台 target 与精确权限原因记录在 feature 证据中。若必须验证这些平台 target，应申请允许读取对应 SDK 目录的环境，而不是修改业务代码绕过 SDK 检测。

## 适用范围

包含 Android、Browser、iOS 等 workload 项目的 GalNet 全解决方案构建，尤其是文件系统受限的 Codex workspace。

## 不要做什么

不要把 `GetPlatformSDKLocation` 的目录权限错误误判为 Gallery、Core 或项目引用编译回归；也不要删除平台项目、改 target framework 或伪造 SDK 路径来让 solution build 表面成功。

## 证据和关联文档

- `features/F-20260922-01-entry-instance-runtime/phase-plan.md`
- 失败命令：`dotnet build GalNet.slnx --no-restore -m:1 -v:minimal -p:UseSharedCompilation=false -p:UsedAvaloniaProducts=`
