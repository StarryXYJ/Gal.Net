# 旧运行取消回调异常不能截断恢复提交

状态：confirmed
来源 Feature：[引擎核心精简](../../features/F-20261026-01-engine-core-simplification/summary.md)

## 现象与根因

CancellationTokenSource.Cancel 会先发布取消，再执行已注册回调；回调抛错时 Cancel 会抛 AggregateException。Engine 恢复若直接依次 ClearActive、Cancel、Restore，旧回调异常会在状态替换前截断执行，留下已清空活动实例但旧 scope 已取消的半份运行。

## 正确做法

先完整准备和验证候选，再让旧 scope 失效。对已经脱离执行的旧 scope，隔离并诊断取消回调异常，保证 source 释放和新状态提交继续进行。该规则不适用于保存/进度落盘失败或必要的新呈现恢复失败，这些仍需向调用方报告。

运行 scope 的变量/结束操作，应在捕获的旧状态同步边界内检查取消；恢复提交也经过旧状态边界，避免检查通过后跨代写入。

## 验证与适用范围

GameEngineLifecycleTests.ObsoleteCancellationCallbackFailureCannotLeaveRestorationHalfApplied 已复现并验证：首个 scope 的取消回调抛 IOException，合法恢复仍回到 Ready，随后能重新执行。适用于 Engine 读档、旧 Choice 清理和相同的运行替换边界。

不要把所有 OperationCanceledException 或任意执行异常一概吞掉，也不要把验证失败后的旧运行先销毁。
