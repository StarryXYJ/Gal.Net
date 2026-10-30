# 运行时参考


## 职责边界

`GalNet.Runtime` 负责解析宿主提供的 Graph 与已编译 `.galgroup` 内容、驱动故事流程、维护运行时状态、调度 primitive instance 和创建存档。具体 UI、渲染资源、平台线程和文件系统由宿主组合根提供；Runtime 不引用 Avalonia、Skia 或具体存储实现。

```text
Graph + .galgroup
  -> GraphLoader / GalgroupLoader
  -> GameEngine
  -> IPrimitiveDispatcher.Dispatch
  -> PrimitiveInstance
```

Runtime 只执行 primitive entry。Composite entry 只能存在于 `.rawgalgroup`，必须在编译阶段展开为 primitive envelope。

## GameEngine

条件与变量表达式共用 NCalc 求值入口；双引号字符串按 JSON 转义规则读取，单引号字符串保留 NCalc 语法，未知变量读取为 null。非法条件按 false 处理并记录 `expression.invalid`（包含运行位置）；同一求值器对相同非法条件只报告一次，最多记录 64 个条件，避免循环刷屏。作者侧验证仍直接报告表达式错误。可选展示失败由对应内置模块降级，必需执行/状态错误继续传播；音频诊断订阅者异常不能阻止播放清理。

`GameEngine` 持有编译图索引、`IGameRuntime`、`IPrimitiveDispatcher`、可选 `IChoicePresenter`，以及当前活动的 `PrimitiveInstance` 队列。对玩家或 UI 只暴露一个推进入口：

```csharp
public Task<bool> AdvanceAsync(CancellationToken cancellationToken = default);
public GameSnapshot CreateSaveData();
public Task RestoreFromAsync(GameSnapshot data, CancellationToken cancellationToken = default);
public Task StopAsync();
public ValueTask DisposeAsync();
```

一次 `AdvanceAsync()` 的顺序是：

1. 清理已完成实例。
2. 在稳定边界更新最后稳定快照。
3. 如果存在 pending Choice，直接返回。
4. 如果调用开始时已有未完成 blocking instance，选择分发顺序最早者所属 batch，只 skip 该 batch 中当前已分发且可跳过的实例。
5. 如果当前 blocking 已解除，继续按顺序消费剧情，直到遇到新的 blocking instance、Choice 等待或剧情结束。

长推进每批让出执行门，重新取得门后复核运行 scope 和阻塞边界；调用方取消仅结束本次推进，保留当前有效运行，不发布 Faulted。批次预算不是内容长度上限。`CheckpointCreated` 在执行门外发布，每个订阅者收到独立快照，订阅者失败不影响剧情。

恢复在执行门内先准备隔离的 `GameRestorePoint`，验证变量、状态表、图位置与模块语义，随后才取消旧 scope 并提交；验证失败保留当前运行。已准备的状态表直接用于提交，不二次反序列化。

GameSession 替换已有运行时先通过旧运行的 ValidateSnapshotAsync 校验候选存档，通过后才停止旧运行；无效存档不会释放当前运行。实际新运行仍自行验证和恢复，不能把预检结果当作任意运行的授权。呈现恢复失败发生在切换之后，正常报告故障，不声称已经回滚平台资源。

手动模式下 Blocking instance 自然完成时，Engine 只执行无 skip 权限的内部 continue；它不是第二个玩家入口，也不能跳过下一个新遇到的 batch。自动/快速模式在有效推进边界等待全部 `IsAutoBlocking` 解除，再计时推进；不会通过模拟点击执行。

### 状态通知和停止

默认 Avalonia 宿主在恢复/Seek 的中间 `Ready` 通知中保持当前会话状态；准备就绪不表示游戏终止，不触发返回标题页。

玩家推进、播放计时、追帧、Step/Seek 的常规收尾共用状态捕获与门禁释放方法，释放后才发布通知；只需释放门禁的只读/准备步骤仍保持独立。追帧与调试回放共用现有推进内核，停止目标和逻辑副作用策略由各自入口明确决定。

`State` 和 `StateChanged(GameEngineStateChange)` 统一报告 `Ready`、`Running`、`Completed`、`Faulted`、`Stopped`；失败通知附带原始异常。自然阻塞完成、Choice 结果/异常和玩家推进都经过同一状态机制，宿主不需要分别推断终态。通知在执行锁外按发生顺序发布，重复状态不重复通知；订阅者异常只诊断，不改变剧情状态。事件处理器不应同步等待 Engine 的异步操作；UI 线程切换、日志文案和存档槽刷新由宿主处理。

`StopAsync()` 是该 Engine 的终态操作：立即拒绝新推进，取消执行 scope，清理活动实例和 pending Choice，并等待内部 continuation 退出和已读进度 FlushAsync 完成后报告 `Stopped`。Choice 后端即使没有协作取消，Engine 的等待也能退出；平台资源本身仍由宿主释放。停止后推进返回 `false`，不能恢复该 Engine；重开使用新 Engine。正常完成后再次推进不会重放已经结束的内容。未停止时 `RestoreFromAsync()` 取消旧 scope 并重新进入 `Ready`。

Engine 同时支持同步和异步释放；异步宿主优先使用 `await using`。释放 Engine 不释放借用的 `IPrimitiveDispatcher`：先停止/释放 Engine，再异步释放执行 view，最后释放 presenters、媒体后端及资源。不新增通用 Runner 或要求平台实现继承宿主基类。

## Batch 与活动实例

### 可选调试会话

宿主显式 `AttachDebugging(GameDebugOptions)` 获取借用的 DebugSession。查询与变量修改经过同一执行门，返回独立 DTO；提交按执行代次、作用域、UID、类型与原值重查，数值解析使用固定文化与有限 Float。Pause 阻止玩家/自动/自然继续，Choice 已产生的结果可延后到 Resume；不表示媒体时钟完整冻结。

调试会话每 Group 保存最近自然入口的单份逻辑场景，不存变量或执行任务。指定路线定位验证真实后继与模块 Scene/Ignore 能力，恢复场景根并无条件追帧到目标前，保留当前变量且不重复写已读；普通读档清空入口缓存并推进代次，调试重建不覆盖基线。Stop/读档会取消旧定位，宿主先停止 Engine，再释放调试 binding。查询/追帧/通信尚未由 Runtime 自动启用。

入口缓存默认 LRU 限制 256 个 Group、64 MiB 序列化 payload，`MaxEntranceGroups/MaxEntrancePayloadBytes` 可调整，0 关闭；初始运行场景单独保留。查询包括 payload 预算和最近追帧耗时/指令数；不预跑任意插件或授予未经证明的固定场景。前跳从活动实例 CatchUp 收束后的场景捕获基线；回跳从入口捕获场景重建。

`GameDebugSpan` 表示匹配作者映射提供的 primitive 半开范围。断点在范围起点前，单步不跨出范围且保留正常阻塞/Skip；定位的 `TargetEnd` 指定一次目标条件豁免范围，离开后恢复普通条件。Engine 不解析作者 ID 或网络协议。`InitialScene` 是调用方显式选择的独立测试起点，不自动把不可达位置视为正常剧情路线。

Runtime 本地变量表归一化作用域前缀；向 IVariableService 回写已有变量时按 UID 保留桥接服务中的原键，避免同一 Player 系统变量生成两个存储别名。默认 Avalonia 运行停止/释放后刷新玩家变量；Save 仍由正常保存链写入。

### 追帧到当前 Group 末尾

`GameEngine.SkipToGroupEndAsync(bool ignoreConditions = false, CancellationToken cancellationToken = default)` 复用现有 CatchUp 展示压缩与清理机制，消费当前 Group 的剩余条目，不受未读文本限制。默认执行条件和逻辑写入；显式忽略条件只绕过 Entry 条件，不跳过变量赋值或参数/资源校验。

完成时游标仍在当前 Group、EntryIndex 等于条目数，Manual 模式，不执行出边或呈现后继 Choice；下一次显式 Advance 才离开。没有运行中的 Group 时不做操作。异步展示先清理，最终场景同步完成后任务返回；不支持 CatchUp 的阻塞扩展仍等待其自然完成，可通过 CancelCatchUp/停止取消。相同追帧请求共享正在执行的任务，不同追帧目标或策略同时请求明确拒绝。此能力不增加玩家控制接口或 Sample 按钮，原 SkipToUnread 行为保持。

`PrimitiveEntry.BatchId` 是编译后的可选局部分组字段。GameView 创建实例时把它原样传入 `PrimitiveInstance.BatchId`，Engine 实际按 `(GroupExecutionId, BatchId)` 匹配 skip 批次：

- 同一次 Group 执行内，同 batch 的 blocking 与 non-blocking 实例可以一起 skip。
- 不同 Group 或同一 Group 的下一次进入即使复用相同字符串，也不会互相合批。
- 空 `BatchId` 表示实例独立成批。
- skip 只发送给当前 `IsSkippable == true` 且尚未完成的实例；实例自己的 `Skip()` 仍必须幂等并重新判断阶段。

`IPrimitiveDispatcher` 不保存活动队列，不选择 skip batch，也不推进剧情。活动队列、分发顺序、完成事件订阅、清理与内部 continue 都由 Engine 管理。

## Choice 与控制流

条件分支、Choice、edge 映射和节点跳转是 Engine 内置控制流，不注册为 primitive。

Choice 节点由 Engine 求值并过滤可见选项，再通过 `IChoicePresenter.ChooseAsync()` 显示文本并取得“可见选项索引”。Choice 等待期间：

- 不创建 `PrimitiveInstance`。
- 不拥有 BatchId。
- 不响应 Advance skip。
- 不持有 Engine 调度门等待 UI。

选择完成后，Engine 在同一个调度门内校验索引、映射回原 outlet、跳转并继续到下一个边界。

## PrimitiveInstance

每次 primitive 调用都会创建独立的 `PrimitiveInstance`：

```csharp
public abstract bool IsBlocking { get; }
public abstract bool IsSkippable { get; }
public string? BatchId { get; }
public bool IsCompleted { get; }
public virtual Task PresentationCompletion { get; }
public void Dispatch();
public void Skip();
```

约束：

- `Dispatch()` 对同一实例只执行一次。
- `IsCompleted` 只从 `false` 变为 `true`。
- `IsBlocking` 和 `BatchId` 在实例创建后不改变。
- `IsSkippable` 可以随实例阶段改变。
- `Skip()` 不保证完成，但必须幂等。

实例可以内部持有 Task、取消源、动画游标或文本游标；这些都是运行期状态，不进入内容或存档格式。

`PresentationCompletion` 默认已完成；异步 presenter 原语覆写为包含 finally 清理的任务。逻辑 `IsCompleted` 不代表后端和旧回调已经清理完。Engine 进入追帧时先切换已有实例的模式并等待展示任务，再继续执行和最终重建；非 Engine 所有的恢复实例由宿主同样跟踪、压缩并等待。`StateRestoreContext.Start` 在 Dispatch 前登记恢复出来的动画/视频实例；`Own` 登记效果、粒子、图层和音频的异步清理。会话按逆序执行全部清理并汇总错误；取消等待不丢弃所有权。恢复循环计划不重复通知已提交的首帧事件。

单次异步操作可继承 Core 的 `AsyncPrimitiveInstance`：稳定完成任务在创建时存在，`OnStarting` 处理同步状态，`ExecuteAsync` 处理操作/清理，`HandleExecutionFailure` 默认传播失败，返回 true 可显式处理错误；未处理的 OperationCanceledException 发布取消任务。默认非阻塞、不可跳过；Quick/CatchUp 沿用基础分发，可由具体实例重载。异步结果先于逻辑完成通知发布，Engine 收到通知时能读取失败。Effect/Particle 共用的 Builtins 默认实现保留记录警告、scope 取消完成和 CatchUp 只更新状态的策略；截图保留错误传播和 CatchUp 无操作。

Dialogue/NVL 使用 Builtins 的 `TextAdvancePrimitiveInstance` 共用打字、等待推进和完成状态机。PresentationCompletion 结束只表示正文已经处理，逻辑实例仍等待玩家推进；打字中 skip 揭示全文，等待中 skip 完成这一行。Quick 保留推进等待，初始 CatchUp 只记录历史。具体展示操作和失败诊断通过重载提供，状态与完成任务每次调用独立。

`SynchronizePresentationAsync` 与追帧展示协调在 Engine 执行门内调用；回调及其等待的清理任务不得重新进入或等待 Engine 推进/模式操作，否则会形成循环等待。同步使用该代隔离的 runtime 和 run token；调用者 token 只控制进入同步门，不作为持久播放的生命周期 token。

玩家间隔配置为 `PlaybackOptions.AutoAdvanceIntervalMilliseconds` 与 `QuickAdvanceIntervalMilliseconds`（int，默认 2000/50），Runtime 以毫秒计时并在更新入口拒绝负值。Normal/Quick/CatchUp 分发与 `IsAutoBlocking`、状态事件、`IAdvanceWaitInstance` 分别表达呈现策略、自动等待和全文后推进等待；自动模式不会把对白等待下一次推进误当作仍在打字。完整阅读成功使用 group ID + 稳定编译条目 ID 记录，progress v2 与 compiled v3 拒绝旧格式，不迁移。

## Entry Module 与 GameView

契约和执行独立注册：

- `IEntryModule` 导出 primitive 参数 schema 和编译期 composite，不创建执行服务。
- `IPrimitiveModule.Register(PrimitiveRegistry)` 在 view 组合时登记完整指令 ID 到实例工厂，持有游戏会话共享状态；注册表检查 schema/DTO 与重复 ID，组合结束或失败后冻结。没有逐指令 implementation 包装对象。
- `GameRegistration` 组合两者；`BuildCatalog` 提供编译契约，`BuildView` 冻结注册并转移执行模块所有权。
- 同前缀可多次注册不同指令；重复完整 ID、实现无对应 schema 都会报错。
- `AddBuiltins` 是主内置入口；`AddBuiltinSchemas` 和各领域 `AddBuiltin*` 可独立选择。注册不等于提供平台能力，具体 presenter/host 由宿主传入。

`CompositeGameView.Dispatch()` 查找完整 ID，按 schema 规范化参数并执行语义验证，构造 `PrimitiveCreateContext`，调用工厂、验证 BatchId，最后分发实例。泛型 `Register<T>` 在工厂入口解码独立 Options，复杂实例可以直接消费 context 与 Options；Immediate / Async 保留短回调入口。缺失实现抛错，不返回 null；Engine 初始化时检查 Graph 所需的全部 primitive 实现。普通分发失败也不会被忽略或推进该条目的游标。

Engine 不拥有 view。宿主先释放 Engine，再 `await view.DisposeAsync()`，最后释放借用的 presenter 和后端；view 逆序释放全部执行模块并汇总错误。内置选择模块通过 RegisterDeclared 按传入 catalog 过滤贡献，已选指令仍须通过最终契约的 schema/DTO 校验。未转移的模块由 registration 释放。

## 执行内部协作者

primitive 和呈现恢复只借用 `Core.Runtime.IGameContext`：游戏位置只读，允许变量/场景操作和显式 EndGame，不提供游标修改、快照或读档方法。Engine 持有的 `Runtime.Runtime.IGameRuntime` 才拥有这些运行管理能力，准备恢复使用同程序集的 GameRestorePoint；旧 scope 在状态同步边界内检查取消，不能在恢复提交后写入新代变量。

执行上下文没有 Settings 属性。Engine 以 Core.Execution.PlaybackOptions 的不可变值保存自动/快速推进配置，宿主通过 UpdatePlaybackOptionsAsync 一致更新；无效间隔在提交前拒绝，不更改当前配置或使运行 Faulted。音频模块借构造参数获取独立的轨道定义副本，以 AudioConfiguration 接收已验证的 Bus/Track 声明，以 AudioMixOptions 接收主音量与分别按 Bus/Track ID 索引的混音值；ApplyMix 复制集合后更新已有及后续 voice，不在每次播放时读取完整玩家模型。Avalonia.GameHost.Settings.GameSettings 只是默认宿主的偏好模型，映射到上述输入，项目不必继承它。窗口、文本和保存设置由各自宿主/呈现服务消费。SettingsContainer、SettingsSection、无消费者的 SaveSettings 和旧 SfxChannelCount 已删除；并发数量只由每轨 MaxVoices 表达。

`IPrimitiveDispatcher`、IChoicePresenter、ICatchUpPresentation 和 IPlayerPlaybackController 位于 Core.Execution，描述分发、状态验证、Choice 输入、快进同步和玩家播放控制，与具体 presenter/UI 无关；CompositeGameView 是分发协议的组合实现。音频后端端口位于可选 Contracts.Builtins.Audio，Runtime 不引用 Presentation.Abstractions 或内置场景/音频契约。`EngineNotifications` 统一按产生顺序排队状态、播放模式和检查点通知，在执行门外发布并隔离每个订阅者。活动实例列表本身保持分发顺序，不维护额外排序或自动阻塞镜像集合。

运行图在 Engine 构造时验证并复制到私有导航索引；外部修改作者图、条目参数或边不会改变已创建的运行。重复节点/出口、缺失根/边目标和非法出口在启动时拒绝；没有后继边仍表示正常结束，合法循环不因批次预算被拒绝。

Runtime 的变量读取和变量集合快照均返回独立 Variable，稳定 UID 保留，写入必须经过 SetVariable。模块使用的 StateTable/可选 runtime.Scene() 仍返回同一持久记录，必须在 State.Update 内修改；这是执行模块内部的共享数据边界，不是隔离快照。GameState 不公开锁对象，跨层同步使用 Read/Update，编码后的 Capture 才是可传递的隔离状态。

Engine 保持唯一执行门禁、运行取消和终态通知。ActivePrimitiveRegistry 拥有实例顺序、group execution identity、事件订阅、完成实例清理与 skip batch；GameGraphNavigator 在启动时索引编译图，运行期间拓扑保持稳定。AudioRuntime 保留轨道、票据、队列溢出、音量和 SEffect 逻辑；AudioBackendDispatcher 负责逐 voice 命令顺序、待加载 start 取消、能力降级和异步工作 drain，并共享 AudioRuntime 的锁。同步后端终态回调的清理在提交完成后取得同一锁，且纳入销毁等待。


状态表是持久事实的唯一来源；Layer、EffectInstance、ParticleEmitterInstance 在此处是中立数据/动画属性对象，不包含播放任务。AnimationPlaybackInstance 等停止控制只存在于派生运行索引。读取活动数据后修改属性必须在 `State.Update` 中完成，展示/异步操作放在同步边界外。当前保存仍恢复检查点时刻的状态；检查点之后自然结束的声音不会反向改写已冻结的历史快照。

内置场景的唯一句柄由状态契约显式声明 keySpace="builtin.scene"，写入与恢复使用同一通用跨表身份检查。可选 Primitives.Builtins.BuiltinScene 按 GameState 根提供 Scene()，持久对象来自上述索引，临时动画控制只属于该根。读取旧 scope 不能解析到读档后的新状态。音频运行、后端调度与票据移入 Primitives.Builtins.Audio，混音配置与后端端口位于 Contracts.Builtins.Audio。
