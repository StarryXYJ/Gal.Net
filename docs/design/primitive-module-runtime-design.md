# 原语模块化运行时设计

> 状态：in-progress。Phase 0 的通用契约、动态 profile 与参数 schema 已实现；具体平台模块、Layer/Effect GUID 内容迁移及资源模块仍按 [实施计划](primitive-module-runtime-phase-plan.md) 推进。

## 1. 背景

当前 Runtime 通过 `EntryHandlerRegistry` 按完整条目类型解析 `EntryHandler`，Handler 再调用由 `IGameView` 聚合的 `ILayerView`、`IAudioView`、`IEffectView` 等专用呈现端口。动画的活动播放、跳过和批次管理目前主要由具体呈现实现维护。这形成了两套按原语类型扩展的接口：Runtime Handler 注册和 View 专用接口。

该结构为内置功能提供了明确的静态契约，但新增原语通常需要同时扩展条目类型、Handler、呈现接口及各宿主实现。目标架构希望把所有原语执行统一为按 Game Scope 组装的模块，使推荐能力和开发者自定义能力使用同一种注册、调度、参数描述、句柄和异步执行机制。`IGameView` 保留，但职责改为顶层原语分发器，不再继承各领域的专用 View 接口。

## 2. 目标

- 原语使用点分隔 ID，例如 `layer.show`、`effect.apply`。
- `IGameView` 作为唯一的顶层原语分发入口，负责拆分完整 ID、选择模块并返回本次执行结果。
- 每个前缀对应一个 Game Scope 内的原语模块实例。
- 模块集合完全由宿主在组合期决定；Core、Runtime 和 `IGameView` 不要求任何具体原语、模块、前缀或推荐能力存在。
- 所有调用始终按完整 ID 动态查询模块；推荐模块不拥有引擎旁路，也不形成必须继承的能力层级。
- 模块可以持有本领域共享数据；单原语 Handler 保持无状态、可重入。
- 除 `IGameView` 外，现有 `ILayerView`、`IAudioView`、`IVideoView`、`IEffectView`、`IAnimationView`、`IControlView`、`IParticleEmitterView`、`ITypewriterView` 和 `IInteractionView` 均由统一模块/Handler 契约取代。
- 所有模块和命令显式注册，重复注册在启动阶段报错。
- 未注册模块、未注册命令和不存在的目标句柄在运行时安全跳过，并产生开发诊断。
- 具体原语不在 Core 实现；模块以 Handler 的 Descriptor 同时提供参数元数据和执行实现，供编译器、编辑器和 Runtime 使用同一注册事实。
- Runtime 统一管理阻塞、跳过和跨原语类型的跳过合批，不要求具体呈现实现维护剧情推进语义。
- Game Scope 内使用统一句柄管理器保存可寻址运行时对象，并通过原语完成创建、恢复、修改和删除。
- Avalonia 等宿主可以提供推荐模块 profile；未来其他宿主可以提供自己的模块实现、替换推荐 ID 的语义，或只注册自定义模块。平台适配细节不在本设计范围内。
- `PrimitiveEntry`、`NonPrimitiveEntry` 与 `GalgroupCompiler` 的编译边界继续保留；Runtime 仍只执行编译后的原语。

## 3. 非目标

- 本阶段不设计编辑器 UI、时间轴编辑器或完整模拟器。
- 本阶段不增加更多 Effect 资源类型。
- 本阶段不设计运行时程序集热加载、模块卸载或第三方不可信代码隔离。
- 本阶段不兼容旧原语 ID、旧字符串句柄、旧 `.galgroup` 或旧存档；格式在实现时一次切换，旧数据直接拒绝。
- 本阶段不解决任意跨模块写入；跨模块只通过全局句柄读取和操作公开实例。
- 本阶段不保存正在执行的 Task、跳过信号或动画中间进度；存档保存可恢复的句柄形态和稳定逻辑状态。
- 本文不规定 Unity 等未来宿主的内部命令队列、线程调度或渲染实现。
- 本阶段不取消非原语指令，也不把非原语编译逻辑移入 `IGameView` 或原语模块。
- 本阶段不把 `layer.*`、`flow.*`、`variable.*` 或任何推荐原语提升为 Core/Runtime 的隐式前提；缺失模块必须通过正常动态分发处理。

## 4. 总体结构

```text
Game Scope
  IGameView
    layer  -> LayerPrimitiveModule
      show    -> ShowLayerHandler
      hide    -> HideLayerHandler
      replace -> ReplaceLayerHandler
    effect -> EffectPrimitiveModule
      apply  -> ApplyEffectHandler
      set    -> SetEffectHandler
      remove -> RemoveEffectHandler

  HandleManager
    Guid -> RuntimeHandle

  OperationManager
    ExecutionId -> PrimitiveOperation
```

`IGameView` 负责前缀路由和模块生命周期；它只暴露 descriptor 查询和通用 `Dispatch`，不再继承、转发或暗含任何领域能力。模块负责命令路由及领域共享数据；模块内部可使用 Handler、委托或其他私有表实现命令，顶层不再维护第二份按原语路由的表。`HandleManager` 管理原语创建出的业务实例，`OperationManager` 管理尚未完成的一次原语调用，两者使用不同的身份和生命周期。Core 只拥有这些通用契约和通用原语信封，不拥有任何具体原语的实现。

这里保留 `IGameView` 名称是为了延续 Game Scope 的宿主组合入口，但它不再只是“画面接口”。包括 `variable.set`、`flow.wait`、交互等待等非视觉原语，也通过同一个分发入口执行。通用原语契约放在 Core；具体模块和宿主实现位于外层，不能要求 Core/Runtime 反向引用 Avalonia。

## 5. 原语注册与路由

### 5.1 顶层分发与两层路由

完整原语 ID 以第一个 `.` 分成模块前缀和命令：

```text
layer.show   -> module: layer  command: show
effect.apply -> module: effect command: apply
```

ID 使用 `StringComparer.Ordinal` 精确比较，不在运行时自动改变大小写。前缀和命令都不能为空；前缀采用小写规范名，命令允许保留现有 camelCase。若命令将来包含更多 `.`，只有第一个 `.` 参与模块拆分，其余部分属于模块内部命令名。

两层结构的目的不是减少字典查询，而是让同一领域的命令共享一个按 Game Scope 创建的模块实例。模块可以持有资源缓存、领域服务和宿主适配对象；Handler 仍保持无状态。`GameEngine` 不再持有另一套 `EntryHandlerRegistry`，否则会形成两次原语路由和两套注册事实来源。

`IGameView` 的目标接口形态为：

```csharp
public interface IGameView : IDisposable
{
    bool TryGetDescriptor(
        string primitiveType,
        out PrimitiveDescriptor? descriptor);

    PrimitiveDispatch Dispatch(
        PrimitiveInvocation invocation,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken);
}
```

`TryGetDescriptor` 接收完整原语 ID，并由 `IGameView` 内部完成前缀拆分和模块查询。Runtime 用它在执行前取得 Checkpoint 等静态元数据；Runtime 自身不解析前缀。

默认 `CompositeGameView` 可以继续保留，但构造参数改为 `IEnumerable<IPrimitiveModule>`，不再分别接收九个专用 View 接口。它在 Game Scope 创建时建立只读前缀字典，并作为 `IGameView` 的默认实现。模块向组合根提供自身的 frozen Descriptor 表；组合根不得另行传入一张可与模块实现分离的 primitive 表。

```csharp
public interface IPrimitiveModule : IDisposable
{
    string Prefix { get; }
    IReadOnlyCollection<PrimitiveDescriptor> Descriptors { get; }

    PrimitiveDispatch Dispatch(
        string command,
        PrimitiveContext context,
        JsonElement arguments,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken);
}
```

模块从内部命令注册派生只读 Descriptor 列表；`IGameView.Primitives` 只是模块 Descriptor 的聚合，不是第二份注册表。模块可以复用通用基类，或直接实现模块契约。`PrimitiveDispatch` 同步返回已经启动的 Completion Task 和本次调用的有效执行策略，使 Runtime 可以立即决定是否注册、等待或允许跳过；真正工作仍在 Task 内异步完成。

### 5.2 显式注册与可选推荐模块

Game Scope 初始化顺序为：

1. 创建 `IGameView`、`HandleManager` 和 `OperationManager`。
2. 创建该宿主选择启用的模块实例；空模块集合也是有效 Game Scope。
3. 显式注册模块及其内部命令；从模块的同一冻结注册记录取得 Descriptor。
4. 检查空 ID、非法 ID、重复前缀和重复命令，并验证每个 Descriptor 的 `TypeId` 与模块前缀、命令一致。
5. 冻结注册表后开始加载和执行内容。

重复注册属于开发者配置错误，启动阶段直接报错，不采用覆盖或后注册优先规则。运行期间默认不修改原语注册表；动态性只发生在组合期，并非热加载或运行中可变注册表。

`GalNet.Primitives.Builtins` 可以提供 `variable`、玩家变量、画廊等完全平台无关的具体模块。它是宿主可选引用的库，不是 Runtime 的内建集合。对于语义通常跨平台一致、但执行依赖平台的能力，可选的推荐模块包可以提供 `LayerModuleBase` 等抽象模块基类：基类拥有该模块的 descriptor 表，并把每个推荐命令映射到抽象或可覆写执行点。此类基类不放入 Core，也不是插件协议的一部分；继承它只是便利，开发者可直接实现 `IPrimitiveModule` 并定义自己的 prefix、参数和语义。

被注册的 descriptor 是支持承诺：模块不得为已公布的命令以默认 `Skipped` 代替实现。平台不支持一组推荐能力时，应不注册该模块或注册自己的不同模块；未知命令仍按本节的正常动态分发规则处理。

### 5.3 未注册命令

内容运行时遇到未注册前缀或命令时，`IGameView.Dispatch` 返回明确的 Skipped 结果：

- 不创建 Operation 或业务句柄；
- 不修改 SceneState；
- 视为立即完成并继续执行后续内容；
- 输出结构化开发诊断，并允许上层在开发/测试模式中提升为校验错误。

宽容运行不替代资源构建和编辑器校验。编辑器和编译器必须由所选 target profile 挂载模块的 descriptor 集合构建选择器与校验，不能使用全局内置 primitive 表或 Handler factory 目录；这既避免手写原语 ID，也允许开发者完全替换推荐语义。编译 profile 可以持有模块的冻结 descriptor 表以避免创建平台对象，但该表必须由同一模块实现/推荐模块基类导出，不能在组合根重新手写一份。

## 6. 原语描述与参数

模块的内部命令表导出不可变描述。Phase 0 已抽取共享的 `DynamicParameterTable`；`PrimitiveDescriptor.Parameters` 使用该表，而不是 primitive 专属参数模型：

```csharp
public sealed record PrimitiveDescriptor(
    string TypeId,
    DynamicParameterTable Parameters,
    bool CreatesCheckpoint = false);
```

`DynamicParameterTable` 是以参数名为 Ordinal key 的只读、冻结集合。每个 `DynamicParameterDescriptor` 至少有名称、运行时 `Type ValueType`、必填性、JSON 序列化的默认值和可选约束（例如资源或句柄约束）。`ValueType` 由注册模块以 `typeof(T)` 提供，方便模块内校验和未来编辑器按 CLR 类型生成 UI；它绝不写入内容、资源 metadata、存档或 pak 格式。句柄参数可以声明期望的句柄类型，使未来编辑器通过模拟当前执行位置列出已有句柄，而不是要求开发者输入字符串。

路由层向模块传递尚未转换成领域对象的 `JsonElement`。内容只保存各参数的 JSON 值；Handler 根据当前模块的 `ValueType` 将它们反序列化为领域模型并校验。descriptor 的 JSON 默认值和约束供编辑器、构建校验及 Handler 共用，但 descriptor 和某次调用的参数值是不同对象，任何运行期路径都不能改写已冻结 schema。

`CreatesCheckpoint` 取代当前 `EntryHandler.CreatesCheckpoint`。GameEngine 在真正 Dispatch 前读取该值并创建快照，避免交互 Handler 已经开始等待后才建立 Checkpoint。

一次调用使用统一信封：

```csharp
public sealed record PrimitiveInvocation(
    string TypeId,
    PrimitiveContext Context,
    JsonElement Arguments);
```

编辑器表单暂以 `Entry.Values` 的字符串表示交互，但其转换边界由 descriptor 的 `ValueType` 决定。已编译参数以 JSON 对象进入 `PrimitiveInvocation`，不会在顶层分发前丢失数字、布尔、对象和数组的原始类型。领域反序列化仍由叶子命令负责。

## 7. 句柄模型

### 7.1 基本约束

业务句柄 ID 使用编辑器创建并持久化的 GUID；内容以固定 GUID 文本格式保存，编译产物原样保留，Runtime 只负责解析和校验。所有可注册对象继承统一基类并实现 `IDisposable`：

```csharp
public abstract class RuntimeHandle : IDisposable
{
    public required Guid Id { get; init; }
    public abstract string TypeId { get; }
    public abstract void Dispose();
}
```

`HandleManager` 是 Game Scope 内的字典与生命周期容器：

```csharp
public interface IHandleManager : IDisposable
{
    void Add(RuntimeHandle handle);
    bool TryGet(Guid id, out RuntimeHandle? handle);
    bool TryGet<THandle>(Guid id, out THandle? handle)
        where THandle : RuntimeHandle;
    bool Remove(Guid id);
    IReadOnlyCollection<RuntimeHandle> GetAll();
}
```

- GUID 重复不是正常业务分支，`Add` 必须报错，不能覆盖并 Dispose 旧对象。
- 查询不到或类型不匹配时，原语安全跳过并记录开发诊断。
- `Remove` 对不存在的 GUID 幂等成功；存在时从字典移除并调用 `Dispose`。
- Game Scope 销毁时，管理器 Dispose 所有剩余句柄。
- Handler 不把句柄保存在自身字段中，每次执行均通过 `HandleManager` 查询。

### 7.2 稳定快照、存档与恢复

存档保存句柄可恢复形态，而不是平台对象、Task 或委托。句柄快照至少包含 GUID、类型、创建所需资源/参数和当前稳定参数。

Runtime 保存 `LastStableSnapshot`。它是创建时刻的独立、深拷贝数据快照，不能与可变 `SceneState`、变量字典或 Handle 对象共享引用。每次 Engine 推进一个步骤并更新当前位置后，若没有仍会改变可存档状态的 Operation，就替换它；非阻塞 Operation 完成并使 Scope 再次稳定时也执行同一检查。Save 永远持久化这份快照，不从正在推进的 Runtime 临时创建快照。稳定表示没有仍会改变可存档状态的 Operation；纯交互或纯展示等待不改变稳定状态。未稳定时可以继续使用上一个稳定快照，但不产生新的存档点。

恢复是纯数据操作，不调用原语、不创建 Operation、不触发 Checkpoint 或剧情推进。它在干净的 Game Scope 中以快照直接重建 Runtime 状态和全部 Handle；所有 Handle 注册完成后，引用关系按 GUID 自然可查询，因此没有类型恢复顺序。Avalonia 等宿主在渲染时读取 Layer 状态和其 Effect ID，再从 `HandleManager` 查询 Effect；平台缓存不进入存档，也不需要恢复阶段主动应用。

恢复和渲染必须避免观察到半个状态：实现要么原子替换状态快照，要么在同一渲染/调度线程完成恢复。具体快照 DTO 由后续实现设计确定，但平台对象不进入存档。

## 8. Effect 原语

首轮 Effect 只需要三个基于句柄的一次性操作：

```text
effect.apply   创建 EffectHandle 并关联效果
effect.set     修改已有 EffectHandle 的参数
effect.remove  删除并 Dispose EffectHandle
```

目标 Layer 持有其 Effect 的唯一持久化 GUID 列表；列表顺序就是该 Layer 的渲染顺序。`EffectHandle` 只保存效果定义和参数，不重复保存 Target Layer ID 或目标顺序。`effect.apply` 先向 `HandleManager` 注册 Effect，再把其 ID 写入目标 Layer；`effect.remove` 先删除 Layer 引用，再移除并 Dispose Handle。全局后处理同样使用独立的有序 Effect ID 列表。目标不存在、类型不匹配或 Effect 句柄不存在时安全跳过；恢复时悬挂 ID 记录诊断并清理。

`apply`、`set`、`remove` 默认立即完成、非阻塞、不可跳过；需要随时间变化的 Effect 参数由动画原语驱动，不在 Effect 原语内部引入时间轴。

## 9. 异步执行、阻塞与跳过

### 9.1 Handler 模型

所有 Handler 使用统一的 `Task` 执行模型。立即原语返回已完成 Task，异步原语在 Task 中完成内部等待。Handler 必须无状态、可重入；每次调用的局部状态存在于该次异步调用、业务句柄或模块共享数据中。

```csharp
public interface IPrimitiveHandler
{
    PrimitiveDescriptor Descriptor { get; }

    PrimitiveDispatch Dispatch(
        PrimitiveContext context,
        JsonElement arguments,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken);
}

public sealed record PrimitiveDispatch(
    PrimitiveDispatchStatus Status,
    PrimitiveExecutionPolicy Policy,
    Task<PrimitiveResult> Completion);

public enum PrimitiveDispatchStatus { Accepted, Skipped }
public enum PrimitiveResultStatus { Succeeded, Failed }

public sealed record PrimitiveResult(
    PrimitiveResultStatus Status,
    JsonElement? Value)
{
    public static PrimitiveResult Empty { get; } = new(PrimitiveResultStatus.Succeeded, null);
}

public sealed record PrimitiveExecutionPolicy(
    bool Blocking,
    bool Skippable,
    string? BatchId);
```

`Dispatch` 本身同步返回，不表示工作同步执行。Handler 在方法内启动并返回一个 Task，所有实际异步等待仍发生在该 Task 中。立即原语使用 `Task.FromResult(PrimitiveResult.Empty)`；未知命令或同步参数校验失败返回 `Skipped` 状态和已完成 Task，已被 Handler 接受的调用返回 `Accepted` 状态。已接受调用的预期失败由 `PrimitiveResult.Status = Failed` 表示，而不是向 Engine 抛出异常。

`PrimitiveExecutionPolicy` 只描述本次 Operation 的已解析行为：`Blocking` 决定 Engine 是否等待，`Skippable` 决定是否参与跳过，`BatchId` 决定可跳过 Operation 的合批。它不是 Descriptor 的默认值。Handler 可以固定它、从 Descriptor 暴露的参数解析它，或为自身不变量强制修正它；返回后 Policy 不再改变，Runtime 只据此创建 Operation。

大多数原语返回 Empty。统一的可选结果用于当前并非 Entry 的引擎交互，例如 Choice 分支可以通过内部 `interaction.choice` 调用获得选项索引，从而不再要求 `IGameView` 同时暴露 `IInteractionView.WaitForChoiceAsync`。这些引擎内部调用使用同一分发协议，但不会被写入 `.galgroup`。

`CancellationToken` 表示 Game Scope 销毁、程序终止或执行取消。用户跳过是“立即推进到最终状态”，不能与取消共用语义。

内容参数、目标句柄和可预期宿主失败统一记录结构化诊断并以 `Skipped` 或 `Failed` 正常完成，Engine 继续推进；只有 Scope 取消需要传播。非阻塞 Operation 同样观察、记录并移除其失败，不遗留未观察异常。

### 9.2 单次执行控制

Runtime 为每次调用创建独立控制对象：

```csharp
public sealed class PrimitiveExecutionControl
{
    public Task SkipRequested { get; }
    public bool RequestSkip();
}
```

`RequestSkip` 必须线程安全且幂等，可以由 `TaskCompletionSource.TrySetResult` 实现。可跳过 Handler 等待正常完成条件或 `SkipRequested`；收到跳过后应用最终状态、完成清理并让 Completion Task 正常结束。立即完成逻辑同样必须幂等。

### 9.3 OperationManager

每次尚未完成的调用由 Runtime 记录为独立 Operation：

```csharp
public sealed record PrimitiveOperation(
    Guid ExecutionId,
    long Sequence,
    string TypeId,
    string BatchKey,
    bool Blocking,
    bool Skippable,
    Task Completion,
    PrimitiveExecutionControl Control);
```

`ExecutionId` 只标识一次调用，不等同于业务 `RuntimeHandle.Id`。同一个无状态 Handler 可以同时产生任意数量的独立 Operation。

- `Blocking = true`：Runtime 注册 Operation 后等待 `Completion`，再执行下一条原语。
- `Blocking = false`：Runtime 注册 Operation 后立即推进；OperationManager 继续持有该操作，直到完成或 Scope 被取消。
- `Skippable = true`：Operation 可以响应用户跳过，无论其是否阻塞。
- OperationManager 必须观察非阻塞 Task 的异常，并在 Task 结束后移除记录。
- 需要结果的调用由发起方保留 `Task<PrimitiveResult>`；OperationManager 只需以基类 `Task` 跟踪其完成、跳过和异常。

## 10. 跳过合批

跳过合批从具体呈现实现上移到 Runtime 的 `OperationManager`，并跨原语类型生效。

每个 Operation 有单调递增的 `Sequence` 和 `BatchKey`：

- 显式提供 `batchId` 时，`BatchKey` 使用该值。
- 未提供 `batchId` 时，为本次调用生成唯一值，因此默认单独成批。
- 一次用户跳过选择 `Sequence` 最小的活动可跳过 Operation。
- 向所有 `Skippable = true` 且 `BatchKey` 相同的活动 Operation 发出跳过请求。
- 等待该批 Operation 的 `Completion` 全部完成，确保最终状态、时间轴末端事件和清理已提交。
- 同一批可以包含不同模块和不同原语，例如 Layer 动画与 Effect 参数动画。

```csharp
public async Task<bool> SkipNextBatchAsync()
{
    var candidate = ActiveOperations
        .Where(operation => operation.Skippable)
        .OrderBy(operation => operation.Sequence)
        .FirstOrDefault();

    if (candidate is null)
        return false;

    var batch = ActiveOperations
        .Where(operation => operation.Skippable &&
                            operation.BatchKey == candidate.BatchKey)
        .ToArray();

    foreach (var operation in batch)
        operation.Control.RequestSkip();

    await Task.WhenAll(batch.Select(operation => operation.Completion));
    return true;
}
```

`Blocking` 只决定剧情流程是否主动等待，不影响 Operation 是否参加跳过合批。非阻塞、可跳过的多次同原语调用会分别注册 Operation，并可通过相同 `batchId` 一次完成。

## 11. 编译层边界

非原语指令继续保留在 Core 的编译层：

```text
.rawgalgroup
  -> 作者指令目录创建通用 PrimitiveEntry 或 NonPrimitiveEntry
  -> NonPrimitiveEntry.Compile 展开
  -> GalgroupCompiler 验证所有产物均为 Primitive
  -> .galgroup
  -> Runtime / IGameView 只分发 Primitive
```

`transition.*` 等非原语仍可维护自己的参数、编辑语义和展开逻辑，但不能注册 Runtime Handler，也不能绕过编译器进入 Runtime。动画计划中的嵌套事件同样必须在编译/加载阶段验证为原语。

具体原语不再由 Core 的静态 `EntryRegistry` 实现或注册。模块注册同时贡献 Handler Descriptor 和延迟创建 Handler 的工厂；Descriptor 目录由注册记录直接构建，不要求编辑器/编译器实例化平台 Handler。编辑器/编译器只读取该目录以生成表单、校验 JSON 并产出通用原语信封；Game Scope 用同一注册记录创建 Handler。这样新增原语只需新增模块注册，不需要修改 Core，也不存在两份原语 schema。

非原语仍有独立的作者指令目录和编译器，但其产物只是携带 `TypeId + JSON Arguments` 的通用 PrimitiveEntry。编译成功不保证当前宿主加载了全部模块；运行时未注册原语仍按安全跳过处理。

现有原语中 `text`、`wait`、`animate`、`unlock_gallery` 没有点分隔前缀，无法进入统一两层路由。目标命名应统一为类似：

```text
dialogue.text
flow.wait
animation.animate
gallery.unlock
```

目标架构只保留带模块前缀的规范 ID；不提供旧 ID 别名或兼容读取。

## 12. 执行流程

```text
读取已编译原语
  -> GameEngine 向 IGameView 查询完整 ID 的 Descriptor
  -> 在稳定时按 Descriptor 创建并保存 Checkpoint（如需要）
  -> Runtime 创建 ExecutionControl
  -> GameEngine 调用 IGameView.Dispatch
  -> IGameView 按第一个 '.' 解析 prefix 与 command
  -> IGameView 查找模块
  -> 模块查找无状态 Handler 及其 Descriptor
  -> Handler 解析参数并返回 PrimitiveDispatch
  -> OperationManager 注册未完成调用
  -> Blocking 时等待 Completion；否则继续剧情
  -> 用户推进时 OperationManager 跳过最早可跳过批次
  -> Task 完成后收敛最终状态并移除 Operation
```

业务句柄的增删改查发生在 Handler 内，并由 `HandleManager` 承担存储与释放。Operation 结束不自动删除业务句柄；只有对应 remove/stop 原语或 Game Scope 释放才结束业务句柄生命周期。任一会影响持久化状态的异步 Operation 完成后，才可产生新的稳定快照。

普通 Group 条目、动画时间轴事件以及 GameEngine 发起的内部交互都必须走同一个 `IGameView.Dispatch`。时间轴事件可以禁止创建 Checkpoint，但不能再直接 Resolve 另一套 Handler Registry，否则跳过合批、诊断和 Operation 跟踪会出现旁路。

## 13. 性能约束

- 模块与命令字典在 Game Scope 启动后冻结，执行期只读。
- Handler 可作为无状态实例复用，不按每条原语反射创建。
- JSON 只解析为 DOM 一次；具体领域模型只在对应 Handler 中转换。
- 每帧渲染和持续动画更新不经过原语字典调度；原语只负责启动、修改或停止长期对象。
- 非阻塞 Task 必须由 OperationManager 统一观察，避免遗失异常和长期保留已完成 Operation。
- 两次字典查询不是预期性能瓶颈；资源加载、反序列化和渲染仍应独立缓存和度量。

## 14. 与当前架构的关系

目标架构将当前 `EntryHandlerRegistry + IGameView 专用端口 + 呈现端活动动画表` 调整为：

- `IGameView`：唯一的模块注册、完整 ID 查询与前缀路由入口；
- `IPrimitiveModule`：Game Scope 内的领域模块和命令注册；
- 模块内部命令表：模块私有的单原语实现；
- `HandleManager`：可存档恢复的业务实例生命周期；
- `OperationManager`：Task、阻塞、跳过和批次生命周期；
- 宿主可选 profile：Avalonia 等平台的推荐具体实现，以及开发者定义的模块集。

当前实现已删除 `EntryHandlerRegistry`、`EntryHandler` 及所有 `ILayerView`、`IAudioView` 等专用 View 接口，且不提供兼容适配层。后续模块只能经 `IGameView` 的动态 Dispatch 接入；推荐模块基类或 Builtins 模块不是 Runtime 的必需能力，也不形成静态调用入口。非原语编译接口不属于运行时执行接口，继续保留。

现有“影响场景的操作先更新稳定逻辑状态，再完成呈现”的原则继续保留。跳过必须收敛到相同最终状态，而不是简单取消 Task。

## 15. 当前实现审核与迁移影响

已完成的基础设施与后续仍需迁移的实现路径如下：

1. `GameEngine` 已直接将完整原语交给 `IGameView`；内部 Choice 也使用带结果的 `interaction.choice` Dispatch。
2. 动画时间轴事件必须走同一 `IGameView.Dispatch + OperationManager`，但关闭事件自身的 Checkpoint 创建。
4. `AvaloniaGamePageView` 当前保存活动动画并实现 `SkipAnimationBatch`。目标状态由 Runtime `OperationManager` 统一跟踪和合批，Avalonia 模块只负责让单次调用立即完成到最终画面。
5. `Entry.Values` 和 `GalgroupLoader` 当前把参数压平为字符串，并对部分 Layer 参数硬编码转换。目标调用信封使用 JSON 参数对象，这部分加载模型需要同步调整。
6. 当前 `ISceneInstanceManager`、Layer、动画、Effect、Particle 和存档字段使用字符串句柄。迁移到 GUID 时必须同时修改内容 schema、编译产物、运行时查询、快照和恢复，不能只修改管理器键类型。
7. `GameRuntime.RestoreFrom` 当前硬编码重建 Layer、Effect、Particle 和 Animation 状态。目标恢复路径应改为重建通用句柄快照和 Runtime 数据，不调用原语；宿主在渲染时按数据查询需要的 Handle。
8. `GalgroupCompiler` 已经正确区分 Primitive 与 NonPrimitive，并拒绝非原语进入编译产物。这条边界应保留，不应随运行时接口重构一起删除。

以上仍未完成的项目属于后续 Phase Plan；本文只确定目标职责，不规定提交拆分顺序。

## 16. 剩余迁移决定

1. 恢复与渲染采用原子状态替换还是同一调度线程执行。两者都满足本设计，按宿主实现选择。

以上项目不改变目标职责，可在后续 Phase Plan 中决定施工顺序。

## 17. 后续资源类型模块化与动态参数目录

本节是当前原语模块化完成后的独立后续设计，不属于本轮 Phase 的实现范围。当前 `ResourceType` 枚举、`AssetMeta.ParseResourceType()` 的字符串映射以及 `IAssetManager` 按 CLR 泛型类型查找 decoder，均是现有事实；后续会一次替换，不保留枚举/字符串映射的兼容路径。

### 17.1 通用、冻结的参数 schema

原语 descriptor、资源 metadata 以及未来其他可配置对象都应复用同一套只读参数描述表，而不是各自维护参数定义。该基础设施在当前 Phase 0 实现，资源模块在后续 Phase 6 消费它。目标模型至少包含：

```text
DynamicParameterTable
  name (ordinal key) -> DynamicParameterDescriptor
    valueType         // 模块注册时的 typeof(T)，不序列化
    isRequired
    defaultValue      // JSON 值；可为空
    constraints       // 可选的范围、枚举值、资源类型等声明
```

`DynamicParameterTable` 在模块注册完成后冻结，只向消费者暴露只读集合。它描述的是 schema；某份内容或某项资源的实际参数仍保存为独立的 JSON 对象。这样 descriptor 的 `defaultValue` 可以表达用户所说的“类型和值”，但加载、编辑或并发访问都不会改写全局定义。模块使用 `typeof(T)` 作为本进程的运行时契约；数据文件永远只存 JSON 值，由已挂载模块将它解释为 `T`，不把 CLR 类型名、程序集名或 `Type` 序列化为长期协议。现有 `PrimitiveParameterDescriptor` 和其参数类型枚举会在 Phase 0 一次收敛到公共模型，不保留平行定义。

本设计只规定 metadata 和校验契约。编辑器基于 `ValueType` 自动生成控件、复杂约束的呈现方式和自定义 UI 扩展点均不在本阶段范围。

### 17.2 资源模块与资源组合根

一个资源类型对应一个 `IResourceModule` 实例，例如 `sprite`、`audio`、`video` 或开发者自定义的 `spine`. 模块拥有：稳定的 `TypeId`、冻结的 `DynamicParameterTable`，以及把原始资源字节和 metadata 转换为该资源类型运行时对象的私有加载实现。

```text
AssetManager
  CompositeResourceCatalog
    sprite -> SpriteResourceModule (只读参数表 + loader)
    audio  -> AudioResourceModule  (只读参数表 + loader)
    spine  -> CustomResourceModule (只读参数表 + loader)
```

`CompositeResourceCatalog(IEnumerable<IResourceModule>)` 是资源系统对应 `CompositeGameView` 的组合根：宿主在组合期显式传入模块，目录以 `StringComparer.Ordinal` 冻结 `TypeId → module/descriptor` 路由，拒绝空 ID 和重复类型。`AssetManager` 保留 Provider、缓存、引用计数和取消语义，但通过该目录按 metadata 的完整 `typeId` 动态查询模块；它不再包含资源类型枚举、静态 decoder 表或别名映射。空资源模块集合和只含自定义资源模块的 profile 均有效。

资源 metadata 目标形态为稳定 GUID、`typeId` 与 `parameters` JSON 对象。未注册类型、参数校验失败或 loader 的预期资源错误必须产生诊断并安全失败，不得回退到隐式 `unknown` 或另一个内置类型。资源模块同样不支持运行期热注册；动态性只发生在宿主组合期。
