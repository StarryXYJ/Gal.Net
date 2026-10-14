# 原语异步结果必须先于完成通知发布

状态：confirmed
来源：F-20261013-02-parameter-and-sample-simplification / Phase 4b1

## 现象与根因

抽取 AsyncPrimitiveInstance 时，先调用 TryComplete、再填充 TaskCompletionSource，会让 Completed 订阅者在 PresentationCompletion 尚未 faulted 时读取结果。GameEngine 的 ActivePrimitiveRegistry.RemoveCompleted 仅在该任务已经 IsFaulted 时抛出错误；完成回调可以立即推进，因而错误可能被误当成成功。

## 证据

AsyncPrimitiveInstanceTests.CompletionObserversSeeThePublishedFailure 在 Completed 事件中读取 IsFaulted；顺序正确时通过。PrimitivePresentationFailureTests 的 fixture 直接继承公共异步实例，覆盖分发中立即失败与分发后失败，两者都进入 Engine Faulted。Phase 4b1 最终 Core 104、Runtime 80 用例通过。

## 正确做法

先等待业务操作及其 finally 清理，发布 PresentationCompletion 结果/错误，再 TryComplete。稳定完成任务从实例创建时存在，避免 Dispatch 前交付的任务与运行任务不同。PresentationCompletion 和 Completed 是两个不同同步点：需要等待事件通知的测试应明确等待通知，不能只 await 展示任务后立即断言事件计数。

## 适用范围与禁止做法

适用于会通过 Completed 触发 Engine 续跑的异步原语及其默认实现。不要为了让 await task 之后逻辑标记必定完成而反转错误发布顺序；不要只验证实例最终完成而遗漏 Engine 的错误路径。

关联：docs/spec/runtime.md；features/F-20261013-02-parameter-and-sample-simplification/phase-4b1.md。
