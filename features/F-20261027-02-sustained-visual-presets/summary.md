# 持续演出预设与统一停止

震屏、集中线新增 playbackMode（Timed / UntilStopped）。持续模式固定不阻塞、不随对白点击跳过；震屏保持振幅，集中线维持显示。人物动作、冲击及转场仍单次。blocking 已列为常用字段，播放方式具备中英日韩四语标签。

一个 animation.stop / CompleteImmediately 即可结束演出：震屏移除叠加偏移，集中线移除自己创建的 Effect 与临时图层，保留其他效果和人物基础位置，收尾后允许复用播放标识。停止 Entry 等待真实收尾；初始化期间停止，以及点击跳过和停止交错，也使用同一完成信号。

Timeline 事件新增可选 execution：EachIteration 保持旧行为，Once 用于首帧初始化，OnCompletion 用于末帧 hide/stop 清理。持续集中线循环不重建图层/Effect，恢复不重复初始化。Timeline 的 AfterIteration 在下一轮边界完成，Headless/CatchUp 无活动呈现的循环直接清理逻辑状态。编辑器复合分析区分结构错误和时间不确定性，停止候选返回原预设的 playbackHandleId 槽位。

## 验证

- Primitives 321、Editor.Shared 449、Presentation 262、Runtime 123、Architecture 52、Editor 预设创建/撤销/保存重开 1 项：共 1208 项通过。
- 完整 GalNet.slnx 构建：0 错误；3 项已有 AVLN3001 警告。
- locked restore 成功；四语各 1105 key 一致；修改文件格式和 diff whitespace 复核。
- 新增 14 项用例覆盖配置、循环初始化、停止收尾、等待异步清理、初始化交错、跳过交错、恢复、Headless、播放标识复用和真实 Avalonia 震动偏移移除。

## 修正与限制

完整回归曾发现宿主直接结束循环呈现时错误提交末值（恢复粒子 emissionRate 从 18 变成 36）；已要求有明确停止意图并通过原用例。额外的跳过/停止交错回归先复现停止过早完成，再修正为稳定完成信号与延后身份释放。经验见 `.agents/lessons/L-20261027-03-timeline-stop-event-cleanup.md`。

主目录存在并行音频开发，因此基于 0818aab4 创建独立工作树验证。初次主目录音频缺失 JsonElement using 和首次独立工作树 sandbox 写权限失败均为验证环境/并行开发问题，未借此修改产品功能。

未运行显式原生视频或 shader 图片导出用例；没有新增图形特效或修改 shader。持续集中线的 shader time 按作者循环周期重播；不承诺跨周期相位无缝。未新增人物姿态中断、淡出停止或复杂动作编排。

当前操作见 [默认视觉库手册](../../docs/manual/default-visuals.md)，事件契约见 [Entry 类型](../../docs/spec/entry-types.md)，停止生命周期见 [Runtime](../../docs/spec/runtime.md)。
