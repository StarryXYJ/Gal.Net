# Scripts

仓库脚本按用途分类：

- `contracts/`：构建、部署和复现性检查项目 Contracts。
- `samples/`：运行 Headless 与 Avalonia Sample。
- `performance/`：启动 benchmark、trace 分析和 native profile。
- `maintenance/`：仓库代码量统计等维护工具。
- `docs/`：正式文档导航、链接校验、完整 Wiki 生成与回归测试。
- `ci/`：本地与 Actions 共用的 restore、format、build、test 入口。
- `release/`：五个聚合 NuGet、Editor/Sample/Contracts ZIP、独立消费和分发验收、产物索引与单独上传。

本地构建与测试见[开发验证](../docs/manual/development/build-and-test.md)，Release 见[引擎发布](../docs/manual/publishing/engine-release.md)，文档整理与 Wiki 生成见[文档维护](../docs/manual/development/documentation.md)。GitHub Release 由正式版本 tag 与主分支归属共同控制，手动 workflow_dispatch 仅构建候选。桌面 AOT 尚未通过完整验收，`Test-DesktopAot.ps1` 用于严格诊断。
