# 显式字符串 codec 要导出字符串 schema

状态：confirmed；来源：F-20261013-02 Phase 5c。

现象：animation.animate 的 CLI 对照发现 property 的 allOf[0] 从 string/null 类型变成 true，其他字段与 options 不变。

根因：System.Text.Json exporter 对 JsonConverter<string> 当作不透明 converter，不会自动导出字符串类型；IStringValueEncoding 已承诺字符串 wire 格式，仍交给该 converter 导出会丢失类型事实。

正确做法：仅对显式 IStringValueEncoding 且处理 string 的 converter 使用标准 string schema，约束仍来自 ValueDescriptor，读取仍使用原 codec。普通自定义 converter 的描述继续沿用原配置，不猜测 wire 格式。

验证：Core 新增显式字符串 codec 的类型/选项/实际读取与非法数字回归；12 条复杂 Primitive CLI schema 与原基线逐项对照。适用：字符串选项来自领域属性表等自定义 codec。
