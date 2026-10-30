# 源码发布使用匹配 profile 的显式锁定还原

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications
- 日期：2026-10-29

真实 Sample 的 publish 隐式还原在 WindowsReadyToRun 下对 SchemaGenerator 报 NU1004（项目 RID 为空，已提交锁图含 win-x64）。独立显式 restore 后 publish --no-restore 成功，不能因此关闭 RestoreLockedMode。

还原参数还须区分 RuntimeIdentifier 与 RuntimeIdentifiers：dotnet restore -r 会引入复数 RID，传到原本可移植的分析器，导致 Desktop/linux-x64 的空 RID 锁图被拒绝；只传单数 -p:RuntimeIdentifier 可匹配该图，也满足 Desktop 的应用 RID 检查。现有 WindowsReadyToRun 锁图则继续沿用 Publish.ps1 的 -r 形式。不要将某个 profile 的成功参数机械用于另一张锁图。

BuildComposeService 显式 SDK -> restore -> publish --no-restore，三步同配置/版本/目标属性、分别保留退出码与日志。WindowsReadyToRun 仅接受 win-x64；Desktop 用全局 GalNetDesktopPublishTarget。配置变化后必须实际还原并验证，不能借用缓存 assets 掩盖问题。

证据：out/cross-platform-validation/20261029-build-compose/actual-windows-before-restore-fix.json、actual-linux-before-rid-fix.json、actual-linux-before-plural-rid-fix.json 及对应 stdout；Windows 真实源码包已完成启动/正常退出。适用当前 SDK 10.0.401 和仓库已提交的 profile 锁图，其他项目应以自己的图为准。
