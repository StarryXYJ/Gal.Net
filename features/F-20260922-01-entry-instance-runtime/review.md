# Review：Entry 模块与 PrimitiveInstance 运行时收敛

日期：2026-09-22  
结论：blocked（目标设计已收敛，当前实现未完成）

## Blocker

### B1：当前工作树无法构建

`GameEngine` 使用 `GameRuntime`，但缺少对应命名空间引用，导致 `GeneralTest` 构建出现 3 个编译错误。当前状态不能视为任何 Phase 已完成交付。

证据：

- `src/GalNet.Runtime/Engine/GameEngine.cs:31`
- 验证命令：`dotnet build test/GeneralTest/GeneralTest.csproj --no-restore -m:1 -v:minimal -p:UseSharedCompilation=false`

## P1

### P1-1：BatchId 的双阶段归属还没闭环

当前 `PrimitiveInstance` 已有 `BatchId`，但 `PrimitiveEntry` / `PrimitiveEntryDocument` 没有通用 `BatchId`。`CompositeGameView` 从 `PrimitiveInstance.BatchId` 取批次，而 transition 只把 batchId 放进具体 animation 参数。这只满足了运行期实例侧，尚未满足编译调用数据侧：Composite Entry 仍无法以统一方式给任意多个原语分一批或多批，批次语义反而被下推到每种 PrimitiveInstance 的参数解释中。

建议：BatchId 同时落在编译后的 `PrimitiveEntry`/`PrimitiveEntryDocument` 和创建后的 `PrimitiveInstance` 上。编辑器或 Composite 提供可选值，编译器持久化，loader 还原，`PrimitiveCreateContext` 原样传给工厂；空值表示实例独立成批。Engine 只在同一 GroupExecutionId 内比较 BatchId。

证据：

- `src/GalNet.Core/Entry/Entry.cs`
- `src/GalNet.Core/Serialization/GroupDocument.cs`
- `src/GalNet.Presentation.Abstractions/View/CompositeGameView.cs:56`

### P1-2：唯一 Advance 语义没有落到真实入口

`IGameView.Advance()` 只有在已有 Blocking 实例时才 Skip；没有 Blocking 时直接返回 false，不能执行“继续游戏下一步”。与此同时 `GameEngine` 仍公开名为 `SkipNextBatchAsync()` 的入口，并由 `StepAsync` 独立推进剧情。这不是对话确认的单一 Advance 状态机。

建议：只保留玩家可调用的 `GameEngine.AdvanceAsync`。它清理完成实例并检测稳定快照；若存在既有 Blocking，只对最早 Blocking Batch 发出一次 Skip，解除后可以继续步进到下一个阻塞点，但不得 Skip 新遇到的 Batch。自然完成使用无 Skip 权限的内部 continue，不冒充玩家 Advance。

证据：

- `src/GalNet.Presentation.Abstractions/View/CompositeGameView.cs:84`
- `src/GalNet.Runtime/Engine/GameEngine.cs:59`

### P1-3：运行时契约引入了未确认的额外概念

`PrimitiveInvocationOrigin`、`PrimitiveContext`、`PrimitiveInvocation`、`PrimitiveResultStatus`、`PrimitiveResult` 都不属于本轮确认的最小模型。其中 `Origin` 只被 Engine 用于区分 checkpoint，模块工厂没有实际消费；它把 Engine 的来源/存档判断泄漏到每个原语工厂。`PrimitiveResult` 又让基础实例不再是“完成 bool + Dispatch/Skip”的最小状态。

建议：删除 `PrimitiveInvocationOrigin`。工厂只接收创建实例真正需要的数据（定义/参数表、编译 primitive/参数、Runtime、Scope cancellation 和 BatchId）。稳定快照完全由 Engine 的统一边界判断更新，不保留 primitive checkpoint 标记。条件、Choice、edge 映射和 Jump 全部保留为 Engine 内置控制流，Choice 只通过窄展示端口取得可见索引，不创建 `interaction.choice` instance，因此也不需要通用 `PrimitiveResult`。

证据：

- `src/GalNet.Core/Primitives/PrimitiveContracts.cs:13`
- `src/GalNet.Runtime/Engine/GameEngine.cs:82`

### P1-4：Choice 被错误地下沉为模块 primitive

当前 `GameEngine.ProcessBranchAsync` 创建 `interaction.choice` 的 `PrimitiveEntry`，再从通用 `PrimitiveResult` 取选择索引。这让 Engine 的 Graph 控制流依赖一个可替换模块，模块缺失、返回格式变化或 Batch 调度变化都可能破坏节点跳转，也正是基础实例被迫携带 Result 的主要原因。

建议：Engine 自己求值、过滤选项和映射 edge；独立 Choice 展示端口只显示已解析文本并返回可见索引。等待期间记录 Engine flow blocker，不进入 PrimitiveInstance 队列、不响应 Advance Skip，也不在持有调度门时等待 UI。

证据：

- `src/GalNet.Runtime/Engine/GameEngine.cs:128`
- `src/GalNet.Runtime/Engine/GameEngine.cs:139`
- `src/GalNet.Runtime/Engine/GameEngine.cs:144`

### P1-5：Primitive 工厂上下文没有把参数 schema 和 BatchId 作为一等输入

设计要求 primitive 定义持有参数描述表，并由工厂使用参数表、参数 JSON、Runtime 和 BatchId 构造实例。当前 `PrimitiveCreateContext` 只包含 `PrimitiveInvocation` 和 Scope cancellation；参数表只能靠工厂闭包捕获，BatchId 也只能靠具体参数或 instance 自己决定。这会让“PrimitiveEntryBase.Parameters 是唯一 schema 真源”的约束变弱，也容易让不同 primitive 以不同方式解释批次。

建议：`PrimitiveCreateContext` 显式包含当前 `PrimitiveEntryBase` 或 `DynamicParameterTable`、编译后的 `PrimitiveEntry`/arguments、Runtime、Scope cancellation 和解析后的 BatchId。工厂只负责按该上下文构造实例，不再需要读取额外 Descriptor 或自定义 `batchId` 参数来决定调度批次。

证据：

- `src/GalNet.Core/Primitives/PrimitiveContracts.cs:35`
- `src/GalNet.Core/Entry/EntryModule.cs:82`
- `src/GalNet.Presentation.Abstractions/View/CompositeGameView.cs:53`

### P1-6：活动队列仍在 IGameView，与最终职责分配相反

最终设计已经确认 `IGameView` 只负责一条 primitive 的定义解析、参数绑定、实例创建和 Dispatch；活动实例、GroupExecutionId、Sequence、Skip、清理与继续调度全部由 Engine 拥有。当前 `CompositeGameView` 仍保存 `_active`、执行 `Advance()` 并提供 `WaitForBlockingClearAsync()`，Engine 仍以等待 View 的方式推进。

建议：把活动记录和批次选择整体迁入 Engine。Blocking 完成由 Engine 订阅并排入内部 continue；NonBlocking 完成不唤醒，只在下一次 Engine 步进、Group 结束或 Dispose 时清理。GameView 返回刚创建并已 Dispatch 的 instance 即结束职责。

证据：

- `src/GalNet.Presentation.Abstractions/View/CompositeGameView.cs:10`
- `src/GalNet.Presentation.Abstractions/View/IGameView.cs:10`
- `src/GalNet.Runtime/Engine/GameEngine.cs:119`

### P1-7：参数描述表还没有形成共享绑定入口

设计已确认每个 Entry 的 `Parameters` 属性是唯一参数描述来源，并由无状态 helper 完成默认值、类型和约束处理。当前编译器、EntryDefinition 投影和运行分发仍有各自的数据形态，GameView 也尚未根据 `PrimitiveEntryBase.Parameters` 生成规范化 Arguments。

建议：在 Core 建立只读取 `EntryBase.Parameters` 的共享 helper；Compiler 和 GameView 复用，Loader 只恢复 JSON 结构。删除 Descriptor、Defaults、Options 中可独立漂移的参数副本，或明确它们只是按需生成的只读投影。

### P1-8：Animation 的 BatchId 仍藏在 plan 参数中

目标设计把整个动画时间线视为一个 primitive instance，编辑器提供的可选 `batchId` 应提升到 `PrimitiveEntry.BatchId` 并传入该 instance。当前 `AnimationPlanDefinition.BatchId` 和 transition 自定义参数仍自行保存批次，通用 `PrimitiveEntry` 没有该字段。

建议：编译时把 animation/transition 的 batchId 提升到通用字段；整个 plan 只创建一个 instance。通用时间线嵌套 Entry 明确不属于本 feature，现有内部事件不得重新进入 Entry Module。

## P2

### P2-1：文件名与主要类型不对应，多个公共类型被堆在同一文件

新增/重写部分没有遵守“一文件一主要类型”：

- `PrimitiveContracts.cs` 含 8 个公共类型。
- `EntryModule.cs` 含 8 个公共类型。
- `Entry.cs` 含 8 个公共类型。
- `BuiltinEntryModules.cs` 含 11 个模块/辅助类型。
- `GroupDocument.cs` 含 5 个公共序列化类型。
- 多个 `*Entries.cs` 把若干彼此独立的公共 Entry 类压成单行或集中在一个文件。

建议：公共、可独立引用的类型拆成同名文件；只有私有嵌套类型或只服务当前类的实现细节留在同文件。相近类型通过目录和命名空间组织，不通过大杂烩文件组织。

### P2-2：Phase 1/2 需要按新增退出条件重新验证

Phase 1/2 已按本轮复审回退为 in-progress。后续不能只复用此前“22 个定向用例通过”的结论；必须覆盖工厂上下文字段、编译数据 BatchId、实例 BatchId、唯一 Advance 入口和无 Blocking 继续剧情后，才能重新标 verified。

### P2-3：正式文档仍保留旧 Handler / Operation 设计叙述

`docs/design/primitive-module-runtime-design.md` 和 `docs/spec/runtime.md` 仍大量描述 `EntryHandlerRegistry`、Handler、Dispatch Policy、ExecutionControl 或 `OperationManager`。这些文档目前不能作为本 feature 的实现依据；否则会重新引入本 feature 正在删除的中间对象。

建议：feature 实现和验证完成前，不把目标状态写进 `docs/spec/` 当作当前事实；但在 Phase 4/closeout 中必须同步正式文档，明确新模型是 Entry Module + PrimitiveInstance，而不是 Handler/Operation 双轨。

证据：

- `docs/design/primitive-module-runtime-design.md`
- `docs/spec/runtime.md`

### P2-4：动态 Skip 的原子阶段判断尚未落到实现

目标设计允许具体 instance 使用锁或等价原子状态机，并要求 `Skip()` 内再次判断阶段。当前代码只在外部读取 `IsSkippable` 后调用 `OnSkip()`，尚未保证 Dialogue 自然推进与 Advance 同时发生时不会跨错阶段。

建议：补“Dialogue 自然切换阶段与 Advance 同时发生”的竞态测试。

### P2-5：旧 CreatesCheckpoint / PrimitiveDescriptor 应整体删除

`CreatesCheckpoint` 是旧模型中按 primitive 类型触发 checkpoint 的元数据，当前仍包装在 `PrimitiveDescriptor` 中。目标设计已经改为 Engine 在所有稳定边界统一更新快照，因此该标记和 Descriptor 包装都没有保留理由。

## 已符合的部分

- 模块确实拥有独立的 Primitive/Composite 两张只读表。
- `DefaultPrimitiveEntryBase` 使用构造传入的 `Func` 创建独立实例。
- `PrimitiveInstance` 已经暴露 `BatchId`，符合“调度从实例读取批次”的方向。
- `PrimitiveInstance` 的 Blocking 固定、Skippable 可动态变化、Completed 单调完成的方向基本正确。
- `CompositeGameView` 已实现“找到最早 Blocking Batch，并 Skip 同批全部当前可跳过实例（包含 NonBlocking）”的核心筛选逻辑。
- 快照以“无未完成 Blocking 实例”为必要条件的方向正确；NonBlocking 呈现不应强制等待队列清空。

## 收敛顺序

1. 先将 Phase 1/2 状态回退，并修复构建。
2. 删除 invocation origin/result、PrimitiveDescriptor、CreatesCheckpoint 等旧公共抽象，恢复最小 PrimitiveInstance 契约。
3. 建立只读取 `EntryBase.Parameters` 的共享参数 helper，补齐工厂上下文。
4. 把编辑器传入的可选 BatchId 提升到 `PrimitiveEntry` 及序列化 DTO，再原样复制到 instance。
5. 把活动队列从 View 迁入 Engine，记录 GroupExecutionId，并按 `(GroupExecutionId, BatchId)` 做局部合批；空 BatchId 独立成批。
6. Blocking 完成通知 Engine 内部继续；NonBlocking 在后续步进、Group 结束或 Dispose 时惰性清理。
7. 把条件、Choice 和 Jump 收回 Engine，删除 `interaction.choice` primitive；使用一个 pending Choice 对象保护恢复/取消边界。
8. 将玩家入口统一为 `GameEngine.AdvanceAsync`，补局部批次、自然完成和动态 Skip 竞态测试。
9. Animation plan 整体迁移为一个 instance；不实现通用时间线嵌套 Entry 分发。
10. 按同名文件拆分公共类型后，再继续 Dialogue `\skip` 和具体模块迁移。
11. Feature 收尾时同步 `docs/design/primitive-module-runtime-design.md`、`docs/spec/runtime.md` 和 `docs/spec/entry-types.md`，删除旧 Handler/Operation 叙述或标明迁移历史。

## 验证结果

- `dotnet build src/GalNet.Runtime/GalNet.Runtime.csproj --no-restore -m:1 -v:minimal -p:UseSharedCompilation=false`：失败，`GameEngine.cs:31` 缺少 `GalNet.Runtime.Runtime` 命名空间，产生 3 个编译错误。
- `dotnet build test/GeneralTest/GeneralTest.csproj ...`：同样先出现上述 3 个 Runtime 编译错误；此外当前受限环境中的 Avalonia telemetry 写日志被拒绝，因此本轮没有得到测试执行结果。
