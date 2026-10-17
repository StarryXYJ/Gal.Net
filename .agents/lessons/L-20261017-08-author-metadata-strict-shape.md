# 作者元数据扩展要同步严格 Raw shape

状态：confirmed
日期：2026-10-17
来源：F-20261015-02-editor-authoring-workspace / P5c

现象：ProjectVariableDefinition 新增 description 后，面板与内存历史可编辑，但保存测试 3/4：Graph/saveVariables/0/description 报 json.fieldUnknown。新增 CLR/JsonPropertyName 和 Clone 不代表作者写入协议已经接通。

根因：ProjectValidationService 独立严格检查 Raw Graph 声明的字段白名单；作者保存先经过此规则，而非只执行类型化序列化。正确做法是在唯一声明 shape 明确允许新元数据并验证其类型，同时同步中立模型克隆、正式格式说明和保存重开测试。不能绕过保存校验、宽泛允许未知字段或在保存时删掉新字段。

证据：p5c-focused.trx 的 description 失败；修正后 p5c-focused-fixed/final.trx 与完整 611 项回归通过。VariableDescriptionIsOptionalTextAndDoesNotRelaxUnknownFields 覆盖字符串/数字/null，变量面板测试覆盖说明及 UID 保存重开。适用范围：给作者 DTO/Graph/Group/settings 增加由 Editor 写出的正式字段时。
