# Phase 4b3：文本推进状态机复用

2026-10-15，状态：verified。

Builtins 内新增 TextAdvancePrimitiveInstance，共用打字/等待推进/完成阶段、同步保护、全文呈现标记、自动等待切换、skip 和运行模式处理。展示结束后 PresentationCompletion 完成，IsCompleted 仍为 false，等待 CompleteAdvance/下一次玩家推进；没有套用单次异步操作结束即完成的 AsyncPrimitiveInstance。

Dialogue/NVL 分别重载具体展示、立即展示、skip、揭示全文、CatchUp 历史和失败诊断。Dialogue 保持吞展示错误，NVL 保持警告后完成；当前 scope 取消保持原处理。初始 CatchUp 只记录历史并完成，Quick 显示全文后仍等待推进。每次调用独立状态，不共享可变实例。原工作区 Dialogue 的 Lock 改动保留为共用状态机的 Lock 字段，其语义没有撤销。

PresentationCompletion 从创建时保持同一任务；正文/自动等待状态在完成任务之前更新。同步展示启动/历史记录失败保留同步异常并发布失败任务；失败诊断 hook 自身失败时也保留两个错误，完成实例，避免挂起。

验证：Builtins 110、Runtime 80、Integration 104、Architecture 37，共 331 个不重复用例通过，TRX 位于 Temp/F-20261013-02/phase4b3-*.trx。包括原 Dialogue/NVL 测试、Engine 自动等待/阅读边界/模式测试，以及稳定任务与逻辑推进分离、连续 skip、50 次 CatchUp/自然展示完成竞态（每次只通知一次）、展示失败等新增测试。修改文件 IDE0005 与 whitespace 整理通过。

生产 C# 含注释/空行：原 Dialogue 130 + NVL 128 = 258 行；迁移后共用实现 97 + 两实例各 28 = 153 行，净减 105 行。计入全部公共机制后，Phase 4b 的 4b1 +29、4b2 +25、4b3 -105，合计净减 51 行；清理和新作者 API 收益仍分别记录，不把测试/文档算入生产删行。
