---
id: F-20260922-01-entry-instance-runtime
title: Entry 模块与 PrimitiveInstance 运行时收敛
type: runtime
status: implementation
created: 2026-09-22
updated: 2026-09-22
---

# Entry 模块与 PrimitiveInstance 运行时收敛

## 问题与目标

当前原语模块方案把一次执行拆成 Descriptor、Handler、Dispatch、ExecutionPolicy、ExecutionControl 和 OperationManager，多处对象共同描述同一个调用，导致 Entry 定义、模块路由和运行实例职责分散。

本 feature 将 Entry 模型收敛为模块拥有的两张只读表，以及每次原语调用独占的 `PrimitiveInstance`：

- `IGameView` 挂载多个 Entry 模块并负责单条原语的定义解析、实例创建与 Dispatch；`GameEngine` 拥有活动实例队列和唯一玩家 `AdvanceAsync`，协调 Skip、Group 步进、节点跳转和稳定快照。
- 每个模块拥有 Primitive Entry 和 Composite Entry 两张冻结表。
- Primitive Entry 持有参数描述表，并通过工厂创建一次调用对应的 `PrimitiveInstance`。
- Composite Entry 只服务编辑器和编译器，并按自身语义决定展开原语的 BatchId；框架不规定一次展开对应一个批次。
- `PrimitiveInstance` 只暴露 BatchId、阻塞、可跳过、完成状态以及 Dispatch/Skip 行为。

## 已确认需求

- 对外只有一个玩家推进入口 `GameEngine.AdvanceAsync`，不暴露独立 Step/Pump/Update/SkipNextBatch。
- 实例自然完成时可以触发内部继续调度，但不能伪造一次玩家 Advance。
- BatchId 是编辑器传入的可选局部分组参数，同时存在于编译后的 primitive 调用数据和创建后的 `PrimitiveInstance` 中；只在一次 Group 执行内匹配。
- Primitive 定义的参数描述表是编辑、编译和实例工厂的同一 schema 来源；工厂上下文需要能拿到参数表、参数 JSON、Runtime 和本次 BatchId。
- Advance 选择最早 Blocking 实例所属 Batch，并 Skip 该 Batch 中全部当前可跳过实例，包括 NonBlocking 实例。
- 一次 Advance 只对调用开始时最早的 Blocking Batch 拥有 Skip 权限；解除当前阻塞后可以继续步进到下一阻塞点，但不得 Skip 新遇到的 Batch。
- Batch 只表示同一次 Group 执行中活动实例的 Skip 关联，不表示并发或事务边界；不同 Group 可以自由复用同一 BatchId。
- 条件、选项过滤、edge 映射和节点 Jump 是 Engine 内置控制流，不注册成 primitive；Choice 展示端口只负责返回可见选项索引。
- 对话与打字机属于 dialogue 模块；`\skip` 表示一次文本跳过的边界。
- 快照以可恢复逻辑边界为准；活动 NonBlocking 呈现实例不应仅因仍在队列中阻止快照。
- 稳定快照由 Engine 在所有稳定边界统一更新，不使用 `CreatesCheckpoint` 等 primitive 元数据。

## 范围

- 重构通用 Entry/module/PrimitiveInstance 契约。
- 重构 `IGameView` 默认组合实现的模块挂载与单条实例创建；活动队列与批次跳过移入 `GameEngine`。
- 重构 `GameEngine` 的推进和快照协作。
- 将推荐 dialogue/typewriter 路径迁移到新实例模型并加入 `\skip`。
- 迁移现有推荐模块和测试，删除被新模型替代的 Handler/Operation 抽象。
- 同步设计、运行时和 Entry 规范文档。

## 非目标

- 不规定 Composite Entry 展开出的原语必须共享 BatchId。
- 不序列化 Task、CancellationToken、平台控件或活动 `PrimitiveInstance`。
- 不引入运行期模块热加载。
- 不在本 feature 中重做资源模块或完整音频系统。
- 不设计动画时间线内的通用嵌套 Entry；整个动画计划只对应一个 primitive instance。

## 验收标准

- 模块的 Primitive/Composite 两张表冻结且重复注册会失败。
- 默认 Primitive Entry 可以通过构造传入的工厂创建实例，无需每个定义一个 CLR 类型。
- 工厂上下文包含参数表、参数 JSON、Runtime 和本次 BatchId，并且每次调用都创建独立实例。
- 编辑器传入的可选 BatchId 经 `PrimitiveEntry`/序列化 DTO 原样传到 `PrimitiveInstance`；空值表示该实例独立成批。
- Blocking、NonBlocking、动态 Skippable 和分批 Skip 有自动化测试。
- 同批 NonBlocking 实例会随当前 Blocking Batch 一起收到 Skip。
- 跨 Group 的同名 BatchId 不会关联。
- 实例自然完成能继续运行到下一阻塞点，但不会跳过下一 Blocking Batch。
- `\skip` 在普通文本和富文本解析中具有一致语义，字面反斜杠可转义。
- Choice 等待不进入 PrimitiveInstance 队列、不响应 Advance Skip；选择完成后由 Engine 内部继续到下一阻塞点。
- GameView 不保存活动实例；已完成 NonBlocking 实例由 Engine 在后续步进、Group 结束或 Dispose 时清理。
- 快照不会被纯 NonBlocking 呈现实例无限阻塞。
- 解决方案构建及相关测试通过。

## 约束

- Core/Runtime 不引用 Avalonia 或其他平台类型。
- 可存档逻辑状态先于异步呈现提交；读档不恢复活动 Task。
- 保护当前工作树中已经完成的动态参数表和 Entry profile 改动。

## 相关文档

- [设计](design.md)
- [实施计划](phase-plan.md)
- `docs/design/primitive-module-runtime-design.md`
- `docs/design/primitive-module-runtime-phase-plan.md`
- `docs/spec/entry-types.md`
