# 实施设计

主设计见 [模块化状态与读档](https://github.com/StarryXYJ/Gal.Net/blob/f17a5934c291d5669ea6615b928321bdf9a2aa02/docs/design/modular-game-state-and-restore.md)。实现先收敛现有中立场景数据为唯一状态记录，删除 Active* 镜像；场景对象的纯数据/动画属性操作可以共用，执行 Task、取消、播放身份与停止协议仍独立。

状态表由执行模块组合时显式注册，冻结 catalog 后与恢复处理器一起由 view 提供给宿主。Engine 只消费 catalog，恢复协调器只消费注册处理器。StateId 对外稳定，内存 Type 索引；编码后的 JSON section 是隔离快照。旧格式直接拒绝。


实际实现复用可序列化的 Layer/EffectInstance/ParticleEmitterInstance 中立数据，避免新增镜像 DTO。它们不是执行实例；Task、停止控制、backend identity 仍单独存在。字段修改由 State.Update 同步，快照经 codec 隔离，未实施深度不可变对象模型。视频替换记录引用来隔离旧任务清理。

恢复资源使用一个 StateRestoreSession，模块注册依赖和清理回调；顺序恢复、逆序释放，清理失败聚合报告。音频逻辑数据直接写注册表，派生 TrackBinding 仅引用配置与记录；没有额外权威队列。所有轨道按检查点恢复，活动内容从头开始，暂停轨道等待 Resume。跨表业务校验由模块声明，不声称能从 CLR 类型自动推导。

架构决定记录见 [ADR-0005](https://github.com/StarryXYJ/Gal.Net/blob/f17a5934c291d5669ea6615b928321bdf9a2aa02/docs/adr/0005-modular-persistent-state.md)。
