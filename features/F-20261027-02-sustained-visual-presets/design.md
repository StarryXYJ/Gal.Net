# 设计

复用预设编译为 timeline.play 的机制。Shake/SpeedLines 增加 playbackMode=Timed/UntilStopped；持续模式生成 Loop 且固定 blocking=false、skippable=false。durationFrames/frameRate 表示循环周期，cycles 是该周期内震动参数。持续震屏保持振幅；持续集中线维持不透明度。

现有 Timeline 的所有事件均按迭代重放，不能直接用于持续集中线。增加事件 execution：EachIteration（默认，保留原行为）、Once（首帧初始化）、OnCompletion（末帧清理，仅 hide/stop 操作）。恢复不重复首帧，循环不重置 Once 的通知状态，退出时执行 OnCompletion。作者原始 Timeline JSON 与复制逻辑继续保留新字段。

animation.stop 立即完成路径等待播放实例的 PresentationCompletion，保证收尾后才推进后续剧情。Timeline presenter 返回正常/跳过结果后，由中立实例补发剩余非 burst 事件并更新循环终态。取消 scope、被替换和 CatchUp 分离不执行完成事件，以免污染新会话或提前结束存档中的持续演出。Headless/CatchUp 已脱离播放的循环通过保存定义清理逻辑状态。

收尾审核补充：循环提交终态需有显式停止请求或 Skipped 结果；宿主单独返回 Completed 不改变循环恢复初值。Timeline 使用稳定完成信号覆盖 presenter 退出及事件清理，点击跳过期间保留播放身份，让交错到达的停止指令也等待同一次清理。

AfterIteration 请求由 Timeline 的单调采样时钟在下一周期边界结束；不阻塞停止 Entry 等待整轮。普通 animation.animate 的 AfterIteration 行为不在本次范围内。

这项修改涉及可序列化 Timeline 契约和跨层停止生命周期，已从轻量入口转为普通实施记录；不进行新的人物动作架构设计。
