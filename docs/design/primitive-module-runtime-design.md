# Entry Module 与 PrimitiveInstance 运行时设计

> 状态：已收敛到当前实现。本文描述新的运行时职责边界；旧的 Handler、Descriptor、OperationManager 方案已废弃。

## 1. 目标

Entry 系统只解决三件事：

1. 用模块组织编辑器可用的 Entry 定义。
2. 在编译期把 Composite Entry 展开为 Primitive Entry。
3. 在运行期由 `IGameView` 创建并分发单条 primitive instance，由 `GameEngine` 保存和调度活动实例。

设计要求：

- Core/Runtime 不依赖 Avalonia、Skia、具体文件系统或平台控件。
- 模块注册事实只有一份：entry 定义自己持有参数 schema 和运行期工厂。
- 每次 primitive 调用创建独立 `PrimitiveInstance`。
- 玩家推进入口只有 `GameEngine.AdvanceAsync`。
- 快照由 Engine 在稳定边界统一更新，不由 primitive 元数据决定。
- Choice、条件、edge 映射和节点跳转保留为 Engine 内置控制流。

## 2. 总体结构

```text
IEntryModule
├─ PrimitiveEntries : IReadOnlyDictionary<string, PrimitiveEntryBase>
└─ CompositeEntries : IReadOnlyDictionary<string, CompositeEntryBase>

.rawgalgroup Entry
  -> GalgroupCompiler
  -> PrimitiveEntry { Type, Arguments, BatchId, Condition }
  -> CompositeGameView.Dispatch
  -> PrimitiveInstance
  -> GameEngine active queue
```

三个阶段的对象严格分离：

- `EntryBase` 及其派生定义是模块中的 schema，生命周期与模块一致。
- `PrimitiveEntry` 是编译后的运行期调用数据，可序列化。
- `PrimitiveInstance` 是一次调用产生的运行状态，不序列化。

## 3. 模块与参数 schema

每个模块构造后冻结两张表。表 key 与 entry `Name` 一致；同一模块内 primitive/composite 不得重名，多个挂载模块之间也不得有重复 primitive 名称。

`EntryBase.Parameters` 是编辑器表单、编译校验、默认值填充和运行期工厂的唯一参数 schema 来源。`DynamicParameterTable` 只读保存：

- 参数名。
- 进程内运行时 `Type`。
- 是否必填。
- JSON 默认值。
- JSON constraints，例如 editor type 和选项。

内容文件和存档只保存 JSON 值，不保存 CLR 类型名。

## 4. Primitive 工厂上下文

`PrimitiveEntryBase.CreateInstance()` 接收 `PrimitiveCreateContext`：

```text
PrimitiveCreateContext
├─ Definition        : PrimitiveEntryBase
├─ Entry             : PrimitiveEntry
├─ Runtime           : IGameRuntime
├─ ScopeCancellation : CancellationToken
├─ Parameters        : DynamicParameterTable
├─ Arguments         : JsonElement
└─ BatchId           : string?
```

`CompositeGameView` 在创建上下文前先用 `PrimitiveArgumentHelper.Normalize()` 规范化参数。进入工厂的 arguments 已包含默认值，且已移除编译期提升到通用字段的 `batchId` 参数。

`DefaultPrimitiveEntryBase` 允许模块用构造传入的委托创建 instance，不要求每个 primitive 定义一个独立 CLR 类型。

## 5. BatchId

`batchId` 是 authoring 输入中的普通可选参数，但编译后提升到 `PrimitiveEntry.BatchId`。运行时不从领域参数 JSON 再解释批次。

Engine 将局部 batch 与内部 `GroupExecutionId` 组合使用：

```text
runtime batch key = (GroupExecutionId, PrimitiveInstance.BatchId)
```

因此：

- 同一次 Group 执行内，同名 batch 的已分发实例可一起响应一次 skip。
- 跨 Group 或重复进入同一 Group 时，同名 batch 不关联。
- 空 batch 表示实例独立成批。

## 6. PrimitiveInstance

公共基类只暴露 blocking、skippable、completed、BatchId、Dispatch 和 Skip。它不提供通用 result、origin、policy、execution control 或 invocation 包装。

实现约束：

- `Dispatch()` 只执行一次。
- `Skip()` 幂等，并在实例内部重新检查当前阶段。
- 自然完成和 skip 都最终收敛到 `TryComplete()`。
- Non-blocking 实例在 `Dispatch()` 返回前必须提交最终可存档逻辑状态。
- 异步呈现失败不得遗留未观察异常或长期活动引用。

## 7. GameView 与 Engine

`IGameView` 只负责一条 primitive：

- 查找 entry definition。
- 规范化参数。
- 创建 instance。
- 调用一次 `Dispatch()`。
- 返回 instance 给 Engine。

`GameEngine` 负责：

- 读取当前节点和 Group 条目。
- 判断条件。
- 保存活动 instance 队列、sequence 和 group execution id。
- 选择 skip batch。
- 订阅 blocking 完成并触发内部 continue。
- 处理 Choice 和条件分支。
- 更新稳定快照。

`IGameView` 不保存活动队列，不推进 Group，不处理 Choice，不决定 skip。

## 8. Advance 规则

一次玩家 `AdvanceAsync` 只对调用开始时最早 blocking instance 所属 batch 拥有 skip 权限。

如果该 batch 被解除阻塞，Engine 可以继续分发同步和 non-blocking 内容，直到遇到新的 blocking instance、Choice 或剧情结束；新遇到的 blocking batch 本次不会被 skip。

如果当前 blocking batch 没有任何可跳过实例，本次 Advance 不穿透它。

Blocking 自然完成只触发内部 continue，这条路径没有 skip 权限。

## 9. Dialogue 与 `\skip`

`dialogue.text` 使用一个 `DialoguePrimitiveInstance` 管理两个阶段：

```text
Typing -> WaitingAdvance -> Completed
```

规则：

- `\skip` 是不可见控制语法。
- 正常打字经过 `\skip` 不暂停。
- Typing 阶段收到 skip，只立即显示到下一个 `\skip` 或文本结尾。
- 到达文本结尾后进入等待阶段，不自动完成。
- WaitingAdvance 阶段收到下一次 skip 才完成实例。
- `\\` 表示字面反斜杠。
- 普通文本和富文本复用同一个指令识别器。

## 10. Animation 与 Effect

Animation 是普通 primitive instance，不再由 Avalonia 页面决定剧情批次。

`animation.animate`：

- 以一个 `AnimationRequest` 表示单属性动画。
- `Blocking` 与 `Skippable` 来自参数。
- `Dispatch()` 先把最终值写入可存档 runtime state，再启动 presenter 动画。
- `Skip()` 请求 presenter 立即完成并完成 instance。

`animation.play`：

- 一个 plan 对应一个 `AnimationPlanPrimitiveInstance`。
- 整个 timeline 是一个 instance 和一个 BatchId。
- plan 内部事件只支持模块私有的 layer/effect 事件处理，不作为嵌套 Entry 重新分发。
- `Dispatch()` 先提交最终 runtime state，再按时间通知 presenter。

`effect.apply` / `effect.stop`：

- 更新 `SceneState.ActiveEffects`。
- 同步维护目标 Layer 的 effect instance id 列表。
- 调用 `IEffectPresenter` 启动或停止平台呈现。
- Effect 参数仍对 Runtime 不透明；可动画数值保存在 `EffectInstance.AnimationValues`。

## 11. 稳定快照

快照不要求活动队列为空。只有同时满足下列条件时才更新最后稳定快照：

1. 当前剧情游标已提交到可恢复边界。
2. 当前没有未完成 blocking instance。
3. 当前没有 pending Choice。

未完成的 non-blocking 呈现实例不会阻止快照；它们的异步部分只能更新可重建的展示状态。读档只恢复数据，不恢复活动 Task 或 instance。

## 12. 明确不引入的公共抽象

当前模型不引入：

- Handler registry。
- Primitive descriptor。
- CreatesCheckpoint 元数据。
- Primitive invocation / origin / context 包装。
- Primitive dispatch result。
- Primitive execution control。
- OperationManager。
- `interaction.choice` primitive。
- View 端活动队列或页面级 skip batch。

如果后续确实需要其中某个能力，必须先写明不可替代的使用场景并修订设计，不能为了迁移旧代码默认恢复。

## 13. 当前实现状态

已验证：

- Entry module 两张表冻结，重复注册失败。
- `PrimitiveEntry.BatchId`、`PrimitiveEntryDocument.BatchId` 和 instance BatchId 闭环。
- `GameEngine.AdvanceAsync` 是唯一玩家入口。
- Engine 拥有活动队列、Batch skip、Choice、条件和快照。
- Dialogue/typewriter、layer、animation、effect、flow.wait、variable.set 已接入 instance 模型。
- Avalonia 页面不再提供页面级 animation batch skip。

仍留给后续 feature：

- 音频、视频、粒子、画廊的完整产品级运行行为。
- 资源类型模块化。
- 更完整的 animation timeline authoring 与内部事件能力。
