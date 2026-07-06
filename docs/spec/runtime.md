# 运行时参考

---

## IGameGraphCompiler

位于 `GalNet.Runtime.Compilation` 命名空间。编译管道的核心抽象 —— 将 Graph 中所有 Group 的 ComplexEntry 编译为 SimpleEntry 列表。

```csharp
public interface IGameGraphCompiler
{
    IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> Compile(Graph graph);
}
```

### GameGraphCompiler（默认实现）

```csharp
public sealed class GameGraphCompiler : IGameGraphCompiler
{
    public static GameGraphCompiler Default { get; } = new();
    public IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> Compile(Graph graph);
}
```

- 遍历 Graph 中所有 `Group` 节点
- 对每个 Group，遍历其 `Entries`（ComplexEntry 列表），调用 `Compile()` 展开
- 返回 `Dictionary<GroupId, IReadOnlyList<SimpleEntry>>`
- 可通过 `GameEngine` 构造函数注入自定义实现

---

## GameRuntime

位于 `GalNet.Runtime.Runtime` 命名空间。实现 `IGameRuntime` 接口，是游戏状态的**唯一来源（single source of truth）**。

游戏运行时状态 —— 统一管理游戏的位置、变量、场景状态、调用栈。Handler 通过 `EntryContext.Runtime` 访问此实例。

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

- `VariableStore` — 变量存储（私有字段，不直接暴露）
- `ExpressionEvaluator` — 表达式求值器
- `Stack<(string NodeId, int EntryIndex)>` — 调用栈（支持子流程调用/返回）

### 构造

```csharp
public GameRuntime(IGameView? view, ICultureService? i18n,
    string rootNodeId = "", SettingsContainer? settings = null)
```

### 方法

| 方法 | 说明 |
|---|---|
| `SetVariable(VariableRoute, object)` | 设置变量值 |
| `GetVariable(VariableRoute)` | 获取变量 |
| `TryGetVariable(VariableRoute, out Variable)` | 尝试获取变量 |
| `EvaluateCondition(string)` | 求值条件表达式 |
| `EvaluateExpression(string)` | 求值通用表达式 |
| `PushCallStack(string nodeId)` | 压入调用栈 |
| `PopCallStack()` | 弹出调用栈，返回 `(nodeId, entryIndex)?` |
| `CreateSnapshot()` | 创建当前状态快照 `GameSnapshot` |
| `RestoreFrom(GameSnapshot)` | 从快照恢复状态 |

---

## IGameRuntime

位于 `GalNet.Core.Runtime` 命名空间。游戏运行时状态接口，由 GameRuntime 实现，通过 EntryContext 注入 Handler。

```csharp
public interface IGameRuntime
{
    // ── 位置（只读，控制流通过显式方法变更）──
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

    // ── 控制流方法（取代属性的可写 setter）──
    void JumpTo(string nodeId, int entryIndex = 0);
    void SetEntryIndex(int entryIndex);
    void EndGame();

    // ── 调用栈（call/return）──
    void PushCallStack(string nodeId);
    (string nodeId, int entryIndex)? PopCallStack();

    // ── 变量操作 ──
    void SetVariable(VariableRoute route, object value);
    Variable? GetVariable(VariableRoute route);
    bool TryGetVariable(VariableRoute route, out Variable variable);
    bool EvaluateCondition(string expression);
    object? EvaluateExpression(string expression);

    // ── 存档快照 ──
    GameSnapshot CreateSnapshot();
    void RestoreFrom(GameSnapshot snapshot);
}
```

---

## GameEngine

位于 `GalNet.Runtime.Engine` 命名空间。核心状态机循环，驱动条目执行和节点转移。所有游戏状态统一由 GameRuntime 管理。

### 状态机循环

```
  CompileAll (编译所有组，通过 IGameGraphCompiler)
  → 定位入口节点
  → 进入 Group: 逐条目执行
     → 检查条件 (condition)
     → Resolve Handler → 若为 jump 类型: 递归调用 StepAsync
     → 若阻塞: handler.Start → 循环 WaitForClickAsync / Interrupt → handler.Complete
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

### 构造

```csharp
// 标准构造：从 Graph + IGameView 创建，使用默认或自定义编译器和 Handler 注册表
public GameEngine(Graph graph, IGameView view, ICultureService? i18n = null,
    SettingsContainer? settings = null,
    IGameGraphCompiler? compiler = null,
    EntryHandlerRegistry? registry = null)

// 读档恢复：使用外部已有的 IGameRuntime 实例
public GameEngine(Graph graph, IGameRuntime runtime,
    IGameGraphCompiler? compiler = null,
    EntryHandlerRegistry? registry = null)
```

- `compiler` 负责编译 ComplexEntry → SimpleEntry，默认使用 `GameGraphCompiler.Default`
- `registry` 负责 EntryHandler 的注册与查找，默认使用 `EntryHandlerRegistry.CreateDefault()`

### 核心方法

#### StepAsync

```csharp
public async Task<bool> StepAsync(CancellationToken ct = default)
```

运行到结束或第一个阻塞点。

- 返回 `false` — 游戏结束或等待用户交互（当前实现始终返回 false）

执行流程：
1. 通过注入的 `IGameGraphCompiler` 编译所有 Group 的复杂条目 → SimpleEntry 列表
2. 进入主循环，检查 `IsGameEnded`
3. 根据 `CurrentNodeId` 定位当前节点
4. 若为 Group：执行组内条目（条件判断 → handler.Start → 阻塞/非阻塞 → Complete）
5. **特殊处理 `jump` 类型条目**：执行 `handler.Start(ctx)` 后递归调用 `await StepAsync(ct)`
6. 若为 Branch：选项分支或条件分支
7. 组结束或分支完成后沿边转移（`MoveToNext`）

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

- `jump` 类型条目：执行后递归调用 StepAsync
- 阻塞条目：循环等待用户点击 → Interrupt → 检查 IsCompleted
- 非阻塞条目：仅调用 Start，立即继续

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
    // 序列化存档到 JSON 字符串
    public static string Serialize(GameSnapshot data);

    // 从 JSON 字符串反序列化
    public static GameSnapshot? Deserialize(string json);

    // 保存到文件
    public static void SaveToFile(GameSnapshot data, string path);

    // 从文件加载
    public static GameSnapshot? LoadFromFile(string path);
}
```

序列化配置：缩进输出、camelCase 命名策略（`JsonNamingPolicy.CamelCase`）。

---

## VariableStore

位于 `GalNet.Runtime.Variables` 命名空间。运行时变量存储。player 和 save 共用命名空间，不可重名。

```csharp
public sealed class VariableStore
{
    // 获取所有变量快照（用于存档）
    public IReadOnlyDictionary<VariableRoute, Variable> Snapshot { get; }

    // 获取或创建设置变量值
    public void Set(VariableRoute route, object value);

    // 获取 bool/int/float/string 值，不存在返回默认
    public bool GetBool(VariableRoute route, bool def = false);
    public int GetInt(VariableRoute route, int def = 0);
    public float GetFloat(VariableRoute route, float def = 0f);
    public string GetString(VariableRoute route, string def = "");

    // 尝试获取变量
    public bool TryGet(VariableRoute route, out Variable variable);

    // 从快照恢复（读档用）
    public void RestoreFrom(IReadOnlyDictionary<VariableRoute, Variable> snapshot);
}
```

### 特性

- 变量通过 `VariableRoute`（树状键路径）标识，如 `player.affection.alice`
- 变量为封装对象（非值类型），支持 `Bool` / `Int` / `Float` / `String` 四种类型
- `Set()` 方法自动识别 CLR 类型并设置对应的 `VariableType`

---

## ExpressionEvaluator

位于 `GalNet.Runtime.Variables` 命名空间。条件/表达式求值器。

变量通过占位符 `[name]` 引用，运行时替换为字面值后求值。

### 支持的运算

| 类别 | 运算符 |
|---|---|
| 逻辑 | `&&` `\|\|` |
| 比较 | `==` `!=` `<` `>` `<=` `>=` |
| 算术 | `+` `-` `*` `/` |
| 括号 | `(` `)` — 改变优先级 |
| 负号 | `-` — 一元负号 |
| 字面量 | `true` / `false` / `null` / 字符串(引号) / 整数 / 浮点数 |

### 方法

```csharp
public sealed class ExpressionEvaluator
{
    public ExpressionEvaluator(VariableStore store);

    // 求值条件表达式，返回 bool。空表达式 = true
    public bool EvaluateCondition(string? expression);

    // 求值通用表达式。先替换 [var]，再解析求值
    public object? Evaluate(string expression);
}
```

### 变量引用

- 格式：`[var_path]`，如 `[score]`、`[player.hp]`
- 运行时替换为实际变量值（字符串字面量加引号、数字保持数值）
- 变量未定义 → 替换为 `null`

### 示例

- `[score] + 10` — 变量 score 加 10
- `[a] * [b] + sin([angle])` — 混合运算（函数预留）
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
    // 注册 Handler 类型（工厂方式，非单例）
    public void Register(string entryType, Func<EntryHandler> factory);

    // 根据条目类型查找 Handler 实例
    public EntryHandler? Resolve(string entryType);

    // 创建默认注册表（内置所有标准 Handler）
    public static EntryHandlerRegistry CreateDefault();
}
```

### 内置 Handler

| 条目类型 | Handler | 阻塞? | 说明 |
|---|---|---|---|
| `text` | `TextHandler` | 是 | 打字机 + 语音 + i18n key 解析 |
| `audio` | `AudioHandler` | 否 | play/stop/pause/resume/enqueue |
| `layer` | `LayerHandler` | 否 | show/hide/move |
| `effect` | `EffectHandler` | 否 | apply/stop |
| `control` | `ControlHandler` | 否 | show/hide/set |
| `wait` | `WaitHandler` | 是 | duration 秒，可打断 |
| `video` | `VideoHandler` | 否 | play/stop |
| `variable` | `VariableHandler` | 否 | 通过 GameRuntime 操作变量 |
| `jump` | `JumpHandler` | 否 | 节点跳转（特殊处理：递归 StepAsync） |
