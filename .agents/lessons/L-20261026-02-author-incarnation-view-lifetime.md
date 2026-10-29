---
status: confirmed
created: 2026-10-26
feature: F-20261015-02-editor-authoring-workspace
---

# 作者 incarnation 不随表单释放推进

现象：GC3 为草稿增加显式 incarnation 后，完整 Editor 回归中 Common/Inspector 共享输入变回源值，关闭所有页面后的本地化草稿保存报源冲突。

根因：把 incarnation 推进放进 `InvalidateParameterFields()`；该方法同时被 Group 页面 Dispose 使用。释放表单租约不等于作者对象消失，原作者内容仍存在，但草稿读源被错误标成 obsolete。

正确做法：`InvalidateAuthorBindings()` 只在删除/移动出原 Group 或换类型时推进作者代次，再释放参数表单；页面关闭只释放参数表单。新表单重用同一作者代次的项目输入，旧控件仍因自身 lease 释放而失去提交权。删除和 Undo 后的新绑定使用新代次，不能恢复旧控件。

禁止用视图生命周期、表单重建次数或对象 RuntimeHelpers 哈希冒充作者身份。覆盖的回归必须同时包括删除/Undo 拒绝旧 lease，以及关闭页面/重开字段/所有视图关闭后的保存；只测第一类会漏掉这次问题。

发现及验证：GC3 初轮完整 Editor 313 通过、5 失败，其中两项为上述生命周期回归；拆分后定向 12 项通过。证据在 `artifacts/group-consistency-gc3/` 的 full 与 focused-repair 日志/TRX，来源见 feature 的 phase-group-consistency.md。
