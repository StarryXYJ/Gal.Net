# 文件路径复用与保留的删除身份

状态：confirmed
日期：2026-10-19
来源：F-20261019-01-editor-game-localization / P1

## 现象与根因

ProjectContentSession.FileDocuments 按会话 ID 持有文件。删除仅将 Exists 设为 false，旧记录为撤销/恢复保留；TrackFile 同一路径时只复用仍存在的记录，否则分配新 ID。因此不能假设所有 FileDocument.Path 唯一，直接 ToDictionary(file => file.Path) 可能在删除后重建时抛异常。

## 发现方式与修正

复核 TrackFile 实现发现该边界；LocalizationReferenceIndexTests.ProjectQuerySeesUnsavedManagedFilesAndDisposalInvalidatesCachedSnapshots 增加删除后同路径 TrackFile 的回归断言。ProjectLocalizationQuery 按平台路径比较器分组，优先选有效记录；无有效记录时保留删除遮罩，不能重新读出磁盘旧文件。多个有效身份同时拥有路径时报告分析不完整。

## 适用范围与禁止事项

适用于作者文件查询、结构化文件编辑器及任何消费 FileDocuments 的服务。不要按路径删除历史身份，不要用删除记录遮蔽新有效文档，也不要忽略删除遮罩并重新读取未保存删除的磁盘内容。长期身份按文件 ID，路径只用于定位当前内容。

关联：docs/spec/editor-history-and-save.md；docs/spec/editor-game-localization.md。

## 2026-10-20 P2：保存期望也必须按有效路径身份归并

新增 LocalizationP2ReplacingAnUndoneNewFileAtTheSamePathCanBeSaved 在修正前稳定失败：第二份同路径文档已创建，但 CheckTrackedFiles 仍把旧删除记录解释为路径必须不存在，报告 files.changed。p2-pre-fix.trx 为 10 通过、1 失败。

ProjectContentSession.CheckTrackedFiles 与 TrackedExpectations 改为先按平台路径比较器归并，优先唯一有效身份，再检查 ManagedContent；不能先筛选 managed 记录，否则可能跳过后来接管该路径的有效非文本身份。历史删除记录继续保留，多个有效身份仍拒绝。
