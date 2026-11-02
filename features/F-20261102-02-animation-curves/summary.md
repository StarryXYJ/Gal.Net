# 实现总结

`animation.animate` 从 5 种扩展为 32 种曲线：Linear、Step，以及 Quad/Cubic/Quart/Quint/Sine/Expo/Circ/Back/Elastic/Bounce 各三种方向。沿用 IAnimationCurve 和枚举选项生成机制，共享方向变换方法；精确端点，Back/Elastic 保留过冲。按用户要求直接改名旧 Ease 系列为 QuadEase 系列，GameTestCase 两个组中三处引用和既有运行时测试已迁移，没有兼容别名。

编辑器中英日韩四种语言补齐全部选项，包括原先遗漏的 Linear。新增测试覆盖参考值、端点、时间夹取、有限性、方向对称、过冲、非弹跳曲线单调性，以及全部曲线的选项约束、编译与 JSON 往返。未修改播放器调度或 Timeline。

验证：Builtins 428/428、GameTestCase 作者文档验证 1/1、四语言全曲线文本及组 JSON 检查通过；git diff --check 通过。日志见仓库 output/animation-curves/。首次测试代码误将 Schema 当作含 Fields 的对象导致编译失败，已修正为实际字段列表并读取 Value 的 enum 约束，随后全量通过。构建有既有分析器警告。未进行 GUI 手工逐曲线演出验收，不涉及自定义曲线编辑器。

当前契约同步到 [运行时条目规范](../../docs/spec/runtime/entries.md)。已有字体/UI 工作未改动；无架构偏差或新增 agent lesson。
