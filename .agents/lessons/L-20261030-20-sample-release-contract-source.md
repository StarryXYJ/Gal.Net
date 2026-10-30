# L-20261030-20-sample-release-contract-source

- 状态：confirmed
- 来源：features/F-20261029-05-cross-platform-applications/release-integration-20261030.md

## 现象与根因

新发行矩阵的带游戏包在严格契约校验处失败。Editor 的 DefaultContracts/GalNet.Project.Contracts.dll 来自 GalNet.Editor.DefaultContracts；CrossPlatform Sample 的同名程序集来自 GalNet.Sample.Contracts。Core/Builtins 一致不代表项目契约字节也一致。

## 正确做法

导出游戏时使用目标 Sample 实际发布目录中的三份契约，安装后核对内容描述中的每份契约 hash 与最终 Bound 载荷。禁止用 Editor 默认项目契约替代目标 Sample 契约，也不能放宽 hash 校验。暂存源目录保留真实项目名：CLI 目前以源目录名作为导出 projectId，通用 game-source 名称会改变绑定身份。

## 验证

本地 Windows SelfContained/FrameworkDependent 原始发布均复现了默认项目契约与 Sample 项目契约 hash 不同；打包门禁拒绝该组合。修正来源后继续执行同一门禁，不绕过验证。适用于跨平台 Sample/其他宿主带内容发行。

补充：模式常量只能作用于玩家宿主/显式 GalNetPlayerAssembly，不能通过根 targets 改变 Core 等严格契约库的编译常量；后者会使 CLI 和 Sample 的契约 hash 不同。限制作用域后，Windows FrameworkDependent 全部模式、导出/安装/归档与契约门禁通过。
