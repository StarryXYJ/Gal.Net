# 组合查询不能把能力缺失压成不可恢复的语义未知

状态：candidate
日期：2026-10-21
来源：F-20261015-02-editor-authoring-workspace / P6e4c3b
关联：[阶段记录](../../features/F-20261015-02-editor-authoring-workspace/phase-6e4.md)

现象：效果 metadata 缺失时，目标身份仍然可知，但能力信息不完整。如果 AtTimeline 从 Before 的扁平 Complete=false 重建内部状态，Timeline 中停止该效果后仍会残留不完整提示。

根因：对外 Complete 同时包含语义分析是否完成、当前目标的能力是否完整。前者必须保守传播，后者会随目标移除而消失。将两者合成的结果再次作为内部语义状态，会永久丢失原因。

发现方式：实现中的代码审查发现 Before → AtTimeline 重建路径；未留存修正前失败 TRX，因此保留 candidate。新增 StoppingAnIncompleteEffectInsideTimelineRecoversOnlyCapabilityCompleteness 同时验证移除缺声明效果恢复完整性、上游未知自定义条目继续不完整。最终验证证据见 timeline-validation/metadata-c3b-analysis-focused.trx 与对应阶段记录。

正确做法：组合内部查询时传递保留原因的内部状态，只在对外结果边界汇总 Complete。不要通过“当前存在能力不完整对象”推断所有 false 都可恢复。既验证可恢复路径，也验证不能抹掉上游语义未知。

适用：对象能力、资源准备状态等会随对象移除而消失的局部不完整事实，与不可逆的未知语义共同组成对外分析状态时。
