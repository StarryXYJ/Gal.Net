---
status: confirmed
feature: F-20261102-05-presentation-polish
---
# 嵌套 metadata 的读取必须使用相同 JSON 名称语义

现象：打开 GameTestCase 就 dirty，关闭被取消。对比 CaptureSaved 与 CaptureWorking，只有两个 Gallery Group 的 metadata 不同。

根因：EditorGraphDocument 根字段有 JsonPropertyName，但嵌套 AssetMeta 的 DisplayNameKey/GalleryUnlockId 没有；Graph 作者文件使用 camelCase。Repository 默认大小写敏感 Deserialize 把两个已知字段放到 ExtensionData；ProjectContentSession 的 Web 基线反序列化又将其识别为已知字段，因此基线与当前内容不等。

修正：Repository 显式使用 PropertyNameCaseInsensitive=true 的同组选项读取 Graph；保留 galleryPreviewAssetId 等未知字段。不靠清 dirty 或重命名作者字段绕过。

验证：GameTestCaseEditorTests 同时检查干净打开、已知字段、未知字段保留、编辑保存重开、资产与本地化字节不变、全部组编译。实际项目打开/保存/重开回归已通过。

适用：带 JsonExtensionData 的嵌套 DTO；只给根 DTO 标注名字不足以保证跨序列化选项的往返。
