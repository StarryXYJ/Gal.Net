# Phase Plan：Entry 模块与 PrimitiveInstance 运行时收敛

## Phase 1：核心 Entry 与实例契约

状态：verified

**目标：** 用两张冻结 Entry 表和 PrimitiveInstance 工厂替代 Descriptor/Handler 双重注册。

**前置条件：** 当前动态参数表和 target profile 改动保留；编译文件仍输出通用 PrimitiveEntry。

**涉及模块：** `GalNet.Core`、`GalNet.Primitives.Builtins`、`GeneralTest`。

**任务：**

1. 定义 `EntryBase`、`PrimitiveEntryBase`、`DefaultPrimitiveEntryBase`、`CompositeEntryBase` 和 `PrimitiveInstance`。
2. 让每个 Entry 定义持有唯一参数表，增加只读取该表的无状态参数 helper，并让工厂上下文暴露参数表、规范化参数 JSON、Runtime、Scope cancellation 和本次 BatchId。
3. 将模块收敛为冻结的 Primitive/Composite 两张表，并验证模块 ID、Entry 名称和重复注册。
4. 调整 target profile 从 Entry 定义直接建立编辑器/编译目录。
5. 迁移推荐 authoring 模块定义，把编辑器/Composite 提供的可选 BatchId 写入通用 `PrimitiveEntry` 及序列化 DTO，不生成全局 ID。
6. 删除被替代的 Handler、Dispatch Policy 和 ExecutionControl 契约。

**测试：** 模块冻结、重复项、默认工厂、共享参数 helper、工厂上下文字段、可选 BatchId 原样传递、实例单次 Dispatch/幂等完成、空 profile 和纯 Composite 模块。

**风险：** 当前工作树包含未提交的模块表迁移，修改时必须保留已经成立的 DynamicParameterTable 与 transition authoring 迁移。

**验证（2026-09-22）：** `EntryBase`、`PrimitiveEntryBase`、`DefaultPrimitiveEntryBase`、`CompositeEntryBase`、`DefaultCompositeEntryBase`、`PrimitiveInstance` 与 `DefaultEntryModule` 已实现；模块两表使用只读字典冻结。旧 Handler、Dispatch Policy 与 ExecutionControl 契约已删除。工厂上下文、共享参数规范化和编译数据 BatchId 均有自动化测试覆盖。

**退出条件：** Core 不再需要 Handler/Policy 才能描述一次原语调用；工厂上下文与编译数据 BatchId 测试通过；相关 Core/Entry 测试通过。

## Phase 2：Engine 队列与 Advance 调度

状态：verified

**目标：** 让 `IGameView` 只执行单条 primitive 调用，由 `GameEngine` 唯一拥有活动队列并通过 `AdvanceAsync` 协调 Skip、Group 步进、节点跳转和稳定快照。

**前置条件：** Phase 1 的 PrimitiveInstance 工厂可用。

**涉及模块：** `GalNet.Presentation.Abstractions`、`GalNet.Presentation.Defaults`、`GalNet.Runtime`、`GeneralTest`。

**任务：**

1. 重写 `CompositeGameView` 的完整 ID 路由、参数规范化、实例创建和单次 Dispatch，并删除其活动队列。
2. 在 Engine 实现 `GroupExecutionId`、Sequence、活动实例队列和完成清理。
3. `GameEngine.AdvanceAsync` 在调用开始时只选择最早 Blocking 实例的局部 Batch，并 Skip 同一次 Group 执行、同 Batch 的全部已分发可跳过实例，包括 NonBlocking。
4. 当前阻塞解除后继续顺序分发到下一阻塞点，但本次调用不得 Skip 新遇到的 Batch。
5. Blocking 自然完成只触发无 Skip 权限的私有 continue；NonBlocking 完成不唤醒 Engine，在后续步进、Group 结束或 Dispose 时惰性清理。
6. 将条件、Choice 和 Jump 保留为 Engine 内置控制流；Choice 通过窄展示端口取回可见索引，不再创建 `interaction.choice` primitive。
7. Choice 等待作为 Engine 流程阻塞，在调度门外等待；完成后回到门内校验索引、跳转并继续。
8. 用同一个异步调度门串行化 Advance、Blocking 完成通知和 Engine 队列清理，完成回调只排队、不递归推进。
9. 删除 Runtime `OperationManager`，调整 Engine 与 NullGameView。

**测试：** 一次 Advance 只 Skip 一个既有 Batch、解除后运行到下一阻塞点、同 Group 同批跨 Blocking 属性 Skip、跨 Group 同名 Batch 隔离、动态 Skippable 原子切换、Blocking 自然完成继续、NonBlocking 惰性清理、条件分支、Choice 索引映射/无效索引/取消/读档重建、Choice 不响应 Advance、未知原语安全跳过、完成/Skip 幂等和完成通知重入。

**风险：** 无公开 Pump 时，Blocking 定时完成必须通过实例完成通知唤醒调度，不能复用玩家 Advance；NonBlocking 不唤醒，只惰性清理。

**验证（2026-09-22）：** `CompositeGameView` 只解析并分发一条 primitive；活动队列、局部 Batch、自然完成、条件、Choice 和节点跳转均由 Engine 管理。推荐模块集成测试覆盖 Layer、Dialogue、未知指令跳过和 Choice 可见索引映射的完整路径。

**退出条件：** Engine 是活动实例队列唯一所有者；剧情阻塞和局部批次跳过由新队列测试覆盖；玩家入口真正统一为 `GameEngine.AdvanceAsync`；旧 OperationManager 无引用。

## Phase 3：快照边界与 Dialogue `\skip`

状态：verified

**目标：** 落实无 Blocking 的快照边界，并将打字机分段跳过放入 dialogue 模块实例。

**前置条件：** Phase 2 的实例队列和内部继续路径可用。

**涉及模块：** `GalNet.Runtime`、`GalNet.Core.Text`、`GalNet.Avalonia.Controls`、`GalNet.Avalonia.GameView`、推荐 dialogue 模块、`GeneralTest`。

**任务：**

1. 将快照更新条件改为逻辑游标已提交且无未完成 Blocking/Choice 流程等待，不要求活动队列为空，并删除 `CreatesCheckpoint` 判断。
2. 提取共享打字机指令识别并加入 `\skip` 与 `\\`。
3. 实现 dialogue PrimitiveInstance 的分段跳过和全文后再次 Advance 完成语义。
4. 保证 Skip 不能被旧异步写入覆盖最终显示状态。
5. 迁移现有 Avalonia/Headless 对话入口。

**测试：** 普通/富文本一致解析、转义、连续 `\skip`、富文本跨边界、NonBlocking 活动时快照更新、Blocking 期间保留旧快照。

**风险：** 当前 TypewriterTextBlock 的 Skip 会永久立即显示剩余全文，不能直接作为新分段语义使用。

**验证（2026-09-22）：** Engine 快照条件已改为“无未完成 Blocking 实例”。普通/富文本解析共享反斜杠指令识别；Dialogue PrimitiveInstance、Avalonia 与 Headless 均已迁移。Headless 在打字完成后通过同一个 `GameEngine.AdvanceAsync` 入口继续剧情。

**退出条件：** 对话每次 Advance 至多跨一个边界；存读档不序列化活动实例且稳定状态测试通过。

## Phase 4：推荐模块迁移与清理

状态：partially verified

**目标：** 迁移其余推荐原语并删除旧执行路径和矛盾文档。

**前置条件：** 前三阶段契约稳定。

**涉及模块：** 全部推荐 primitive 模块、Sample、Editor Preview、文档和测试。

**任务：**

1. 迁移 layer、animation、effect、particle、flow、variable、gallery 及现有媒体模块；animation plan 整体作为一个 instance，不迁移通用嵌套 Entry 分发。
2. 长期行为使用可序列化 Handle，启动实例及时完成。
3. 删除 Avalonia 页面旧活动动画批次决策和 Runtime Handler 残留。
4. 同步 `primitive-module-runtime-design.md`、长期 phase plan、runtime 和 entry spec。

**测试：** 解决方案构建、全量单元测试、Editor/Headless 组合根测试及关键运行路径冒烟。

**风险：** 音频系统仍有独立 discovery feature；本阶段只迁移现有能力，不扩张其产品范围。

**退出条件：** 新模型是唯一 Entry 执行路径，旧契约无引用，相关文档与实现一致，全量验证通过。

**当前范围（2026-09-22）：** 本轮已在新 instance 模型下实现 layer、dialogue/typewriter、Engine 内置流程跳转、animation、effect、flow.wait 和 variable.set，并接入 Avalonia Sample、Editor Preview 与 Headless Sample 的组合根。Animation plan 整体作为一个 `AnimationPlanPrimitiveInstance`；内部 layer/effect 事件仅作为 animation 模块私有事件处理，不重新进入通用 Entry 分发。Audio、video、particle 和 gallery 仍保留推荐 schema，完整产品行为留给后续 feature。

**验证证据（2026-09-22）：** `GeneralTest` 全量 219 项通过；新增测试覆盖 animation/effect runtime state、presenter 调用、plan final state 和 `flow.wait` skip。受限环境中构建/测试需设置 `AVALONIA_TELEMETRY_OPTOUT=1` 并传入 `-p:UseSharedCompilation=false`。文档已重写 `docs/spec/runtime.md`、`docs/spec/entry-types.md`、`docs/spec/architecture.md`、`docs/glossary.md`、`docs/design/primitive-module-runtime-design.md` 和长期 phase plan。
