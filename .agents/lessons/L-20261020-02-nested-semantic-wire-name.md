# 嵌套语义引用使用实际 JSON wire 名

状态：confirmed
日期：2026-10-20
来源：F-20261019-01-editor-game-localization / P3

## 现象与根因

FieldDescriptor.Id 是 DTO schema 的规范名称（生成器通常采用 camelCase），不保证等于嵌套 DTO 在作者 JSON 中的属性名。ValueDescriptor 的 JsonOptions 默认保持 PascalCase，也可能指定命名策略或 JsonPropertyName。仅以 field.Id 访问 JSON 会漏掉实际引用并误报未知字段。

## 修正与证据

LocalizationReferenceIndex 按 Field 标记关联 CLR member，再从该值契约的 JsonTypeInfo 取得实际 wire 名；纯手写 schema 没有 CLR member 时保留 field.Id fallback。导航 JSON Pointer 使用实际 wire 名，并按 JSON Pointer 规则转义。

LocalizationReferenceIndexTests.NestedRoleFieldsUseActualSerializerWireNames 分别验证默认 PascalCase 与显式 camelCase，两项通过；CharacterNestedListFieldUsesSemanticEditorAndRenameUpdatesStoredWirePath 真实挂载列表内 speaker 输入框，验证创建和角色改 key 更新 /items/0/Speaker，撤销恢复。

## 适用范围

本地化、角色、变量或资产等需要遍历嵌套 DTO 的索引和重写器。不要用显示标签或规范 schema 名猜测作者 JSON 路径；不要把未知结构当作无引用。相关共享结构化表单重建 FieldDescriptor 时也须保留 Semantic。

关联：docs/spec/editor/localization.md；features/F-20261019-01-editor-game-localization/phase-3.md。

多层 DTO 同样必须沿实际外层 JsonOptions 递归，不在每个子字段恢复其默认配置；否则外层 camelCase 的第二层会再次被当成 PascalCase。NestedWireOptionsPropagateThroughMultipleDtoLevels 两种策略用例通过，最终 Shared 264 项全通过。
