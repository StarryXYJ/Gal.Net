# 作者参数测试必须按 schema 的线格式编码

状态：confirmed
日期：2026-10-21
来源：F-20261015-02-editor-authoring-workspace / P6e4c4
关联：[阶段记录](../../features/F-20261015-02-editor-authoring-workspace/phase-6e4.md)

现象：播放 Locator 首轮 99 项分析专项中 16 项失败，Animate 候选为空。测试 helper 用原生 JSON bool 构造顶层 blocking；AnimationOptions 的 StringBooleanValueConverter 按契约拒绝它，分析器捕获异常并退回保守状态。嵌套 TimelineDefinition 则确实使用 JSON bool，两者不能混用。

根因与发现：检查 AnimationOptions 的字段属性及 GalNet.Core.Parameters.StringValueConverters 后确认。CLR 属性 bool 不表示作者 wire value 必为 JSON bool；Field schema 及编码 converter 才决定序列化格式。首次失败证据保留于 timeline-validation/playback-c4-analysis-first.trx 和同名前缀 log。

正确做法：构造作者 Entry.Parameters 时使用该字段的 schema/编码；Animate 的 blocking/skippable 使用字符串 true/false，Timeline 根内相应字段使用 JSON bool。修正 helper 后保留原生 bool 输入的拒绝用例，并为所有有效夹具显式断言 Complete，避免“候选为空”偶然满足排除条件。

适用：手工 JSON fixture、DTO 字段到作者参数的投影、跨嵌套契约测试。禁止为了使错误夹具通过而拓宽生产 converter，或仅根据 CLR 类型猜测 JSON 线格式。
