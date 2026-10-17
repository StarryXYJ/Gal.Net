# 集成 fixture 不应覆盖要验证的生产服务策略

状态：confirmed
日期：2026-10-18
来源：F-20261015-02-editor-authoring-workspace / P5e

现象：GUI 已注册 Authoring 仓库，但 AuthoringDraftsReopenInMountedGroupAndKeepOpaqueValuesThroughEditsCopyUndoAndSave 首次仍在 SerializeEntry 抛 Unknown parameter 'future'。堆栈经 ProjectContentSession.SaveAsync，容易误以为生产 DI 没有生效。

根因：公共 Services() fixture 在 AddEditorServices() 后无条件覆盖 IEditorDocumentRepository，手工 new 默认 Complete 仓库；这个覆盖原本只是为了支持文件故障注入，却改变了所有普通用例的生产策略。

修正：没有故障事务时直接保留生产注册；确需注入故障时仅包装明确 Authoring 的仓库。禁止为了让集成测试通过而全局把 CLI 默认模式改为 Authoring，或让所有测试另行手工拼装“等效”生产策略。

验证：修改 fixture 后同一真实 GUI 草稿保存、重开、字段编辑、Entry/Graph 复制和 Undo/Redo 用例通过；p5e-editor-focused.trx 为修复后 2/2，首次失败堆栈保留在本轮工具输出及 phase-5e 记录。

适用：已有 DI 集成 fixture、新增服务策略、临时 fault decorator、production 注册回归。关联 docs/spec/editor-groups.md 与 phase-5e.md。
