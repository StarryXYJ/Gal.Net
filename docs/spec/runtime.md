# 运行时参考

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
| `SceneState` | `SceneState` | 场景状态（图层、控件、特效等） |

### 内部状态

- `VariableStore` — 变量存储
- `ExpressionEvaluator` — 表达式求值器
- `Stack<(string NodeId, int EntryIndex)>` — 调用栈（支持子流程调用/返回）

### 构造

```csharp
public GameRuntime(IGameView? view, ICultureService? i18n, string rootNodeId = "")
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
    // ── 位置 ──
    string CurrentNodeId { get; set; }
    int EntryIndex { get; set; }

    // ── 游戏结束标志 ──
    bool IsGameEnded { get; set; }

    // ── 核心引用 ──
    IGameView? View { get; }
    ICultureService? I18n { get; }

    // ── 场景状态 ──
    SceneState SceneState { get; }

    // ── 变量操作 ──
    void SetVariable(VariableRoute route, object value);
    Variable? GetVariable(VariableRoute route);
    bool TryGetVariable(VariableRoute route, out Variable variable);
    bool EvaluateCondition(string expression);
    object? EvaluateExpression(string expression);

    // ── 调用栈 ──
    void PushCallStack(string nodeId);
    (string nodeId, int entryIndex)? PopCallStack();

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
  CompileAll (编译所有组) 
  → 定位入口节点
  → 进入 Group: 逐条目执行
     → 检查条件 (condition)
     → Resolve Handler → Start()
     → 若阻塞: 循环 IsCompleted / Interrupt → Complete()
     → EntryIndex++
  → 组结束: EntryIndex=0, MoveToNext() (沿边转移)
  → 进入 Branch: 选项或条件分支
     → 选项: 显示选项列表 → 等待选择 → 沿边转移
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
// 标准构造
public GameEngine(Graph graph, IGameView view, ICultureService? i18n = null)

// 读档恢复：使用外部 GameRuntime
public GameEngine(Graph graph, IGameRuntime runtime)
```

### 核心方法

#### StepAsync

```csharp
public async Task<bool> StepAsync(CancellationToken ct = default)
```

运行到结束或第一个阻塞点。

- 返回 `false` — 游戏结束
- 返回 `true` — 等待用户交互

执行流程：
1. 编译所有 Group 的复杂条目 → SimpleEntry 列表
2. 进入主循环，检查 `IsGameEnded`
3. 根据 `CurrentNodeId` 定位当前节点
4. 若为 Group：执行组内条目（条件判断 → handler.Start → 阻塞/非阻塞 → Complete）
5. 若为 Branch：选项分支或条件分支
6. 组结束或分支完成后沿边转移（MoveToNext）

#### CompileAll

```csharp
public static IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> CompileAll(Graph graph)
```

编译所有 Group 条目的复杂条目 → 简单条目内存列表。

#### CreateSaveData / RestoreFrom

```csharp
public GameSnapshot CreateSaveData()
public void RestoreFrom(GameSnapshot data)
```

创建/恢复存档快照。

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

### JSON 结构示例

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
    "activeControls": ["default_dialogue"],
    "activeEffects": []
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

序列化配置：缩进输出、camelCase 命名策略。

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
