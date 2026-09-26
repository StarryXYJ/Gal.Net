# Review：Entry 模块与 PrimitiveInstance 运行时收敛

日期：2026-09-22  
结论：pass-with-follow-up

## 审核范围与证据

- 读取 feature、设计和 Phase 计划，并检查 Core Entry/Primitive、Presentation GameView、Runtime Engine、推荐 Builtin 模块及其定向测试。
- 全仓搜索确认产品代码已经没有 `EntryHandler`、`HandlerRegistry`、`DispatchPolicy`、`ExecutionControl`、`OperationManager`、`PrimitiveInvocation`、`PrimitiveResult`、`CreatesCheckpoint`、`SkipNextBatchAsync` 或 `StepAsync` 的引用。命中的仅为明确说明旧方案已废弃的设计文档，及未在本 feature 中维护的历史服务分析文档。
- `GameEngine` 是活动队列、局部 batch、Choice 等待、自然 continue 和快照的唯一所有者；`CompositeGameView` 只解析、创建、验证并分发单条 instance。`PrimitiveEntryDocument`、`PrimitiveEntry`、`PrimitiveCreateContext` 与 `PrimitiveInstance` 的 `BatchId` 已闭环。
- 常规构建在 Avalonia 11.3.2 的 `AvaloniaStatsTask` 因 sandbox 无权枚举 `C:\Users\Starry\AppData\Local\AvaloniaUI\Licensing\Tickets\v2` 中止；以 `-p:UsedAvaloniaProducts=` 跳过该遥测 task 后，相关项目编译成功。随后 `dotnet test test/GeneralTest/GeneralTest.csproj --no-build --no-restore -m:1 -v:minimal` 通过 199/199 项。

## 已修复：AnimationRequest 残留 BatchId 的第二真源

`BatchId` 已是 Engine 调度和 `PrimitiveInstance` 的专属字段，但 `AnimationRequest` 仍保存并由工厂赋值同一个可变字段：

- `src/GalNet.Core/Scene/AnimationRequest.cs`
- `src/GalNet.Primitives.Builtins/BuiltinRuntimeActions.cs`

这与“整个动画作为一个 instance、调度批次只由 instance 表达”的目标矛盾，并允许 presenter 请求和 instance 的 batch 在未来分歧。本轮已从 `AnimationRequest`、其工厂和测试移除该字段；presenter 不再携带 Engine 的批次决策。

## P2：EntryDefinition 是可控的兼容投影，但仍有可收敛空间

`EntryDefinition.Parameters`、`Defaults` 与 `Options` 目前由 `EntryBase.Parameters` 在建 catalog 时生成，`DynamicParameters` 仍保留同一张 schema 的引用。因此这不是第二个可写真源，也没有阻塞目标架构。

但 `EntryDefinition` 的公开构造函数和 `TargetProfileEntryCatalog.Create()` 继续传播旧的 `Entry.Values` 字符串形态。后续应把它收窄为 Editor 的只读投影或改为直接投影 `DynamicParameterTable`；这需要同时迁移 Editor 命令和 Composite authoring，不应在本轮把无关文件拆分混入。

## P2：未实现媒体 schema 当前会静默成为 no-op

`BuiltinAudioModule`、`BuiltinVideoModule` 和 `BuiltinParticleModule` 仍经 `BuiltinEntrySchemas.Primitive` 的默认工厂创建 `ImmediatePrimitiveInstance`。这符合本 feature 暂不扩展音频、视频和粒子产品行为的范围，但运行时挂载这些 schema 时会静默完成而非报告“尚未实现”。在对应能力 feature 落地前，需明确是否将它们仅保留在 editor target profile，或改为显式诊断；不应借本 feature 擅自定义媒体行为。

## 补充（2026-09-24）：Gallery 已有显式过渡行为

`BuiltinGalleryModule` 已不再是静默 no-op：当前实现校验非负 sequence ID，通过注入的 `IGameProgressService` 写入玩家级 Gallery 进度，缺少服务时明确抛出错误。该行为满足现阶段 primitive instance 的可观察执行要求。

后续已确认的 Gallery 目标设计不继续扩充这套专用 progress 集合，而是引入字符串类型 catalog、稳定 item ID 与系统 Player bool，并由 Avalonia 自主消费 Gallery 数据。该目标现已在 Phase 5-8 实现：旧 progress Gallery 集合已删除，Editor authoring、预览/导出和默认 CG/视频/音频 Avalonia 页面已接入。此处保留原审核记录作为时间线事实，最新验证证据以 phase plan 为准。

## P2：AnimationPlan 的取消源生命周期需要单独收敛

`AnimationPlanPrimitiveInstance` 持有 `CancellationTokenSource`，构建分析仍报告 CA1001。当前正常 presenter 路径会在异步观察结束时释放它，但无 presenter 的立即完成路径没有对应释放，也未形成可供 Engine 统一调用的实例清理契约。这个问题不影响本轮的 batch 语义；应在动画生命周期的专门小阶段中，先明确 instance 是否需要可释放的内部资源，再统一设计 Engine 的清理调用，避免仅为消除警告而在 Skip/自然完成竞争处引入新的释放竞态。

## P2：历史服务分析文档仍提及旧执行模型

`docs/design/services-analysis.md` 仍描述 `EntryHandler` 和 `GameEngine.StepAsync`。它不作为本 feature 的实现依据，但在 Phase 4 收尾时应更新或标记为历史，避免读者重新引入已移除的术语。

## 不构成问题的项

- `PrimitiveArgumentHelper` 将 authoring 的 `batchId` 提升到编译后的通用字段，且在运行期参数 JSON 中移除，符合设计。
- Composite Entry 继续使用 `Entry.Values` 作为 editor/compiler authoring 形态，未进入 Runtime；这不是旧的 Handler/Operation 执行路径。
- 公共类型仍有若干集中在同一文件。该问题可读性优先级较低，且在共享 Core API 的拆分会扩大 diff；本轮不以风格理由重构。
