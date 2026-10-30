# 外部调试子进程的监控生命周期

状态：candidate
来源 Feature：F-20261021-03-external-game-debugging

## 现象、根因与证据

外部 App 超时后按策略继续运行；原 `DisconnectAsync` 却等待启动监控任务结束，而监控任务等待子进程退出。用户断开一个仍运行的 App 时，Editor 因此会继续等待。原启动监控还把 stdout/stderr 读取绑定到调试会话取消令牌；提前停止读取会让仍存活的子进程失去管道消费者。

根因由代码路径确认，尚待真实运行复现。

## 正确做法

断开只使当前调试 epoch 失效、关闭控制连接并取消 UI 会话等待，不等待外部进程退出。只要 Editor 仍在运行，就继续排空子进程 stdout/stderr；让启动监控在子进程退出后结束并关闭该次诊断文件。迟到连接和退出事件必须通过 epoch 检查，不能更新新调试会话的状态。

## 适用范围与禁止做法

适用于 Editor 启动但不拥有终止策略的外部进程。不要把子进程退出当作断开操作的完成条件；不要取消仍存活子进程的重定向管道读取。

## 关联文档

- `features/F-20261021-03-external-game-debugging/phase-liveness-diagnostics.md`
- `docs/manual/editor/preview-and-debugging.md`
