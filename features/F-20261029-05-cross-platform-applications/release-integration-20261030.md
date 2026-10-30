# 公开发行链路与可复用玩家编译模式

用户确认的最终范围：Windows/Linux Editor 各自包含/框架依赖；CrossPlatform Sample 每平台 Developer（调试、外部）、Player（非调试、外部）、Bound（非调试、内置 GameTestCase）各两种部署；Android 只发布 Bound ARM64 Mono AOT。macOS/iOS 延期。旧 Avalonia Sample 不进入本轮公开发行矩阵。

## 实现与退出条件

- 共享 `GalNetPlayerMode` props/targets、编译元数据和 `GameLaunchPolicy`；其他 NuGet 消费者可使用同一模式与自己的游戏包。Bound 的项目身份来自已验证导出，不写死 Sample 身份；项目相同的内容更新允许，其他项目拒绝。
- Editor 随附 `bind-game.ps1`，校验自己的包并生成导入属性及 pak 内容；正式打包严格核对游戏与目标 Sample 契约闭包。
- `release-build.yml` 并行构建 NuGet/两个桌面平台/Android；桌面两部署复用 native 音频与 Linux 媒体准备；最终完整矩阵封存并由原 Push journal 上传。
- schemaVersion 2 发布门禁检查全部 19 个应用/契约附件、版本、来源提交和哈希；旧本地 Windows 工具仍能使用 schemaVersion 1。
- CI 纯文档修改跳过完整 BuildTest；已压缩 artifact 不再二次压缩。普通 CI 仍负责测试；Release 只构建、校验产物与消费者。

## 验证记录

- 已复现并拦截：Editor 默认项目契约与 CrossPlatform Sample 同名契约不同；改为从实际目标 Sample 载荷导出，保留所有契约 hash 验证。暂存目录保留真实项目名，避免 CLI 以通用暂存名称改变 projectId。
- 运行时策略新增 3 项测试通过：Developer/Player 外部能力、Bound 拒绝其他项目、允许本项目新 build。
- 发布集合 Python 测试 5 项通过；原始应用保护测试 8 项通过；真实临时 Git 图/模拟上传门禁 19 项及发布发现/已发布跳过用例 19 项通过。
- 修正后 Windows FrameworkDependent 三模式原始发布、四份完整归档及 bind-game 工具通过；NuGet 六包和五个仓库外消费者通过；文件系统完整回归 37 项通过。
- 三种模式的独立可执行消费者编译/运行通过；缺失绑定、非法模式、矛盾开关三种负例被构建拒绝。
- PR #28 的 Android runner 未预装 sdkmanager；增加显式 Android SDK/NDK/CMake 安装，并将 workload 固定到本地已验证的 10.0.401.1 集合（Android 36.1.69、Mono 10.0.12），避免 runner 默认清单选择其他运行时版本。最终远端检查与公开 Release 继续验收。

## 边界

Linux 媒体沿用 Ubuntu 24.04 私有闭包、包来源/hash/版权清单及 native 依赖检查；不将 WSLg 等同于原生 Linux 桌面完整验收。Android 使用现有开发签名发布预览 APK，不宣称已建立长期生产签名管理。Bound 不是防篡改方案。
