# Tag 驱动的 NuGet 与 Windows Release

- 状态：实现中；本地 CI、聚合打包和 R2R 发布正在验收，尚未公开发布。
- 日期：2026-10-27。
- 需求：本地先运行完整发布流水线，后续 CI 在版本 tag 上调用同一套仓库脚本，生成 NuGet 和 GitHub Release。
- 已确认：Editor 不做 AOT；仅 Sample 提供 R2R / NativeAOT 两种模式。Release 包含纯 Sample、整合 GameTestCase 的 Sample、Editor 和 Sample Contracts DLL。
- 已确认：NuGet 将多个项目合入少量包，不采用一项目一包。Editor 不提供 SDK，修改时克隆源码。
- 当前提案：Contracts、Engine、Desktop、Avalonia、SchemaGenerator 共 5 个包，保留现有程序集边界；分开契约、运行逻辑、默认后端与图形展示。
- 设计假设：第一批可执行发行包只支持 Windows x64；Windows ARM64 在桌面依赖和实机验收后再增加。
- [设计方案](design.md)。本文与设计均是目标状态，不替代当前正式发布文档。
- [阶段与验收](phase-plan.md)、[CI 调查与 AOT 限制](ci-investigation.md)。当前可运行命令见 docs/manual/release-packaging.md。
