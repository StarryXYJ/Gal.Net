# 资源 sidecar 进入作者状态后必须统一写入边界

状态：confirmed
来源：features/F-20261031-01-resource-gallery/summary.md

现象：资源名称需要与 i18n 原子撤销，因此将 `.meta` 纳入受管理文件。已有资源改名/删除测试随即失败：原文件改名后 sidecar 仍留在旧作者路径，删除时其内部 path/id 被当作其他文件的候选引用。过滤与压缩若继续直接改磁盘，还会绕开待保存名称版本。

根因：原有文件事务主要按目录后代处理作者文件，资源的同级 `<asset>.meta` 不属于文件路径的后代。普通字面引用扫描又不认识 meta 的身份字段。复制默认读取磁盘，无法获得已应用而未保存的作者版本。

正确做法：把资源与其同级 sidecar 明确作为所有权整体；移动同步迁移作者路径和 meta 内 path，删除/撤销处理相同集合；复制从当前受管理内容读取并单独分配身份。引用扫描跳过 id/path/displayNameKey 等身份字段，显式检查封面资源引用。所有 Inspector 修改走同一作者事务；切换导入选项前提交该资源的名称输入，避免使未提交草稿基线过期。

验证：SelectedAndLockedAssetInspectorFollowIdentityThroughRenameDeleteAndUndo 修正后通过，资源名称/ID/导入设置另有回归测试；完整证据见来源记录。禁止为修复此问题开放普通文本编辑器写入受保护的 Assets/*.meta。

关联：docs/spec/editor/project-and-files.md、docs/manual/editor/resources.md。
