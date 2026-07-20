# 运行时参考

---

## GameEngine

位于 `GalNet.Runtime.Engine` 命名空间。核心状态机循环，驱动条目执行和节点转移。所有游戏状态统一由 `GameRuntime`（实现 `IGameRuntime`）管理。

### 状态机循环

```
  定位入口节点
  → 进入 Group: 逐条目执行
     → 检查条件 (condition)
     → Resolve Handler → 若阻塞: handler.Start → 循环 WaitForClickAsync / Interrupt → handler.Complete
     → 若非阻塞: handler.Start, 立即继续
     → EntryIndex++
  → 组结束: EntryIndex=0, MoveToNext() (沿边转移)
  → 进入 Branch: 选项或条件分支
     → 选项: 过滤可见选项 → 显示选项列表 → 等待选择 → 沿边转移
     → 条件: 顺序匹配条件表达式 → 沿边转移
  → 重复直到游戏结束
```

### 属性

| 属性 | 说明 |
|---|---|
| `Runtime` | 外部可访问的 `IGameRuntime` 实例 |
| `CurrentNodeId` | 当前节点 ID（快捷方式） |
| `EntryIndex` | 当前组内条目索引（快捷方式） |
| `IsRunning` | 游戏是否正在运行 |

### 事件

| 事件 | 说明 |
|---|---|
| `CheckpointCreated` | 在交互边界触发（阻塞条目开始后、分支选择前），携带 `GameSnapshot` |

### 构造

```csharp
// 标准构造：从 Graph + IGameView 创建
public GameEngine(Graph graph, IGameView view, ICultureService? i18n = null,
    SettingsContainer? settings = null,
    EntryHandlerRegistry? registry = null,
    IGameProgressService? progress = null)

// 读档恢复：使用外部已有的 IGameRuntime 实例
public GameEngine(Graph graph, IGameRuntime runtime,
    EntryHandlerRegistry? registry = null,
    IGameProgressService? progress = null)
```

- `registry` 负责 EntryHandler 的注册与查找，默认使用 `EntryHandlerRegistry.CreateDefault()`
- `progress` 用于标记已读文本和管理鉴赏解锁

### 核心方法

#### StepAsync

```csharp
public async Task<bool> StepAsync(CancellationToken ct = default)
```

运行到结束或第一个阻塞点。返回 `false` 表示游戏结束或等待用户交互。

执行流程：
1. 进入主循环，检查 `IsGameEnded`
2. 根据 `CurrentNodeId` 定位当前节点
3. 若为 Group：执行组内条目（条件判断 → handler.Start → 阻塞/非阻塞 → Complete）
4. 若为 Branch：选项分支或条件分支
5. 组结束或分支完成后沿边转移（`MoveToNext`）

#### CreateSaveData / RestoreFrom

```csharp
public GameSnapshot CreateSaveData()
public void RestoreFrom(GameSnapshot data)
```

创建/恢复存档快照。`RestoreFrom` 将 `IsRunning` 设为 true。

### 条目执行细节

```
  Start() ──→ [Running] ──→ IsCompleted? ──→ Complete() ──→ 下一句
                │                           
                └── 用户点击 → handler.Interrupt()（handler 自己决定行为）
```

- 阻塞条目：循环等待用户点击 → Interrupt → 检查 IsCompleted
- 非阻塞条目：仅调用 Start，立即继续
- 文本条目（text）：Start 后触发 `CheckpointCreated` 事件并调用 `IGameProgressService.MarkRead()`

---

## GameRuntime

位于 `GalNet.Runtime.Runtime` 命名空间。实现 `IGameRuntime` 接口，是游戏状态的**唯一来源（single source of truth）**。

### 属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `CurrentNodeId` | `string` | 当前所在节点 ID |
| `EntryIndex` | `int` | 当前组内条目索引 |
| `IsGameEnded` | `bool` | 游戏结束标志 |
| `View` | `IGameView?` | 游戏视图接口实例 |
| `I18n` | `ICultureService?` | 国际化服务实例 |
| `Settings` | `SettingsContainer` | 设置容器 |
| `SceneState` | `SceneState` | 场景状态（图层、控件、特效等） |

### 内部状态

- `VariableStore` — 变量存储（私有字段）
- `ExpressionEvaluator` — 基于 NCalc 的表达式求值器
- `Stack<(string NodeId, int EntryIndex)>` — 调用栈（支持子流程调用/返回）
- `IVariableService?` — 可选的变量持久化服务（用于编辑器预览中同步变量变更）

### 构造

```csharp
public GameRuntime(IGameView? view, ICultureService? i18n,
    string rootNodeId = "", SettingsContainer? settings = null,
    IVariableService? variableService = null)
```

- 若提供 `variableService`，则从该服务恢复 Player 和 Save 变量快照，并注册变量变更回调

### 方法

| 方法 | 说明 |
|---|---|
| `JumpTo(string nodeId, int entryIndex = 0)` | 跳转到指定节点 |
| `SetEntryIndex(int entryIndex)` | 设置条目索引 |
| `EndGame()` | 标记游戏结束 |
| `SetVariable(string name, object value)` | 设置变量值（名称可带 `player.`/`save.` 前缀） |
| `GetVariable(string name)` | 获取变量 |
| `TryGetVariable(string name, out Variable)` | 尝试获取变量 |
| `GetVariables(VariableScope scope)` | 获取指定作用域的所有变量快照 |
| `EvaluateCondition(string expression)` | 求值条件表达式 |
| `EvaluateExpression(string expression)` | 求值通用表达式 |
| `PushCallStack(string nodeId)` | 压入调用栈 |
| `PopCallStack()` | 弹出调用栈，返回 `(nodeId, entryIndex)?` |
| `CreateSnapshot()` | 创建当前状态快照 `GameSnapshot` |
| `RestoreFrom(GameSnapshot)` | 从快照恢复状态 |

---

## IGameRuntime

位于 `GalNet.Core.Runtime` 命名空间。游戏运行时状态接口，由 `GameRuntime` 实现，通过 `EntryContext` 注入 Handler。

```csharp
public interface IGameRuntime
{
    // ── 位置（只读）──
    string CurrentNodeId { get; }
    int EntryIndex { get; }

    // ── 游戏结束标志（只读）──
    bool IsGameEnded { get; }

    // ── 核心引用 ──
    IGameView? View { get; }
    ICultureService? I18n { get; }

    // ── 设置 ──
    SettingsContainer Settings { get; }

    // ── 场景状态 ──
    SceneState SceneState { get; }

    // ── 控制流方法 ──
    void JumpTo(string nodeId, int entryIndex = 0);
    void SetEntryIndex(int entryIndex);
    void EndGame();

    // ── 调用栈 ──
    void PushCallStack(string nodeId);
    (string nodeId, int entryIndex)? PopCallStack();

    // ── 变量操作 ──
    void SetVariable(string name, object value);
    Variable? GetVariable(string name);
    bool TryGetVariable(string name, out Variable variable);
    IReadOnlyDictionary<string, Variable> GetVariables(VariableScope scope);
    bool EvaluateCondition(string expression);
    object? EvaluateExpression(string expression);

    // ── 存档快照 ──
    GameSnapshot CreateSnapshot();
    void RestoreFrom(GameSnapshot snapshot);
}
```

> 变量名使用 `string` 类型，支持 `player.` 和 `save.` 前缀来区分作用域。`VariableRoute` 类型仅在 `GalNet.Core.Variable` 中定义，运行时接口优先使用简单的 `string` 参数。

---

## GameSnapshot

位于 `GalNet.Core.Runtime` 命名空间。游戏运行时快照 —— 用于存档/读档的不可变数据对象。

```csharp
public sealed class GameSnapshot
{
    public string NodeId { get; init; }
    public int EntryIndex { get; init; }
    public Dictionary<string, Variable> Variables { get; init; }
    public SceneState SceneState { get; init; }
}
```

### JSON 结构示例（camelCase 序列化）

```json
{
  "nodeId": "group_05",
  "entryIndex": 3,
  "variables": {
    "affection_alice": { "type": "int", "value": 5 },
    "route_flag": { "type": "string", "value": "alice_route" }
  },
  "sceneState": {
    "layers": [
      { "id": "bg", "assetId": "bg_classroom", "x": 0, "y": 0, "z": 0, "visible": true }
    ],
    "activeControlIds": ["default_dialogue"],
    "activeEffectIds": [],
    "activeTransition": null
  }
}
```

---

## SaveManager

位于 `GalNet.Runtime.SaveLoad` 命名空间。存档管理器 —— 序列化/反序列化 `GameSnapshot`。

```csharp
public static class SaveManager
{
    public static string Serialize(GameSnapshot data);
    public static GameSnapshot? Deserialize(string json);
    public static void SaveToFile(GameSnapshot data, string path);
    public static GameSnapshot? LoadFromFile(string path);
}
```

序列化配置：缩进输出、camelCase 命名策略（`JsonNamingPolicy.CamelCase`）。

---

## VariableStore

位于 `GalNet.Runtime.Variables` 命名空间。运行时变量存储。player 和 save 变量分开存储，通过名称前缀（`player.` / `save.`）或 scope resolver 区分作用域。

```csharp
public sealed class VariableStore
{
    // 构造函数
    public VariableStore(
        Func<string, VariableScope>? scopeResolver = null,
        Action<VariableScope, string, Variable>? onVariableChanged = null);

    // 快照
    public IReadOnlyDictionary<string, Variable> PlayerSnapshot { get; }
    public IReadOnlyDictionary<string, Variable> SaveSnapshot { get; }

    // 设置变量值（自动识别 CLR 类型并设置对应的 VariableType）
    public void Set(string name, object value);

    // 获取类型化值，不存在返回默认
    public bool GetBool(string name, bool def = false);
    public int GetInt(string name, int def = 0);
    public float GetFloat(string name, float def = 0f);
    public string GetString(string name, string def = "");

    // 尝试获取变量
    public bool TryGet(string name, out Variable variable);

    // 获取指定作用域快照
    public IReadOnlyDictionary<string, Variable> GetSnapshot(VariableScope scope);

    // 从快照恢复
    public void RestorePlayerFrom(IReadOnlyDictionary<string, Variable> snapshot);
    public void RestoreSaveFrom(IReadOnlyDictionary<string, Variable> snapshot);
}
```

### 特性

- 名称支持 `player.` 和 `save.` 前缀自动解析作用域
- 变量为封装对象（非值类型），支持 `Bool` / `Int` / `Float` / `String` 四种类型
- `Set()` 方法自动识别 CLR 类型并设置对应的 `VariableType`
- 通过 `onVariableChanged` 回调通知外部变量变更（用于编辑器预览同步）

---

## ExpressionEvaluator

位于 `GalNet.Runtime.Variables` 命名空间。基于 **NCalc** 引擎的条件/表达式求值器。

变量通过占位符 `[name]` 引用，运行时由 NCalc 参数机制动态解析。

### 支持的运算

| 类别 | 运算符 |
|---|---|
| 逻辑 | `&&` `\|\|` |
| 比较 | `==` `!=` `<` `>` `<=` `>=` |
| 算术 | `+` `-` `*` `/` |
| 括号 | `(` `)` — 改变优先级 |
| 字面量 | `true` / `false` / `null` / 字符串(单引号) / 整数 / 浮点数 |

### 方法

```csharp
public sealed class ExpressionEvaluator
{
    public ExpressionEvaluator(VariableStore store);

    // 求值条件表达式，返回 bool。空表达式 = true
    public bool EvaluateCondition(string? expression);

    // 求值通用表达式。变量通过 [name] 引用，由 NCalc 动态解析
    public object? Evaluate(string? expression);
}
```

### 变量引用

- 格式：`[var_path]`，如 `[score]`、`[player.hp]`
- 运行时由 NCalc 的 `EvaluateParameter` 事件动态解析为实际变量值
- 变量未定义 → 替换为 `null`
- 字符串字面量使用单引号（NCalc 语法），但双引号也会被自动转换为单引号

### 示例

- `[score] + 10` — 变量 score 加 10
- `[a] * [b] + [angle]` — 混合运算
- `[hp] / [max_hp] * 100` — 百分比计算
- `flag == true && score > 50` — 复合条件

---

## NullGameView

位于 `GalNet.Runtime.View` 命名空间。`IGameView` 的无界面实现，供 Headless 和测试使用。

- 所有异步方法自动完成（`Task.CompletedTask`）
- Layer/控件操作输出至控制台（Verbose 模式可追踪）
- 不依赖任何 UI 框架

---

## EntryHandlerRegistry

位于 `GalNet.Runtime.Handlers` 命名空间。EntryHandler 的注册与查找中心。

```csharp
public sealed class EntryHandlerRegistry
{
    // 注册 Handler 实例
    public void Register(EntryHandler handler);

    // 注册 Handler 工厂（每次 Resolve 创建新实例）
    public void Register(string entryType, Func<EntryHandler> factory);

    // 根据条目类型查找 Handler 实例
    public EntryHandler? Resolve(string entryType);

    // 创建默认注册表（内置所有标准 Handler）
    public static EntryHandlerRegistry CreateDefault();

    // 创建默认注册表（含进度服务，用于解锁鉴赏）
    public static EntryHandlerRegistry CreateDefault(IGameProgressService? progress);
}
```

### 内置 Handler

| 条目类型 | Handler | 阻塞? | 说明 |
|---|---|---|---|
| `text` | `TextHandler` | 是 | 打字机 + 语音 + i18n key 解析 |
| `layer.show` | `ShowLayerHandler` | 否 | 显示图层 + 可选转场 |
| `layer.hide` | `HideLayerHandler` | 否 | 隐藏图层 + 可选转场 |
| `layer.move` | `MoveLayerHandler` | 否 | 移动图层 |
| `audio.play` | `PlayAudioHandler` | 否 | 播放音频 |
| `audio.stop` | `StopAudioHandler` | 否 | 停止音频 |
| `audio.pause` | `PauseAudioHandler` | 否 | 暂停音频 |
| `audio.resume` | `ResumeAudioHandler` | 否 | 恢复音频 |
| `audio.enqueue` | `EnqueueAudioHandler` | 否 | 入队音频 |
| `video.play` | `PlayVideoHandler` | 否 | 播放视频 |
| `video.stop` | `StopVideoHandler` | 否 | 停止视频 |
| `dialogue.show` | `ShowDialogueHandler` | 否 | 显示对话框 |
| `dialogue.hide` | `HideDialogueHandler` | 否 | 隐藏对话框 |
| `effect.apply` | `ApplyEffectHandler` | 否 | 应用特效 |
| `effect.stop` | `StopEffectHandler` | 否 | 停止特效 |
| `wait` | `WaitHandler` | 是 | duration 秒，可打断 |
| `variable.set` | `SetVariableHandler` | 否 | 求值表达式并设置变量 |
| `unlock_gallery` | `UnlockGalleryHandler` | 否 | 解锁鉴赏条目（需 IGameProgressService） |

---

## EntryHandler 基类

位于 `GalNet.Core.Handler` 命名空间。条目处理器的抽象基类。

```csharp
public abstract class EntryHandler
{
    public abstract string EntryType { get; }
    public virtual bool IsBlocking => true;
    public virtual void Start(EntryContext ctx) { }
    public virtual bool IsCompleted(EntryContext ctx) => true;
    public virtual void Complete(EntryContext ctx) { }
    public virtual void Interrupt(EntryContext ctx) { }
}
```

### EntryContext

```csharp
public sealed class EntryContext
{
    public required Entry Entry { get; init; }
    public required IGameRuntime Runtime { get; init; }

    public Dictionary<string, string> Params => Entry.Values;
    public IGameView View => Runtime.View!;
    public ICultureService? I18n => Runtime.I18n;

    public string GetString(string key, string def = "");
    public bool GetBool(string key, bool def = false);
    public float GetFloat(string key, float def = 0f);
    public int GetInt(string key, int def = 0);
    public string GetText(string key, string def = "");  // 通过 I18n 解析
}
```