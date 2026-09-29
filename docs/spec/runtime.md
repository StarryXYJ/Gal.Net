# 运行时参考

## 职责边界

`GalNet.Runtime` 负责读取 Graph 与已编译 `.galgroup`、驱动故事流程、维护运行时状态、调度 primitive instance 和创建存档。具体 UI、渲染资源、平台线程和文件系统由宿主组合根提供；Runtime 不引用 Avalonia、Skia 或具体存储实现。

```text
Graph + .galgroup
  -> GraphLoader / GalgroupLoader
  -> GameEngine
  -> IGameView.Dispatch
  -> PrimitiveInstance
```

Runtime 只执行 primitive entry。Composite entry 只能存在于 `.rawgalgroup`，必须在编译阶段展开为 primitive envelope。

## GameEngine

`GameEngine` 持有 Graph、`IGameRuntime`、`IGameView`、可选 `IChoicePresenter`，以及当前活动的 `PrimitiveInstance` 队列。对玩家或 UI 只暴露一个推进入口：

```csharp
public Task<bool> AdvanceAsync(CancellationToken cancellationToken = default);
public GameSnapshot CreateSaveData();
public void RestoreFrom(GameSnapshot data);
```

一次 `AdvanceAsync()` 的顺序是：

1. 清理已完成实例。
2. 在稳定边界更新最后稳定快照。
3. 如果存在 pending Choice，直接返回。
4. 如果调用开始时已有未完成 blocking instance，选择 sequence 最早者所属 batch，只 skip 该 batch 中当前已分发且可跳过的实例。
5. 如果当前 blocking 已解除，继续按顺序消费剧情，直到遇到新的 blocking instance、Choice 等待或剧情结束。

Blocking instance 自然完成时，Engine 只执行无 skip 权限的内部 continue；它不是第二个玩家入口，也不能跳过下一个新遇到的 batch。

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

| 状态 | 说明 |
| --- | --- |
| `CurrentNodeId` / `EntryIndex` | 当前流程位置 |
| `IsGameEnded` | 结束标记 |
| `TextResolver` | 宿主提供的剧情文本解析器；默认原样返回 |
| `Settings` | 运行期设置容器 |
| `SceneState` / `SceneInstances` | 可存档场景快照与按句柄管理的活跃实例 |
| 变量存储 | Player / Save 作用域变量与表达式求值 |

`GameSnapshot` 包含 `NodeId`、`EntryIndex`、变量字典与 `SceneState`。`CreateSaveData()` 返回 Engine 保存的最后稳定快照，而不是即时抓取可能仍在异步变化的状态。

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

## Entry Module 与 GameView

宿主把一个或多个 `IEntryModule` 挂到 `CompositeGameView`。每个模块持有两张冻结表：

- `PrimitiveEntries`：Runtime 可执行 primitive schema 与 instance 工厂。
- `CompositeEntries`：只服务编辑器和编译器的 authoring 展开。

`CompositeGameView.Dispatch()` 负责：

1. 按 `PrimitiveEntry.Type` 查找 `PrimitiveEntryBase`。
2. 使用该 entry 的 `DynamicParameterTable` 规范化参数和默认值。
3. 构造 `PrimitiveCreateContext`，其中包含 definition、规范化后的 entry、runtime、scope cancellation、arguments 与 BatchId。
4. 调用工厂创建 instance。
5. 验证 instance BatchId 与 entry BatchId 一致。
6. 调用一次 `Dispatch()` 并把 instance 返回给 Engine。

未知 primitive 返回 `null`，由 Engine 记录诊断并继续执行。

## 当前推荐模块

`GalNet.Primitives.Builtins` 提供可选推荐模块。当前已实现运行时行为的能力包括：

- `dialogue.text`：对话与打字机阶段，支持 `\skip` 分段跳过；`dialogue.show` / `dialogue.hide` 控制显示状态。
- `layer.*`：show/showColor/hide/move/replace，先更新 `SceneState` 再通知 `ILayerPresenter`。
- `animation.animate` / `animation.play` / `animation.stop`：创建 animation primitive instance；一次性动画先提交最终逻辑状态，循环动画提交第 0 帧状态并保存完整重放定义，再启动呈现动画。plan 是单个 instance，内部事件只作为 animation 模块私有 layer/effect 事件处理，不重新进入通用 Entry 分发。
- `effect.apply` / `effect.stop`：维护 `SceneState.ActiveEffects`、目标 Layer 的 effect 索引和 `IEffectPresenter` 调用。
- `particle.play` / `particle.stop`：维护 `SceneState.ActiveParticleEmitters` 与 Runtime emitter instance，并调用 `IParticlePresenter` 创建或停止渲染端 emitter。存档只保存 emitter 定义、排序与动画值；宿主读档后通过 `BuiltinPresentationReplay` 重建渲染端 emitter，不保存单颗粒子。
- `flow.wait`：blocking、skippable 的等待实例。
- `variable.set`：求值后写入 Runtime 变量。
- `gallery.unlock`：按稳定 item ID 解锁 Gallery，写入对应的系统 Player bool。

音频和视频仍保留推荐 schema；完整产品级剧情原语行为由后续 feature 或宿主自定义模块补齐。Gallery 的平台无关 catalog 与解锁数据层由 Core/Storage 提供；默认 Avalonia 前端通过宿主提供的 `IGameGallerySession` 浏览 CG、视频和音频，不把页面或媒体状态写入 Runtime。
