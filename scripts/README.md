# Scripts

仓库脚本按用途分类：

- `contracts/`：构建、部署和复现性检查项目 Contracts。
- `samples/`：运行 Headless 与 Avalonia Sample。
- `performance/`：启动 benchmark、trace 分析和 native profile。
- `maintenance/`：仓库代码量统计等维护工具。
- `release/`：发布 Editor、Sample、整合 GameTestCase 的 ZIP，以及安装 `.galpak` 到整合包的流程。

本地 Release 用法见 [`docs/manual/release-packaging.md`](../docs/manual/release-packaging.md)。CI 的 GitHub Release 由 `vMAJOR.MINOR.PATCH` 或 `vMAJOR.MINOR.PATCH-preview.N` tag 触发。
