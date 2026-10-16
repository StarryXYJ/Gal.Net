# Entry 默认值也必须通过完整值契约转换

状态：confirmed
来源：F-20261013-02-parameter-and-sample-simplification / Phase 2d

DTO 的枚举默认值经 converter 编码成 JSON 字符串后，EntryBase 的旧默认值投影无条件去掉字符串引号，产生无法被枚举值 codec 读取的编辑文本。显式 null 字符串也会被投影成普通字符串 "null"。

BuildDefinition 现用 ValueCodec.ToEditorText 保留编码语义，再检查 ParseEditorText 往返；不能无损表示的默认值不写入旧字符串 Defaults，仍保留在 FieldDescriptor.DefaultValue，编译时使用原值。Core 的 EditorDefaultsPreserveStringEncodedTypesAndDoNotCoerceNullStrings 已验证。不要把 CLR 字符串与 converter 生成的 JSON 字符串混为一谈；不要靠填入默认字符串抹掉原始 presence。
