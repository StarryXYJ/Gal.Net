# 新持久字段必须进入手写保存基线比较

状态：confirmed，2026-10-29。

现象：资源补丁即时保存回归中，未保存的 Group.storyPackage 仍在工作版，磁盘保持原值，但 ContentSession.IsDirty 错误为 false。

根因：DTO、克隆和重载已保留 StoryPackage，ProjectContentBaseline.Node 的手写相等比较却没有该字段。完整构建和保存/撤销回归不能自动覆盖“只改该字段”的 dirty 判断。

正确做法：增加持久字段时搜索序列化、克隆、重载、等价判断、恢复与发布基线；验证单独修改字段使会话 dirty，局部发布其他内容后仍 dirty，磁盘没有被顺带保存。不以强制 Save All 掩盖漏项。

验证：ResourcePatchesPersistWithoutSavingOtherContentAndExportDoesNotCommitDrafts，包名工作版与磁盘不同时 IsDirty=true，资源补丁节保存及导出均保留差异。

来源：features/F-20261028-01-content-packaging/follow-up-panel-20261028.md；ProjectContentBaseline.Node。
