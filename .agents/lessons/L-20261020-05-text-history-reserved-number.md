# 文本历史不能回退不可撤销的角色编号

状态：confirmed
日期：2026-10-20
来源：F-20261019-01-editor-game-localization / P4

原文复核状态和 characterHighWater 同在 Graph/localization-authoring.json。编号预留直接更新 Working/Saved，故创建角色再撤销仍保留更高编号；文本历史若按整个 metadata 字节检查/恢复，会拒绝更早文本的撤销，或把编号写回旧值。

LocalizationEditOperation 对该文件仅允许 characterHighWater 单调重基：期望比较忽略这一字段，实际值不能低于期望值；应用文本 before/after 时保留当前更高编号。其他 metadata 字段仍必须相等，不能把例外泛化成整文件盲覆盖。编号未变时返回原目标字节，避免仅因格式变化产生 dirty。

GameTextUndoPreservesRoleNumberReservationsAndEmptyTextIsDistinctFromMissing 验证文本→角色→撤销角色→撤销文本→重做文本→创建角色不重用编号；初始定向通过。适用于把不可撤销预留与可撤销编辑存入同一作者文档的场景。相关规范：docs/spec/editor-game-localization.md；过程：features/F-20261019-01-editor-game-localization/phase-4.md。

P6 补充（2026-10-21）：同一文档新增不可撤销的 exchangeProjectId 预留。旧文本历史的期望缺少该字段时，可重基到已经初始化的合法 GUID，应用 before/after 必须保留当前 GUID；期望已有 GUID 时仍严格匹配，不能将两种不同身份视为等价。仅同样保护明确的不可撤销预留，不放宽其他 metadata。交换用例验证首次模板导出后旧文本操作仍能撤销/重做，身份持久保留，其他 dirty 内容不会被导出顺带保存。
