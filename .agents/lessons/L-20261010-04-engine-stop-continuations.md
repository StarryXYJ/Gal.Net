# Engine 停止必须覆盖自动续跑，而不只跟踪首次推进

状态：confirmed
来源：docs/design/game-runner-refactor.md；GameEngineLifecycleTests

## 现象与根因

旧 Sample 用 GameRunCoordinator 只追踪首次 AdvanceAsync，后续玩家点击、选择结果和阻塞实例完成的续跑不在同一停止边界。只取消首次推进任务不能取消 Engine scope，也无法保证旧 continuation 不再访问宿主资源。

## 正确做法

Engine 在终态停止时先拒绝新请求、取消执行 scope，再清理队列并等待内部 continuation 退出。选择等待必须通过 scope token 中断，不能无条件等待不协作取消的 presenter Task。终态通知必须覆盖所有续跑入口并在执行锁外发布，宿主按 Engine、执行 view、平台后端顺序释放。

## 验证与禁止做法

GameEngineLifecycleTests 验证自然阻塞完成与 Choice 的完成/失败通知、不协作 Choice 的停止、执行中与排队推进的停止、正常结束后不重放和 view 非拥有关系。禁止把“首次 AdvanceAsync 已返回”当成游戏已停止；禁止让宿主分别猜测每种异步入口的终态；禁止在状态事件中同步等待 Engine 异步任务。
