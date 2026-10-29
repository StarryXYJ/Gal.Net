# 重复安装仍需验证本次输入归档

状态：confirmed
来源 Feature：F-20261029-05-cross-platform-applications

## 现象与根因

GalpakInstaller 发现已有安装的文件 hash 和 game.pak 均匹配时直接复用。归档的路径与大小检查并不验证源载荷 hash，因此把归档中的 localization.pak 改坏但保持大小不变后，重复安装曾可跳过源完整性检查。

## 发现与验证

在 PackedDocumentTests.ArchiveInstallationPreservesEmptyDirectoriesAndExistingExternalTranslation 中，保留正确安装目录、破坏输入 ZIP 的 localization.pak，要求重复安装和新目录安装均拒绝，并检查原 game.pak 未被改变。修正后集成测试通过。

## 正确做法

复用目标前仍对本次归档全部声明载荷计算并匹配 hash；新安装在复制中验证。不能只因目标内容正确就把任意同清单输入认作有效包。任何失败均保留已有安装与外置翻译。

## 适用范围与禁止做法

适用于带幂等复用的安装、缓存导入和内容合包。不要用目标缓存命中、ZIP 条目大小或 MetadataIndex 验证替代本次源载荷校验；不要为拒绝坏归档删除正确的目标目录。

相关文档：docs/spec/file-formats.md、features/F-20261029-05-cross-platform-applications/environment-matrix.md。
