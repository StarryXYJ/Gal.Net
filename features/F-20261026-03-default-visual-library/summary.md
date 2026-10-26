# 阶段交付记录

整体状态：进行中，尚未完成。

## P1

- 新建默认契约项目获得 8 个可编辑 SkSL 资产及自身 metadata；运行不依赖编辑器模板。
- `defaultVisualPrograms` 保存项目用途绑定；准备阶段保留作者已有值，不改变传入配置。
- dry-run 报告包含将要部署的 shader/metadata；自定义契约和无契约 scaffold 维持显式能力边界。
- 9 项初始化/CLI 测试及 8 项真实 shader 编译测试通过。
- DepthBlur / MaskReveal 目前仅有可编译资源，实际 Z 注入与外部纹理绑定在 P2；预设与创作入口在 P3；展示/文档在 P4。
