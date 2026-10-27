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

## GameRuntime 与存档

`GameRuntime` 是游戏事实的唯一来源：

内容 provider 将项目与安装目录的 `settings.json` 解析为 `GameContent.Configuration`（Core.Configuration.ProjectConfiguration）。它是版本化的作者内容文档，保存 section ID、版本和 JSON；不含固定游戏设置模型。宿主通过 GameContentLoadOptions 显式提供配置 schema 和开发语言选择委托；Storage 不引用默认 DTO。文件缺省时返回空文档，读取 section 默认值不修改内容，未知 section/字段保留，非法信封或已声明配置拒绝加载。默认模板和 Sample 选择 Contracts.Builtins 的 DefaultProjectOptions；自有项目可声明自己的 section。通用 section 编辑与完整自有 DTO 预览接入仍在实施。

宿主把轨道定义显式传入 BuiltinAudioModule，把 PlaybackOptions 传给 Engine；GameRuntime 和 IGameContext 不携带完整设置或设置容器。未配置音频模块轨道时为空，不隐式创建默认轨道。

| 状态 | 说明 |
| --- | --- |
| `CurrentNodeId` / `EntryIndex` | 当前流程位置 |
| `IsGameEnded` | 结束标记 |
| `TextResolver` | 宿主提供的剧情文本解析器；默认原样返回 |
| `State` | 按注册类型/ID 存储的通用 GameState；契约可选择 keySpace 共享跨表身份索引 |
| 变量存储 | Player / Save 作用域变量与表达式求值 |

`GameSnapshot` 当前版本为 4，旧版本直接拒绝，不提供迁移兼容。它包含 `NodeId`、`EntryIndex`、Save 变量与 `States`：稳定 StateId → section version / JSON payload。模块组合时显式注册纯数据表与恢复处理器，未注册类型、重复 ID、缺失/未知 section、版本不符及无效载荷明确报错。builtin.animations 当前 section version 为 2，LoopAnimationState 保存 Request 或 Timeline；builtin.layers 当前 section version 也为 2，新增可选的 LayerVariantsState；两者均明确拒绝旧 v1 section。PrimitiveRegistry.State 的 version 参数传递到 Core 状态契约，其他模块默认仍为 1。每份存档写出全部注册表，包括空表；不写 CLR 类型名、Task 或后端对象。

差分 Layer 保存图集资源 ID、完整请求、最后保护集合、实际变体键和解析原因，实际 sprite ID 仍以 Layer.AssetId 为真源。解码阶段检查来源互斥与状态结构，模块的同步 ValidateState 使用会话已准备的不可变 ImageVariantsCatalog 重新解析，核对实际变体/图片/原因，再允许替换状态根；内容版本变更导致不一致时拒绝读档。展示恢复还会在呈现前复核内容。定义异步加载在会话准备阶段完成，show/select 本身同步，无需改变 Engine 读档协议。恢复后继续选择仍保留曾因缺组合而未显示的服装等意图。

`CreateSaveData()` 返回 Engine 保存的最后稳定快照。状态表编码共享短同步边界，JSON 载荷和变量与运行对象隔离；不再由中央代码逐类型复制字段。读档先解码/校验候选状态与模块规则，再取消旧 scope 并替换整个 GameState；可选场景模块按新状态根解析独立场景服务。旧回调保留旧状态根，不能清理新代。展示恢复成功前暂停推进，恢复失败进入 Faulted。

Save scope 随存档槽保存和恢复；Player scope 由宿主变量服务独立持久化，普通读档不得回滚。内置系统可以通过 `SystemVariableDefinition` 注册生成变量。Gallery 为每个 item 注册默认 `false` 的 Player bool `gallery_<item-id>_unlocked`；`gallery.unlock` 校验 item ID 后把该变量设为 `true`。

稳定快照的更新条件：

- 当前剧情游标已经提交到明确边界。
- 不存在未完成 blocking instance。
- 不存在 pending Choice。

未完成的纯 non-blocking 呈现实例不会单独阻止快照；因此 non-blocking primitive 必须在 `Dispatch()` 返回前先提交最终可存档逻辑状态。读档不恢复 Task、CancellationToken、平台控件或活动 `PrimitiveInstance`；宿主通过 `StateRestoreSession` 调用模块注册的恢复处理器重建展示；处理器依赖在组合时排序并校验缺失/循环。

动画按稳定语义存档，而不是保存展示层播放游标：

- `Once` 的 non-blocking 动画在分发时提交末值，读档直接显示末值；
- `Loop` / `PingPong` 保持第 0 帧逻辑状态，并在 `LoopAnimationState` 表保存类型化 request 或 timeline，读档后从第 0 帧重新播放；
- blocking 动画运行期间不会产生新的稳定快照，因此不保存其中间帧；
- 循环粒子属性动画保存 emitter、初始属性值与动画定义，不保存单颗粒子、渲染资源或时钟。

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

## 当前内置模块

`GalNet.Primitives.Builtins` 提供可选内置模块。当前已实现运行时行为的能力包括：

- `dialogue.text`：对话与打字机阶段，支持 `\skip` 分段跳过；`dialogue.show` / `dialogue.hide` 控制显示状态。
- `nvl.show` / `nvl.hide` / `nvl.clear`：分别显示、隐藏和清空 NVL 历史；`nvl.append` 以相同的富文本和打字机语义追加一行，完成打字后等待玩家推进。NVL 不会因 append 自动显示。
- `layer.*`：show/showColor/hide/move/replace，先更新 `GameState` 的 Layer 表再通知 `ILayerPresenter`。
- `animation.animate` / `timeline.play` / `animation.stop`：创建 animation primitive instance；一次性动画先提交最终逻辑状态，循环动画提交第 0 帧状态并保存完整重放定义，再启动呈现动画。timeline 是单个 instance，内部事件只作为 animation 模块私有 layer/effect 事件处理，不重新进入通用 Entry 分发。
- `effect.apply` / `effect.stop`：维护唯一的 EffectInstance 数据表和 `IEffectPresenter` 调用；动画直接修改同一记录，不维护 ActiveEffectState 镜像。Skia effect-program 的 `@gal.effect` 元数据声明可动画参数，动画使用其逻辑参数名；`time` 可映射到常见 shader 时间 uniform。
- `audio.play` / `audio.stop` / `audio.pause` / `audio.resume` / `audio.enqueue` / `audio.next` / `audio.clear`：`AudioRuntime` 持有轨道队列与 voice 生命周期，`IAudioPlaybackHost` 只执行平台播放请求。`dialogue.textVoice` 与 `nvl.appendVoice` 会先发出非阻塞的 `audio.play(replaceIfDifferent)`，默认使用 `vocal` 轨道，再按正常文本语义阻塞。
- `audio.play` / `audio.enqueue` 的 `loopCount` 默认 1，0 为无限重复；`completion` 默认为 `discard`，`requeueTail` 在完整播放次数结束后回到队尾。单项重复优先于 completion，失败、停止、替换、next 与 scope 清理不触发重播。每轮重新生成后端 identity；这是结束事件驱动的重复播放，不承诺无缝。有限循环保存 `CompletedIterations`，旧数据缺省为 0；旧请求缺省仍为一次播放并丢弃，因此 audio state section 维持 v1。无限循环计数不累加。暂停期间结束的播放保留逻辑项，Resume 后启动新一轮。
- `audio.seffect.apply` / `audio.seffect.stop`：以 `scope`（track/bus）、`targetId` 和 `instanceId` 管理有序目标效果链。默认 PCM 后端支持 gain、真正的二阶 lowPass/highPass（cutoffHz/q）和 reverb（roomSize/decaySeconds/damping/mix），不再使用 EQ 频段近似。相同实例更新保留顺序，启停平滑；参数或目标非法则诊断。`audio.mix.set` 保存独立于玩家偏好的目标 gain/muted。
- `particle.play` / `particle.stop`：用 `rate` 创建和停止持续 emitter，维护唯一的 ParticleEmitterInstance 数据表，场景句柄查找引用同一记录。存档只保存 emitter 定义、排序与动画值；宿主读档后通过模块注册的恢复处理器 以相同 seed 从头重建，不保存单颗粒子。`initial` 的 lifetime、velocity、size、rotation、angular velocity、color 以及 `flipbook` 的播放速度与起始帧都按粒子确定性采样；`motion` 提供 gravity、noise、drag、radial、orbit、attractor，`lifetime` 曲线线性插值 size、opacity、velocity、rotation、color。当前粒子年龄、位置和 flipbook 帧不单独存档。
- `particle.burst`：用 `count` 向 `IParticlePresenter` 发出一次性 burst，不创建 Runtime scene state，也不参与存档恢复。有限持续效果由流程或 animation timeline 编排 play/stop；重复 burst 由 animation timeline 事件表达，跳过 timeline 时不补发未来 burst。
- `video.play` / `video.stop`：视频是 Runtime Layer，`IVideoPresenter` 将平台解码帧送入场景管线；阻塞时每次推进跳到当前播放位置之后的下一时间戳，跳转期间保持阻塞，结束后继续剧情。稳定存档只保存视频图层定义，非阻塞视频恢复时从零播放；播放 identity 和游标不入存档。
- `flow.wait`：blocking、skippable 的等待实例。
- `variable.set`：求值后写入 Runtime 变量；表达式非法或没有值时拒绝本次赋值、保留原值并给出定位诊断，不把失败转换为空字符串。写入/状态错误仍正常传播。
- `gallery.unlock`：按稳定 item ID 解锁 Gallery，写入对应的系统 Player bool。
- `custom.invoke`：`BuiltinCustomActionModule` 按 ordinal action ID 查注册的实例工厂，原样传入 argument。注册在 view 组合时冻结，重复/空白 ID 拒绝。`RegisterImmediate` 同步非阻塞；`RegisterAsync` 支持运行 scope/模块生命周期联合取消和可选阻塞，并通过 PrimitiveTaskScope 在异步释放时取消、排空任务和聚合错误。两种便利入口 Normal 调 execute，Quick 调 quick 或回退 execute，CatchUp 调 catchUp 或空操作；运行中模式切换不重启委托。`RegisterFactory` 返回的独立实例自行管理完整模式/跳过/完成和资源清理语义。未匹配时所有模式 warning 后同步完成，可配置严格缺失报错；处理器自身失败正常传播。模块不自动注册持久状态，也不序列化委托或资源。接入见[通用 action](../manual/custom-actions.md)。

音频实例保存资源、内容 gain、淡入淡出与完成处理；Track 管理容量、顺序/随机选择和 None/One/All 循环策略。跨平台 PCM 路径为实例 → Track 汇总/效果/音量 → Bus 汇总/效果/音量 → Master 限幅。玩家 mix 与剧情目标 mix 独立相乘，Bus 效果处理一次汇总结果，源结束后继续输出尾音。AudioTrackState/AudioSEffectState 版本为 2，新增 AudioChannelState；随机状态、队列、暂停和效果顺序可恢复。读档活动源从头开始，不保存媒体游标、包络或 DSP 缓冲；暂停源在 Resume 前不启动。配置 `galnet.project` v2 使用 audioBuses / busId。旧配置/状态版本拒绝，详见 [音频混音](../manual/audio-mixing.md)。

循环播放读档时从当前轮开头重播并保留已完成轮数；重新入队开始新一轮循环计数。ticket 随完整逻辑播放完成，轮播/无限重复需通过停止、替换或跳过终止。`ReplaceIfDifferent` 命中相同资源时更新 gain、completion 与 loopCount，次数改变时重新计数，不重启当前后端声音。

淡出项立即从逻辑播放集合移除，但 AudioRuntime 保留运行期 retiring voice，继续发送玩家音量/静音更新；retiring 不进入存档，也不占逻辑声部/队列容量。支持淡出的 host 必须在真实结束（含显式停止、淡出完成、停止尚未启动的声音）时上报对应 Ended，供运行时清理身份；scope 切换和释放会强制立即停止所有 retiring。此类结束不会重新入队。

动画 timeline 的事件与轨道共用 presenter 的播放时间源。`PlayTimelineAsync` 必须在采样前 await `beforeFrame(elapsedFrames)`，正常完成前提供末帧；Loop/PingPong 的 elapsedFrames 单调增加，不在循环时归零。跳过不补发未来 particle burst。

音频后端请求按 voice 顺序提交，目标级 Configure/Mix/Effect/Reset 另有顺序屏障，Start 等待已提交的目标配置。Stop 即刻取消待加载 Start；异步销毁等待后台源和设备回调退出。Windows miniaudio 解码至每实例有界 PCM 缓冲；设备回调不执行文件 I/O、不取得 Runtime 锁或调用终态订阅。取消后的租约和原生解码器均释放，读档清空旧图，旧 scope Reset 与迟到终态不能影响新代。Headless 保留逻辑状态；无 host 时流程 ticket 完成。

## 已读进度提交边界

存档槽、玩家变量和已读进度的文件写入共用替换工具：每次写入使用同目录下独立临时文件，成功写完并检查取消后替换目标，失败时清理本次临时文件。各服务继续负责自身的锁、数据快照和提交时机；进度 revision 只在替换成功后推进。

`ReadProgressIndex` 在 Runtime 中维护内存索引与 revision。文件实现 MarkRead 不执行磁盘 I/O，默认每 2 秒批量写入；同一个进度实例串行化 flush/clear，写入期间的新标记保留 dirty 状态。后台失败由 LastPersistenceError 暴露并在下个周期重试。Engine StopAsync 在执行与 continuation 退出后等待 FlushAsync，失败仍发布 Stopped 并向调用者传播；Sample 保存前 flush，销毁进度实例时取消定时器并完成最终提交。强制终止可能丢失自上次成功 flush 以来的标记（正常 I/O 时最多一个周期，持续失败时不保证时间上限）。存档槽恢复不回滚玩家已读进度。

## 执行内部协作者

primitive 和呈现恢复只借用 `Core.Runtime.IGameContext`：游戏位置只读，允许变量/场景操作和显式 EndGame，不提供游标修改、快照或读档方法。Engine 持有的 `Runtime.Runtime.IGameRuntime` 才拥有这些运行管理能力，准备恢复使用同程序集的 GameRestorePoint；旧 scope 在状态同步边界内检查取消，不能在恢复提交后写入新代变量。

执行上下文没有 Settings 属性。Engine 以 Core.Execution.PlaybackOptions 的不可变值保存自动/快速推进配置，宿主通过 UpdatePlaybackOptionsAsync 一致更新；无效间隔在提交前拒绝，不更改当前配置或使运行 Faulted。音频模块借构造参数获取独立的轨道定义副本，以 AudioConfiguration 接收已验证的 Bus/Track 声明，以 AudioMixOptions 接收主音量与分别按 Bus/Track ID 索引的混音值；ApplyMix 复制集合后更新已有及后续 voice，不在每次播放时读取完整玩家模型。Avalonia.GameHost.Settings.GameSettings 只是默认宿主的偏好模型，映射到上述输入，项目不必继承它。窗口、文本和保存设置由各自宿主/呈现服务消费。SettingsContainer、SettingsSection、无消费者的 SaveSettings 和旧 SfxChannelCount 已删除；并发数量只由每轨 MaxVoices 表达。

`IPrimitiveDispatcher`、IChoicePresenter、ICatchUpPresentation 和 IPlayerPlaybackController 位于 Core.Execution，描述分发、状态验证、Choice 输入、快进同步和玩家播放控制，与具体 presenter/UI 无关；CompositeGameView 是分发协议的组合实现。音频后端端口位于可选 Contracts.Builtins.Audio，Runtime 不引用 Presentation.Abstractions 或内置场景/音频契约。`EngineNotifications` 统一按产生顺序排队状态、播放模式和检查点通知，在执行门外发布并隔离每个订阅者。活动实例列表本身保持分发顺序，不维护额外排序或自动阻塞镜像集合。

运行图在 Engine 构造时验证并复制到私有导航索引；外部修改作者图、条目参数或边不会改变已创建的运行。重复节点/出口、缺失根/边目标和非法出口在启动时拒绝；没有后继边仍表示正常结束，合法循环不因批次预算被拒绝。

Runtime 的变量读取和变量集合快照均返回独立 Variable，稳定 UID 保留，写入必须经过 SetVariable。模块使用的 StateTable/可选 runtime.Scene() 仍返回同一持久记录，必须在 State.Update 内修改；这是执行模块内部的共享数据边界，不是隔离快照。GameState 不公开锁对象，跨层同步使用 Read/Update，编码后的 Capture 才是可传递的隔离状态。

Engine 保持唯一执行门禁、运行取消和终态通知。ActivePrimitiveRegistry 拥有实例顺序、group execution identity、事件订阅、完成实例清理与 skip batch；GameGraphNavigator 在启动时索引编译图，运行期间拓扑保持稳定。AudioRuntime 保留轨道、票据、队列溢出、音量和 SEffect 逻辑；AudioBackendDispatcher 负责逐 voice 命令顺序、待加载 start 取消、能力降级和异步工作 drain，并共享 AudioRuntime 的锁。同步后端终态回调的清理在提交完成后取得同一锁，且纳入销毁等待。


状态表是持久事实的唯一来源；Layer、EffectInstance、ParticleEmitterInstance 在此处是中立数据/动画属性对象，不包含播放任务。AnimationPlaybackInstance 等停止控制只存在于派生运行索引。读取活动数据后修改属性必须在 `State.Update` 中完成，展示/异步操作放在同步边界外。当前保存仍恢复检查点时刻的状态；检查点之后自然结束的声音不会反向改写已冻结的历史快照。

内置场景的唯一句柄由状态契约显式声明 keySpace="builtin.scene"，写入与恢复使用同一通用跨表身份检查。可选 Primitives.Builtins.BuiltinScene 按 GameState 根提供 Scene()，持久对象来自上述索引，临时动画控制只属于该根。读取旧 scope 不能解析到读档后的新状态。音频运行、后端调度与票据移入 Primitives.Builtins.Audio，混音配置与后端端口位于 Contracts.Builtins.Audio。


## 宿主显式变量修改

GameEngine.SetVariablesAsync 接收带 player./save. 前缀的有限基础值批次，复制并预验证输入后，通过唯一执行门及 GameState 同步边界提交；恢复或停止使已排队的旧 scope 请求失效。它不推进剧情，也不捕获未完成的呈现状态，仅把本批显式存档变量合入原稳定快照，并在执行锁外发布检查点通知。玩家变量由原变量桥接回写，持久化仍由宿主协调。变量观察者接收独立副本，异常记录日志且不截断其他更新。

UpdateVariablesAsync 可在同一门内读取独立变量快照并同步计算修改批次，计算/校验失败不提交，适合条件扣款和计数累加。该入口不提供任意异步业务事务或阻塞活动原地恢复；项目通过模块状态及明确检查点策略处理复杂行为。通过 Runtime 对象直接写入不会自动刷新 Engine 的稳定存档快照，外部应用应使用此入口。
