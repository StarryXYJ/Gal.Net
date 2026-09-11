# 运行时参考

## 职责边界

`GalNet.Runtime` 负责读取 Graph 与已编译 `.galgroup`、驱动故事流程、维护运行时状态、执行原语条目和创建存档。具体 UI 由 `IGameView` 提供；Runtime 不引用 Avalonia 或文件选择器。

```
Graph + .galgroup
  → GraphLoader / GalgroupLoader
  → GameEngine
  → EntryHandlerRegistry
  → IGameView（宿主呈现）
```

`.galgroup` 必须声明 `kind: "Compiled"`，且仅含有原语条目；`GalgroupLoader` 会拒绝 `.rawgalgroup` 的 `Raw` 文档和非原语。`GameEngine` 也会拒绝被程序直接注入的非原语，保证 Runtime 不承担内容编译职责。

## GameEngine

`GameEngine` 持有 Graph、`IGameRuntime`、`IGameView`、`EntryHandlerRegistry` 与 `TimeProvider`。标准构造函数从 Graph、View 和可选的 `ITextResolver` 创建 `GameRuntime`；恢复场景可传入已有 Runtime 与 View。

```csharp
public Task<bool> StepAsync(CancellationToken ct = default);
public GameSnapshot CreateSaveData();
public void RestoreFrom(GameSnapshot data);
```

`StepAsync()` 从当前位置继续执行，直到游戏结束或到达需要玩家交互的边界。返回 `false` 表示已结束或等待交互；引擎在交互边界触发 `CheckpointCreated(GameSnapshot)`。宿主应保留该快照用于存档，而不是从 View 读取状态。

执行顺序为：找到当前节点 → Group 中按顺序执行满足 `condition` 的条目 → 经边转移 → 处理 Choice/Condition Branch → 重复。每个条目由 `EntryHandler.ExecuteAsync(context, view, timeProvider, ct)` 执行；阻塞行为由 Handler 和条目参数决定。

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

`GameSnapshot` 包含 `NodeId`、`EntryIndex`、变量字典与 `SceneState`。`SaveManager` 使用缩进的 camelCase JSON 序列化或反序列化快照。`RestoreFrom()` 会恢复这些状态并重新开始运行；呈现层的恢复由宿主/运行流程按场景状态完成。

## 场景实例与动画

图层由 `SceneInstanceManager` 按稳定 `handleId` 管理。`layer.show` 创建或更新 Layer，`layer.hide` 删除它并使句柄立即失效，`layer.replace` 保留 transform、z 和 display mode。

`animate` 目标是 `AnimatableSceneInstance` 的一个浮点属性。当前 Layer 支持位置、旋转、双轴缩放和不透明度。曲线由内容 JSON 的 `AnimationCurveDefinition` 解析为 `IAnimationCurve`，再通过 `AnimationRequest` 发给 `ILayerView.AnimateAsync()`；完成或跳过后才提交目标属性。详细参数及曲线格式见 [条目类型](entry-types.md)。

## 呈现端口

`IGameView` 聚合以下能力：

- `ITypewriterView`：开始与跳过文本逐字显示；
- `IInteractionView`：等待玩家推进与选择；
- `ILayerView`：显示、移动、替换、隐藏及动画图层；
- `IAudioView`、`IVideoView`、`ITransitionView`、`IEffectView` 与 `IControlView`：其他可见/可听请求。

`CompositeGameView` 可把各端口组合为一个 View。`NullGameView` 立即完成异步请求，适用于测试与无界面宿主；实际 Avalonia 游戏页由宿主实现端口并负责 UI 线程切换。

## 内置处理器

`EntryHandlerRegistry.CreateDefault()` 注册文本、图层、动画、音频、视频、对话框、效果、等待和变量原语的处理器。`CreateDefault(IGameProgressService?)` 在提供进度服务时还注册 `unlock_gallery`。非原语不会注册 Handler；它们必须在进入 Runtime 前由 `GalgroupCompiler` 展开。条目定义、参数和默认值的权威参考在 [条目类型](entry-types.md)。

Handler 处理无效句柄、无法解析的参数或无法执行的呈现请求时应记录诊断并安全失败；宿主不应依赖异常来处理普通内容错误。
