# 复合条目不能发布容错分析的半份结果

状态：confirmed
日期：2026-10-20
来源：F-20261015-02-editor-authoring-workspace / P6e4c2
关联：[阶段记录](../../features/F-20261015-02-editor-authoring-workspace/phase-6e4.md)

现象：转场经纯编译展开后复用 Timeline 分析。新层 transform.x 为字符串时，五种转场的查询把旧层移除，未保留保守输入；Slide 在编译期拒绝，所以不受同一条路径影响。

根因：Timeline 事件分析逐项捕获错误、标记不完整并继续，适合展示部分作者事实。转场直接复用该结果后，新层创建失败，后续隐藏旧层仍然生效。无异常返回不等于整个复合分析成功。

发现与复现：新增六种 MalformedTransitionTransformCannotApplyOnlyTheOutgoingDeletion 用例，在修复前 5 失败/1 通过。证据位于 timeline-validation/transitions-c2-malformed-before.trx 和同名前缀日志；失败明确显示预期 old，但结果为空。

正确做法：在临时事实状态中分析整个复合展开，单独跟踪本次分析完整性，成功后才发布；局部失败时保留原输入并按既有规则降为可能，不能丢掉原输入。上游本就不完整的标记仍须保留，不能因本次局部成功而提升全局完整性。

适用：复合条目、宏或其他批量语义调用逐项容错的分析器。禁止仅以函数未抛异常判断整批分析成功，或直接重置正式状态的 Complete 来掩盖上游不确定性。后续验证还应覆盖局部失败和上游不完整但本次成功两条路径。

修正验证：transitions-c2-atomic-focused.trx 中 55 项场景分析用例全部通过，包含六种坏 Transform 的整批拒绝和上游不完整标记保留。正式候选仍需连同实际 Editor 控件完成阶段回归，结果以 feature 记录为准。
