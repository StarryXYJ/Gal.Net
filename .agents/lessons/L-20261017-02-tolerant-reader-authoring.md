# 容错读取不等于可以安全重写

状态：confirmed
日期：2026-10-17
来源：F-20261015-02-editor-authoring-workspace / P4a

现象：AssetMetaCodec 对合法 JSON 中无法类型化的可选字段会回退到默认 DTO；读取仍能取得 ID 和 path，但直接序列化回去会丢掉无效字段及原始扩展内容。

根因：运行时读取的容错策略与作者写入的无损要求不同。文件改名/复制即使只想改 path，也会经过完整 metadata 重写。

正确做法：作者操作先用相同 DTO 和 JSON 配置进行严格类型化校验；失败时拒绝整个文件计划，保留原始资源和 sidecar，不将默认 DTO 当可无损写回的来源。合法 metadata 继续通过 codec 保留 Gallery 和 extension data，不修改 Core 读端既有策略。

验证：ProjectFileTransactionTests.InvalidOptionalMetadataCannotBeSilentlyReplacedByAuthoring 使用 gallery 为字符串且包含 custom 对象的真实 sidecar，改名返回失败，源文件、原 metadata 字节不变，目标不存在。P4a Shared 回归已通过。

适用范围：共享容错 codec 被 Editor 作者写入、迁移或导入工具复用时。禁止仅凭 Deserialize 返回对象就声称数据可无损往返；不能为通过测试静默删掉原字段。
