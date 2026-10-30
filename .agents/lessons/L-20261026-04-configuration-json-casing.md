# schema 检查与 DTO 解码必须使用同一字段身份规则

状态：confirmed
来源：F-20261026-01 引擎核心重构 R4b

现象：section 按字段 ID 精确校验 count，但 JsonSerializerDefaults.Web 默认不区分大小写。输入 {"count":4,"Count":0} 能先通过 count 的最小值校验，再在 DTO 解码时让 Count=0 覆盖已验证值；未知字段也因此变成已知字段的写入入口。

根因：schema 查询使用 Ordinal 精确匹配，DTO 解码却使用 Web 的 PropertyNameCaseInsensitive=true，两个阶段对字段身份的定义不一致。

正确做法：配置 DTO 的 JSON 解码显式使用 PropertyNameCaseInsensitive=false；原始未声明字段继续保留，不解释成另一个已知字段。领域成员的 descriptor JSON options 与 DTO JSON options 也采用相同规则。

验证：ProjectConfigurationTests.UnknownFieldCasingDoesNotBypassDeclaredFieldValidation 覆盖 count/Count 共存、类型化修改后已知值仍有效、未知字段原样保留；r4b-final-tests.log 的 Core 166 项通过。

适用范围：在按精确字段 ID 校验之后反序列化为 CLR DTO 的配置、命令或内容边界。不要通过大小写不敏感解码绕过 schema，也不要为修复字段身份问题直接删除不认识的作者数据。

关联：docs/spec/content/file-formats.md；features/F-20261026-01-engine-core-simplification/summary.md。
