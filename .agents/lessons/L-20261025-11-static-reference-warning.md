# 静态引用扫描的正常警告不等于应用失败

状态：confirmed
日期：2026-10-25
来源：F-20261025-02-external-project-authoring / P2

现象：批量删除资源并在同一批次移除 UI JSON 引用后，ProjectApplyTests.ResourceDeletionRequiresRemainingJsonReferencesToBeUpdatedInTheSameBatch 仍返回失败。

根因：ProjectQueryService.References 始终附带 references.partial warning，说明自定义 C# 和运行表达式未被完整分析。以 diagnostics.Count != 0 判定失败，会把无剩余静态候选、但明确不完整的有效扫描也拒绝。

正确做法：应用成败按 Severity=error 和实际匹配引用判断；保留 warning，并明确 ReferencesComplete=false，不把零静态候选写成全局零引用。当前 ProjectApplyService 返回合并诊断并使用错误级别判定。

验证：修正后的 Editor.Shared 全部 417 项及最终批次/热重载 19 项通过；同批清除 UI 引用后资源删除成功，留下引用时拒绝。

适用：消费 References 或其他允许 partial/Complete=false 的查询结果。不要直接用诊断总数代替错误数量，也不要吞掉不完整性说明。
