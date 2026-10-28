# PAK metadata 索引不等于 payload 完整性验证

状态：confirmed，2026-10-28。

现象：P4 覆盖包集成测试翻转普通 sprite payload 的一个字节，安装准备仍成功，直到资源读取才检查出哈希错误。

根因：Archive.Deserialize 验证控制描述、索引、范围等；普通资源的解压长度和 payload 哈希由 GetAsset 校验。MetadataIndex 仅返回表项，不触发解码。覆盖包的全文件 hash 用于优先级排序，是针对当前文件计算的值，不能替代已知期望校验和。

正确做法：无外部可信期望哈希的独立覆盖包，在准备时逐个 GetAsset 并验证 payload；基础/剧情包还需与清单的已知全文件哈希比较。不要将“可反序列化索引”写成“所有内容校验通过”。

验证：StoryPackageExportTests.ResourcePatchExportsBeforeBaseAndSessionsFreezeTheSelectedOverrideSet，损坏覆盖包必须在 CreateAssetProviders 阶段抛 InvalidDataException。

适用：PAK 安装、导入、资源索引和任何以 metadata-only 优化装载的流程。

来源：features/F-20261028-01-content-packaging/phase-4.md；docs/spec/assets.md。
