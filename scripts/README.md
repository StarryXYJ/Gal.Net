# Scripts

仓库脚本按用途分类：

- `contracts/`：构建、部署和复现性检查项目 Contracts。
- `samples/`：运行 Headless 与 Avalonia Sample。
- `performance/`：启动 benchmark、trace 分析和 native profile。
- `maintenance/`：仓库代码量统计等维护工具。
- `ci/`：本地与 Actions 共用的 restore、format、build、test 入口。
- `release/`：五个聚合 NuGet、Editor/Sample/Contracts ZIP、独立消费和分发验收、产物索引与单独上传。

本地 CI / Release 用法见 [`docs/manual/release-packaging.md`](../docs/manual/release-packaging.md)。先运行 `ci/Invoke-CI.ps1 -Coverage`，再运行 `release/Release.ps1 -Version 0.1.0-dev.1`。GitHub Release 由 `vMAJOR.MINOR.PATCH` 或 `vMAJOR.MINOR.PATCH-preview.N` tag 触发；手动 workflow_dispatch 仅构建候选。桌面 AOT 尚未通过验收，`Test-DesktopAot.ps1` 用于严格诊断。
