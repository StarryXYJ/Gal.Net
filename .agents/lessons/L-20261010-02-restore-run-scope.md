# 读档必须隔离旧运行实例

状态：confirmed
来源：Entry 契约/执行注册重构（docs/design/entry-registration-refactor.md）

## 现象与根因

仅清空 Engine 活动列表并在原 SceneState 上恢复内容，不能阻止仍持有 Runtime 的旧异步实例在 finally 中清理新状态。取消令牌只是一种协作信号，不保证旧任务已退出。

## 发现与证据

审查发现 Engine 原先读档不更新 scope cancellation。新增 GameEnginePrimitiveDispatchTests.RestoreCancelsOldScopeAndIsolatesSceneAndSnapshots 验证旧 scope 被取消、旧实例的场景引用不能清理新场景、控制流操作抛出取消异常、调用者快照修改不污染 Engine。

## 正确做法

先验证存档版本并复制输入，再取消旧 scope；恢复时创建新的 SceneState/SceneInstanceManager。实例收到 RunScopedRuntime，持有该代场景对象，变量和控制流操作检查该代令牌。外部快照与 checkpoint 参数深复制。句柄复用时，异步清理还须检查实例 identity，不能仅按字符串句柄移除。

## 适用范围与禁止做法

适用于读档、重载、重启、同句柄替换。禁止把 ClearActive 当作取消；禁止让旧 finally 按句柄无条件清理新状态；禁止取消现有运行后才检查非法存档版本。

关联：docs/spec/runtime/execution.md；GameRuntimeSnapshotTests；AnimationPrimitiveInstance；AnimationPlanPrimitiveInstance。
