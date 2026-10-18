# P6c：Timeline 契约与命名统一

日期：2026-10-18。状态：verified。用户确认 Timeline 交互与选择规则后授权开始，先完成跨模块命名/契约的可验证切片。

目标：统一 TimelineDefinition/Options/Track/Keyframe/Event/Sampler/Primitive 及 PlayTimelineAsync，Entry ID 为 timeline.play，根参数为 timeline；不保留旧 AnimationPlan 或 animation.play/plan 别名。animate 单属性入口与共享 AnimationBlendMode/LoopMode 保留。更新转场编译器、运行时恢复、Avalonia/Headless presenter、四语和有效样例/测试；特性历史文档保留当时名称。

LoopAnimationState.Plan→Timeline 改变 builtin.animations 的保存 payload，同步升级模块状态版本，明确拒绝旧版本，不使旧字段被当作缺失新字段。完整 GameSnapshot 外层版本不因单模块 payload 升级盲目递增。

验证计划与退出条件见 phase-plan.md / P6c。当前仅命名/契约切片，Dock、对象/属性查询、关键帧/切线 UI 和 animate 自定义曲线按后续阶段推进；实时预览不纳入计划。

实施发现 PrimitiveRegistry.State 未暴露 Core.StateCatalogBuilder 已支持的 version；新增可选 version（默认 1），builtin.animations 指定 2。新增新 Entry/schema/参数测试与两种循环定义的 JSON state 往返、旧 section v1 拒绝测试；既有 sampler、事件调度、恢复和转场集成用例统一新名称。

首轮解决方案构建成功（119 警告、0 错误），14 项 Timeline 聚焦通过。最终回归首轮 Core 111 通过、Primitives 116 通过/1 失败：TypedBuiltinAuthoringTests 的属性列表期望缺少 time。通过 git show 核对 HEAD，AnimationPropertyValueConverter 在本次之前已经 Append("time")，本次没有修改该转换器；仅补齐旧测试的 shader 常用属性期望，保留完整契约断言，未改变生产行为。后续最终回归见下。


最终隔离解决方案构建成功（3 条警告、0 错误）；Core 111、Primitives 117、Runtime 85、Presentation 243、Integration 110、Editor 156、Shared 214、Architecture 45，共 1081 项通过、0 失败。Presentation 另有 6 个原生媒体环境用例 NotExecuted，未计入通过数。whitespace/IDE0005 verify 与源文件 SHA-256 门禁通过。

证据目录：C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation，包含 validate-p6c.ps1、p6c-*.log/trx、变更 manifest 与源码 hash。当前 src/test C# 已无旧 AnimationPlan/AnimationTrackDefinition/AnimationTrackSampler/AnimationKeyframeDefinition；旧 animation.play 仅在否定测试与解释性文档保留。四语各 620 keys，新增 Timeline 分类及新类型文案。

P6c 退出条件已满足。P6d 的 Timeline Dock、项目选择上下文和叶值编辑尚未实施；P6e 的对象树/曲线/事件编辑和 P6f 的 animate 自定义曲线继续 planned。实时预览没有新增阶段。
