# 编辑文本必须携带完整值契约

状态：confirmed
来源 Feature：F-20261013-02-parameter-and-sample-simplification / Phase 2a

## 现象与根因

EditorDocumentRepository 原先只传 ValueType 给文本转换，忽略 schema 的 JsonSerializerOptions。配置 JsonStringEnumConverter 的参数可以通过完整校验，却无法经 Editor 加载/保存。仅按 JsonValueKind.String 去掉引号也不正确：真实 string 的编辑文本是原始文本，但字符串编码的 enum 是 JSON 文本，反向读取必须保留引号。

## 发现与验证

检查参数消费者时发现 Type-only 适配；新增 FieldDescriptorTests.EditorTextKeepsJsonStringQuotesForStringEncodedEnumsAndUsesCustomOptions 与 EditorDocumentRepositoryTests.RepositoryRoundTripsStringEncodedEnumUsingProjectJsonOptions。后者用独立注入的项目 catalog，实际从 rawgroup 加载 Monday，编辑 Tuesday，保存并检查磁盘 JSON；两者均通过。

## 正确做法与禁止做法

ValueCodec.FromEditorText/ToEditorText 两个方向都传完整 ValueDescriptor，以 CLR 类型决定文本表示，并用描述内的冻结 JSON 配置解析、校验。只有真实 string 才去掉 JSON 引号。不从 ValueKind 推断编辑类型，不为现有描述另建只有 Type 的临时描述，不把 UI 文本直接当成内容 JSON。

## 适用范围

Editor、共享 SchemaForm、DTO 源码生成器及任意文本绑定。省略与显式 null 仍需独立状态；本规则不声称旧 string 编辑模型能表示所有 nullable string。
