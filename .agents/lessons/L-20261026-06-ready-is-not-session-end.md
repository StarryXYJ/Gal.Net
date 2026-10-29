# Engine Ready 不是宿主会话终止

状态：confirmed
来源：F-20261026-01-engine-core-simplification / R8

## 现象与根因

原生打包差分图片验证在 Seek 后出现返回标题页和 debug run 身份改变。Engine 恢复先发布 Ready、随后回放重新进入 Running；AvaloniaGameRun 把除 Running 外的所有状态映射为 IsPlaying=false，GameShell 据此排队返回标题。该中间状态不是停止，UI 队列可能在 Running 映射之前执行导航。

## 正确处理

默认宿主对 Ready 保持已有会话活动状态：首次准备仍为未开始，活动运行中的恢复/Seek 仍为活动。Running 与 Completed/Faulted/Stopped 再更新活动标志。不要删除 Engine 的 Ready 通知，也不要让通用内核拥有页面策略；不要用固定延时掩盖导航竞态。

修正后真实打包 Sample 的五个呈现检查点、不同图片哈希和正常关闭通过。证据：artifacts/engine-core-audit/r8-packaged-native-verified.log（修正前）与 r8-packaged-native-final.log/trx（修正后）。对应状态映射位于 AvaloniaGameRun，当前规则同步 docs/spec/runtime.md。
