# 运行时参考

## 职责边界

`GalNet.Runtime` 负责解析宿主提供的 Graph 与已编译 `.galgroup` 内容、驱动故事流程、维护运行时状态、调度 primitive instance 和创建存档。具体 UI、渲染资源、平台线程和文件系统由宿主组合根提供；Runtime 不引用 Avalonia、Skia 或具体存储实现。

```text
Graph + .galgroup
  -> GraphLoader / GalgroupLoader
  -> GameEngine
  -> IGameView.Dispatch
  -> PrimitiveInstance
```

Runtime 只执行 primitive entry。Composite entry 只能存在于 `.rawgalgroup`，必须在编译阶段展开为 primitive envelope。

## GameEngine

`GameEngine` 持有编译图索引、`IGameRuntime`、`IGameView`、可选 `IChoicePresenter`，以及当前活动的 `PrimitiveInstance` 队列。对玩家或 UI 只暴露一个推进入口：

```csharp
public Task<bool> AdvanceAsync(CancellationToken cancellationToken = default);
public GameSnapshot CreateSaveData();
public void RestoreFrom(GameSnapshot data);
public Task StopAsync();
public ValueTask DisposeAsync();
```

一次 `AdvanceAsync()` 的顺序是：

1. 清理已完成实例。
2. 在稳定边界更新最后稳定快照。
3. 如果存在 pending Choice，直接返回。
4. 如果调用开始时已有未完成 blocking instance，选择 sequence 最早者所属 batch，只 skip 该 batch 中当前已分发且可跳过的实例。
5. 如果当前 blocking 已解除，继续按顺序消费剧情，直到遇到新的 blocking instance、Choice 等待或剧情结束。

手动模式下 Blocking instance 自然完成时，Engine 只执行无 skip 权限的内部 continue；它不是第二个玩家入口，也不能跳过下一个新遇到的 batch。自动/快速模式在有效推进边界等待全部 `IsAutoBlocking` 解除，再计时推进；不会通过模拟点击执行。

### 状态通知和停止

`State` 和 `StateChanged(GameEngineStateChange)` 统一报告 `Ready`、`Running`、`Completed`、`Faulted`、`Stopped`；失败通知附带原始异常。自然阻塞完成、Choice 结果/异常和玩家推进都经过同一状态机制，宿主不需要分别推断终态。通知在执行锁外按发生顺序发布，重复状态不重复通知；订阅者异常只诊断，不改变剧情状态。事件处理器不应同步等待 Engine 的异步操作；UI 线程切换、日志文案和存档槽刷新由宿主处理。

`StopAsync()` 是该 Engine 的终态操作：立即拒绝新推进，取消执行 scope，清理活动实例和 pending Choice，并等待内部 continuation 退出和已读进度 FlushAsync 完成后报告 `Stopped`。Choice 后端即使没有协作取消，Engine 的等待也能退出；平台资源本身仍由宿主释放。停止后推进返回 `false`，不能恢复该 Engine；重开使用新 Engine。正常完成后再次推进不会重放已经结束的内容。未停止时 `RestoreFrom()` 取消旧 scope 并重新进入 `Ready`。

Engine 同时支持同步和异步释放；异步宿主优先使用 `await using`。释放 Engine 不释放借用的 `IGameView`：先停止/释放 Engine，再异步释放执行 view，最后释放 presenters、媒体后端及资源。不新增通用 Runner 或要求平台实现继承宿主基类。

## Batch 与活动实例

`PrimitiveEntry.BatchId` 是编译后的可选局部分组字段。GameView 创建实例时把它原样传入 `PrimitiveInstance.BatchId`，Engine 实际按 `(GroupExecutionId, BatchId)` 匹配 skip 批次：

- 同一次 Group 执行内，同 batch 的 blocking 与 non-blocking 实例可以一起 skip。
- 不同 Group 或同一 Group 的下一次进入即使复用相同字符串，也不会互相合批。
- 空 `BatchId` 表示实例独立成批。
- skip 只发送给当前 `IsSkippable == true` 且尚未完成的实例；实例自己的 `Skip()` 仍必须幂等并重新判断阶段。

`IGameView` 不保存活动队列，不选择 skip batch，也不推进剧情。活动队列、sequence、完成事件订阅、清理与内部 continue 都由 Engine 管理。

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

内容 provider 将项目与安装目录的 `settings.json` 解析为 `GameContent.Settings`；两个 Sample 都把它注入 Runtime 的 `SettingsContainer`，自定义音频轨道不会退回默认轨道。缺少文件使用默认设置，非法 JSON 明确拒绝。

| 状态 | 说明 |
| --- | --- |
| `CurrentNodeId` / `EntryIndex` | 当前流程位置 |
| `IsGameEnded` | 结束标记 |
| `TextResolver` | 宿主提供的剧情文本解析器；默认原样返回 |
| `Settings` | 运行期设置容器 |
| `SceneState` / `SceneInstances` | 可存档场景快照与按句柄管理的活跃实例 |
| 变量存储 | Player / Save 作用域变量与表达式求值 |

`GameSnapshot` 当前版本为 3，旧版本直接拒绝，不提供迁移兼容。它包含 `NodeId`、`EntryIndex`、变量字典与 `SceneState`。`CreateSaveData()` 返回 Engine 保存的最后稳定快照，而不是即时抓取可能仍在异步变化的状态。所有对外快照均深复制；调用者修改保存数据或事件参数不会影响 Engine。读档取消旧 scope，并替换 SceneState/SceneInstances；旧实例保留旧场景，不能更改新运行的变量与流程，恢复时重置结束标记。

Save scope 随存档槽保存和恢复；Player scope 由宿主变量服务独立持久化，普通读档不得回滚。内置系统可以通过 `SystemVariableDefinition` 注册生成变量。Gallery 为每个 item 注册默认 `false` 的 Player bool `gallery_<item-id>_unlocked`；`gallery.unlock` 校验 item ID 后把该变量设为 `true`。

稳定快照的更新条件：

- 当前剧情游标已经提交到明确边界。
- 不存在未完成 blocking instance。
- 不存在 pending Choice。

未完成的纯 non-blocking 呈现实例不会单独阻止快照；因此 non-blocking primitive 必须在 `Dispatch()` 返回前先提交最终可存档逻辑状态。读档不恢复 Task、CancellationToken、平台控件或活动 `PrimitiveInstance`；宿主根据恢复后的 `SceneState` 重建画面。

动画按稳定语义存档，而不是保存展示层播放游标：

- `Once` 的 non-blocking 动画在分发时提交末值，读档直接显示末值；
- `Loop` / `PingPong` 保持第 0 帧逻辑状态，并在 `ActiveAnimations` 保存完整 request 或 plan，读档后从第 0 帧重新播放；
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

`PresentationCompletion` 默认已完成；异步 presenter 原语覆写为包含 finally 清理的任务。逻辑 `IsCompleted` 不代表后端和旧回调已经清理完。Engine 进入追帧时先切换已有实例的模式并等待展示任务，再继续执行和最终重建；非 Engine 所有的恢复实例由宿主同样跟踪、压缩并等待。`BuiltinPresentationReplay` 的 `onContinuation` 在 Dispatch 前交付恢复出来的动画/视频实例，避免部分重建失败时丢失跟踪。

单次异步操作可继承 Core 的 `AsyncPrimitiveInstance`：稳定完成任务在创建时存在，`OnStarting` 处理同步状态，`ExecuteAsync` 处理操作/清理，`HandleExecutionFailure` 默认传播失败，返回 true 可显式处理错误；未处理的 OperationCanceledException 发布取消任务。默认非阻塞、不可跳过；Quick/CatchUp 沿用基础分发，可由具体实例重载。异步结果先于逻辑完成通知发布，Engine 收到通知时能读取失败。Effect/Particle 共用的 Builtins 默认实现保留记录警告、scope 取消完成和 CatchUp 只更新状态的策略；截图保留错误传播和 CatchUp 无操作。

Dialogue/NVL 使用 Builtins 的 `TextAdvancePrimitiveInstance` 共用打字、等待推进和完成状态机。PresentationCompletion 结束只表示正文已经处理，逻辑实例仍等待玩家推进；打字中 skip 揭示全文，等待中 skip 完成这一行。Quick 保留推进等待，初始 CatchUp 只记录历史。具体展示操作和失败诊断通过重载提供，状态与完成任务每次调用独立。

`SynchronizePresentationAsync` 与追帧展示协调在 Engine 执行门内调用；回调及其等待的清理任务不得重新进入或等待 Engine 推进/模式操作，否则会形成循环等待。同步使用该代隔离的 runtime 和 run token；调用者 token 只控制进入同步门，不作为持久播放的生命周期 token。

玩家间隔配置为 `GameSettings.AutoAdvanceIntervalMilliseconds` 与 `QuickAdvanceIntervalMilliseconds`（int，默认 2000/50），Runtime 以毫秒计时并拒绝负值。Normal/Quick/CatchUp 分发与 `IsAutoBlocking`、状态事件、`IAdvanceWaitInstance` 分别表达呈现策略、自动等待和全文后推进等待；自动模式不会把对白等待下一次推进误当作仍在打字。完整阅读成功使用 group ID + 稳定编译条目 ID 记录，progress v2 与 compiled v3 拒绝旧格式，不迁移。

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
- `layer.*`：show/showColor/hide/move/replace，先更新 `SceneState` 再通知 `ILayerPresenter`。
- `animation.animate` / `animation.play` / `animation.stop`：创建 animation primitive instance；一次性动画先提交最终逻辑状态，循环动画提交第 0 帧状态并保存完整重放定义，再启动呈现动画。plan 是单个 instance，内部事件只作为 animation 模块私有 layer/effect 事件处理，不重新进入通用 Entry 分发。
- `effect.apply` / `effect.stop`：维护 `SceneState.ActiveEffects` 和 `IEffectPresenter` 调用，不另存冗余的 effect ID 索引。
- `audio.play` / `audio.stop` / `audio.pause` / `audio.resume` / `audio.enqueue` / `audio.next` / `audio.clear`：`AudioRuntime` 持有轨道队列与 voice 生命周期，`IAudioPlaybackHost` 只执行平台播放请求。`dialogue.textVoice` 与 `nvl.appendVoice` 会先发出非阻塞的 `audio.play(replaceIfDifferent)`，默认使用 `vocal` 轨道，再按正常文本语义阻塞。
- `audio.seffect.apply` / `audio.seffect.stop`：以 `trackId` 和 `instanceId` 附着或移除宿主 catalog 已注册的轨道 SEffect；当前 Avalonia host 支持 `seffect.gain`、基于 equalizer 频段近似的 `seffect.lowPass` 与 `seffect.highPass`。未知 ID、非法参数或不支持 scope 只记录诊断，不会静默附着。
- `particle.play` / `particle.stop`：用 `rate` 创建和停止持续 emitter，维护 `SceneState.ActiveParticleEmitters` 与 Runtime emitter instance。存档只保存 emitter 定义、排序与动画值；宿主读档后通过 `BuiltinPresentationReplay` 以相同 seed 从头重建，不保存单颗粒子。`initial` 的 lifetime、velocity、size、rotation、angular velocity、color 以及 `flipbook` 的播放速度与起始帧都按粒子确定性采样；`motion` 提供 gravity、noise、drag、radial、orbit、attractor，`lifetime` 曲线线性插值 size、opacity、velocity、rotation、color。当前粒子年龄、位置和 flipbook 帧不单独存档。
- `particle.burst`：用 `count` 向 `IParticlePresenter` 发出一次性 burst，不创建 Runtime scene state，也不参与存档恢复。有限持续效果由流程或 animation plan 编排 play/stop；重复 burst 由 animation plan 事件表达，跳过 plan 时不补发未来 burst。
- `video.play` / `video.stop`：视频是 Runtime Layer，`IVideoPresenter` 将平台解码帧送入场景管线；阻塞时每次推进跳到当前播放位置之后的下一时间戳，跳转期间保持阻塞，结束后继续剧情。稳定存档只保存视频图层定义，非阻塞视频恢复时从零播放；播放 identity 和游标不入存档。
- `flow.wait`：blocking、skippable 的等待实例。
- `variable.set`：求值后写入 Runtime 变量。
- `gallery.unlock`：按稳定 item ID 解锁 Gallery，写入对应的系统 Player bool。

视频图层与音频的轨道播放、淡化、队列和当前轨道 SEffect 行为由内置模块实现。玩家音量/静音按 master × bus × 内容 gain × 轨道默认 gain 合成并应用到当前及后续 voice；item effect、音频参数动画和音频存档恢复仍未实现，读档后不会自动续播旧音频。向前追帧的 BGM 最终投影属于会话内状态，不等于音频存档恢复。Gallery 的平台无关 catalog 与解锁数据层由 Core/Runtime 提供；默认 Avalonia 前端通过宿主提供的 `IGameGallerySession` 浏览 CG、视频和音频，不把页面或媒体状态写入 Runtime。

动画 plan 的事件与轨道共用 presenter 的播放时间源。`PlayAnimationPlanAsync` 必须在采样前 await `beforeFrame(elapsedFrames)`，正常完成前提供末帧；Loop/PingPong 的 elapsedFrames 单调增加，不在循环时归零。跳过不补发未来 particle burst。

音频后端请求按 voice 顺序提交，Stop 即刻取消尚未完成的 Start；异步销毁等待排队请求结束。Avalonia host 先登记待加载音频，停止/销毁取消加载，等待加载结束后释放 LibVLC 与临时目录。音频轨道状态不进入当前存档；读档会停止旧 scope 音频。

## 已读进度提交边界

`ReadProgressIndex` 在 Runtime 中维护内存索引与 revision。文件实现 MarkRead 不执行磁盘 I/O，默认每 2 秒批量写入；同一个进度实例串行化 flush/clear，写入期间的新标记保留 dirty 状态。后台失败由 LastPersistenceError 暴露并在下个周期重试。Engine StopAsync 在执行与 continuation 退出后等待 FlushAsync，失败仍发布 Stopped 并向调用者传播；Sample 保存前 flush，销毁进度实例时取消定时器并完成最终提交。强制终止可能丢失自上次成功 flush 以来的标记（正常 I/O 时最多一个周期，持续失败时不保证时间上限）。存档槽恢复不回滚玩家已读进度。

## 执行内部协作者

Engine 保持唯一执行门禁、运行取消和终态通知。ActivePrimitiveRegistry 拥有实例顺序、group execution identity、事件订阅、完成实例清理与 skip batch；GameGraphNavigator 在启动时索引编译图，运行期间拓扑保持稳定。AudioRuntime 保留轨道、票据、队列溢出、音量和 SEffect 逻辑；AudioBackendDispatcher 负责逐 voice 命令顺序、待加载 start 取消、能力降级和异步工作 drain，并共享 AudioRuntime 的锁。同步后端终态回调的清理在提交完成后取得同一锁，且纳入销毁等待。
