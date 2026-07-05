# GalNet 架构设计参考

---

## 技术栈

| 层 | 选型 |
|---|---|
| 运行时 | .NET 10 |
| UI 框架 | Avalonia 12 |
| 控件库 | Ursa 2.1 |
| 主题 | Semi 2.1 |
| Dock 布局 | Dock.Avalonia 12 |
| 设置面板 | AutoSettingUI |
| 国际化 | DynamicLocalization |
| 日志 | Serilog |

---

## 产品组成

### 编辑器 (GalNet.Editor)

面向开发者，MVVM + DI。以节点图组织游戏逻辑，导出可分发的游戏包。

- 项目管理（新建、模板、打开）
- 节点图编辑（图 = 完整游戏流程图）
- 组编辑（双击组节点进入，多 Tab）
- 条目编辑（组内的单步操作）
- 资源管理与导入（类 Unity 资源面板，可设压缩 / 加密选项）
- 实时预览（可观察变量、跳转到任意组或组内某行）
- 顶部菜单：项目设置、编辑器设置、导出
- 日志 Dock 面板（展示 Serilog 输出）

### 启动器 (GalNet.Launcher)

面向玩家，多用户多游戏，存档隔离。支持全平台（Desktop / Android / iOS / Browser）。

- 用户管理（用户名 + 密码创建 / 删除）
- 游戏导入与校验
- 每用户独立存档与设置
- 运行游戏

启动器目录结构：

```
<LauncherRoot>/
├── Launcher.exe
├── configs/
├── users/<uid>/
│   ├── configs/global.json
│   └── <game-id>/
│       ├── configs.json
│       └── saves.json
├── games/<game-id>/    # 导入后解压、校验的游戏数据
```

---

## 核心设计决策

| 决策点 | 结论 |
|---|---|
| 执行模型 | 纯解释执行，条目类型注册为 EntryHandler 子类 |
| 条目结构 | 纯扁平队列，按编号顺序执行。唯一通用参数：`condition`；其余参数条目自行定义 |
| 条目状态 | 仅 Running / Completed 两态。Interrupt 由 handler 自行决定是否响应 |
| 坐标体系 | 背景和立绘统一为 Layer；使用 x/y/z 坐标，z 为 z-index 控制层叠顺序 |
| Runtime ↔ UI 契约 | `IGameView` 接口；页面通过 `ScreenTemplate`/`ScreenInstance` + `ShowPageAsync()` 切换 |
| 控件体系 | Category→Template→Instance 三层：`WidgetCategory`（类按钮/滑条）→ `WidgetTemplate`（具体样式）→ `WidgetInstance`（填入参数）。页面同理：`ScreenCategory`→`ScreenTemplate`→`ScreenInstance` |
| 变量系统 | VariableRoute（树状键路径）标识；player 和 save 共用命名空间，不可重名；变量为封装对象（非值类型）；表达式求值用自建 ExpressionEvaluator（递归下降，支持 [var] 占位符、比较/逻辑/算术） |
| 对话框切换 | 条目通过 `control_instance` 参数引用 WidgetInstance ID；缺省继承 |
| 音频模型 | 多 Channel（bgm / sfx1~N / voice / ambient），同 channel 替换，支持 once / repeat / times(n) |
| 存档内容 | GameSnapshot（当前节点 ID + 条目索引 + 场景快照（含 z 轴）+ 存档变量）|
| 用户变量作用域 | 按游戏隔离 |
| 包校验 | Hash 完整性校验；保留签名扩展点 |
| 日志 | Serilog；Runtime 通过 `ILogger` 输出，资源缺失跳过并 log；Editor 有 Log Dock 面板 |
| i18n | 文本：字符串 key + DynamicLocalization.ICultureService；资源：目录 fallback |
| 测试 | 参见「测试策略总览」章节 |
| Headless | `GalNet.Headless` = 编辑器的无界面版本，供 AI 集成和 CI，功能与 GUI Editor 对等 |
| 扩展点 | IGameView/IGameRuntime、WidgetCategory/WidgetTemplate/WidgetInstance、ScreenCategory/ScreenTemplate/ScreenInstance、EntryHandler、ITransition、IEffect |

---

## 项目分层

```
┌──────────────────────────────────────────────┐
│  GalNet.Editor        GalNet.Launcher        │  ← 壳层
│  GalNet.Headless      Launcher.Headless      │  ← 无头 CLI
├──────────────────────────────────────────────┤
│  GalNet.Editor.Control                       │  ← 编辑器专用控件
│  GalNet.Control                              │  ← 通用游戏 UI 控件（实现 IGameView）
├──────────────────────────────────────────────┤
│  GalNet.Runtime                              │  ← 游戏执行引擎
│  GalNet.Assets                               │  ← 资源管理
├──────────────────────────────────────────────┤
│  GalNet.Core                                 │  ← 领域模型 & 共享协议（含 IGameView）
└──────────────────────────────────────────────┘
```

### GalNet.Core

**所有通用数据模型**与共享协议，不放 UI 代码。

- 图 / 组 / 条目 / 分支模型
- 场景模型（Layer 列表：id/asset/x/y/z/visible、活跃控件实例、活跃特效）
- 变量系统（存档变量 & 用户变量，VariableRoute 树状键路径标识，封装对象）
- **WidgetCategory / WidgetTemplate / WidgetInstance / WidgetConfig** — 控件三层体系
- **ScreenCategory / ScreenTemplate / ScreenInstance / ScreenConfig** — 页面三层体系
- 打包清单
- 序列化 / 反序列化 / 版本迁移
- 资源引用描述
- **`IGameView` 接口** — Runtime 与游戏 UI 的唯一契约
- **`ITransition` / `IEffect` 接口** — 转场与特效扩展契约
- **`EntryContext`** — 条目执行的上下文载体（通过 Runtime 统一管理 View/I18n/变量）
- **`EntryHandler` 基类** — 条目类型扩展点
- **`IGameRuntime`** — Runtime 状态契约（CurrentNodeId/EntryIndex/VariableStore/SceneState/调用栈/View/I18n）

### GalNet.Assets

资源管理层。

- 资源扫描与 ID 映射
- 加载 / 卸载 / 缓存
- 加密 / 解密 / 压缩 / 解压
- 完整性校验

### GalNet.Runtime

游戏执行引擎，无 UI 依赖（仅依赖 `IGameView` 接口）。

核心入口为 **GameRuntime（实现 IGameRuntime）**，统一管理运行时状态：

- **CurrentNodeId / EntryIndex** — 当前执行位置
- **VariableStore** — 变量存储（VariableRoute 树状键路径）
- **SceneState** — 场景快照
- **调用栈** — 支持子流程调用/返回
- **View** — IGameView 实例
- **I18n** — ICultureService 实例

**功能列表**：

- 加载并解析游戏包
- 复杂条目 → 简单条目编译（惰性 + 缓存）
- 图转移引擎（节点 → 边 → 下一节点）
- 条目解释器（Type → EntryHandler 注册，Runtime 驱动状态机循环）
- 变量读写（VariableRoute 树状键路径）
- 分支判断（选项分支 & 条件 branch/switch）
- 存档 / 读档（GameSnapshot 序列化）
- 音频 Channel 管理
- 日志输出

### GalNet.Control

实现 `IGameView`。三层体系：**Category（类别）→ Template（模板）→ Instance（实例）**。

```
Category（"是什么类型的控件"）
  └── Template（"什么样式"）  ← XAML 定义
        └── Instance（"具体哪一个"）  ← 填入参数值
```

**核心模型**：

```csharp
// 类别：定义控件大类
public class WidgetCategory
{
    public string Name { get; }           // "Button", "Slider", "DialogueBox", "NvlBox", "ChoicePanel", ...
}

// 模板：类别下的具体样式（XAML + 参数定义）
public abstract class WidgetTemplate
{
    public string Id { get; }             // "default_button", "large_button"
    public string Category { get; }       // 所属 WidgetCategory.Name
    public abstract WidgetConfig CreateDefaultConfig();
    public abstract Control CreateView(WidgetConfig config);
}

// 实例：模板 + 填入的参数值
public class WidgetInstance
{
    public string Id { get; }             // "new_game_btn", "nvl_box"
    public string TemplateId { get; }     // 引用 WidgetTemplate.Id
    public WidgetConfig Config { get; set; }
}

public abstract class WidgetConfig { }
```

**类别与内置模板**：

| Category | 内置 Template | Config 示例参数 |
|---|---|---|
| `Button` | `DefaultButton`, `LargeButton` | 文本、宽高、图片、hover 效果 |
| `Slider` | `DefaultSlider` | min/max/step、颜色、标签 |
| `Toggle` | `DefaultToggle` | 开关样式、颜色 |
| `DialogueBox` | `DefaultDialogue`, `NvlDialogue` | 宽高、字体、颜色、底图、说话人位置 |
| `NvlBox` | `DefaultNvl` | 全屏尺寸、滚动速度、背景透明度 |
| `ChoicePanel` | `DefaultChoice`, `HorizontalChoice` | 布局、间距、按钮样式 |
| `SaveSlot` | `DefaultSlot` | 缩略图尺寸、文字布局、高亮样式 |

**自定义**：插件可新增 Category 或为已有 Category 新增 Template（写 XAML + Config 类 + DLL）。

DefaultGameView 启动时：
1. 扫描所有 `WidgetTemplate`（内置 + 插件 DLL）
2. 读取项目中的 `WidgetInstance` 定义
3. 为每个实例调用 `template.CreateView(instance.Config)`，缓存 `id → Control`
4. `ShowControl(id)` / `HideControl(id)` 切换可见性

条目通过 `control_instance` 参数引用 WidgetInstance ID（缺省继承上一条目）。

### 页面模板系统

同样三层：**ScreenCategory → ScreenTemplate → ScreenInstance**。

内置 ScreenCategory：`Title`、`Settings`、`SaveLoad`、`Gallery`、`Game`。每类有默认 Template，可自定义。

```csharp
public class ScreenCategory
{
    public string Name { get; }           // "Title", "Settings", "SaveLoad", "Gallery", "Game"
}

public abstract class ScreenTemplate
{
    public string Id { get; }
    public string Category { get; }
    public abstract ScreenConfig CreateDefaultConfig();
    public abstract Control CreateView(ScreenConfig config, Func<string, Control> resolveWidget);
}

public class ScreenInstance
{
    public string Id { get; }
    public string TemplateId { get; }
    public ScreenConfig Config { get; set; }
}

public abstract class ScreenConfig { }
```

页面 XAML 内用 Grid/StackPanel 布局 + ControlPlaceholder 引用 WidgetInstance。自定义页面：新增 ScreenCategory 或 ScreenTemplate（XAML + DLL）。

### GalNet.Editor.Control

编辑器专属控件：节点图画布、连线、节点面板、组编辑器、Dock 面板、资源选择器、变量编辑器。

### GalNet.Editor

编辑器主程序。Dock 布局：节点图、组编辑、变量编辑、资源管理、游戏预览、日志。

### GalNet.Headless / GalNet.Launcher.Headless

无界面 CLI，供 CI / 批处理 / AI 集成。

---

## 核心领域模型

### 图 (Graph)

一个游戏一张主图，由节点和边组成。一个入口节点。无后继边的节点视为结束，回标题界面。

### 节点 (Node)

只有两种：

- **组 (Group)**：一段线性内容序列，包含条目列表。执行完沿出边转移。
- **分支 (Branch)**：选项分支（展示选项、等待选择）或条件分支（根据变量值自动匹配出边）。

### 边 (Edge)

表示节点间转移关系。

### 条目 (Entry)

条目分两层抽象，对纯游戏开发者透明。

#### 复杂条目 (ComplexEntry)

开发者实际编写的高层条目。编辑器中一行对应一个 ComplexEntry。参数和简单条目类似，在编译时展开为多个简单条目。

```csharp
public abstract class ComplexEntry
{
    public int Id { get; }                       // 组内顺序 ID
    public string Type { get; }
    public string Condition { get; }             // 唯一通用参数
    public Dictionary<string, string> Params { get; }
    
    /// <summary>编译为 1+ 个简单条目</summary>
    public abstract IReadOnlyList<SimpleEntry> Compile();
}
```

#### 简单条目 (SimpleEntry)

编译后的最小执行单元。Runtime 只认 SimpleEntry，一一对应 EntryHandler。

```csharp
public class SimpleEntry
{
    public string Id { get; }                    // 编译后的 ID（如 "5" 或 "5_2"）
    public int SourceId { get; }                 // 来源复杂条目的 Id（调试/热更新用）
    public string Type { get; }
    public string Condition { get; }
    public Dictionary<string, string> Params { get; }
}
```

#### 编译与 ID 方案

1. 每个复杂条目在组内有唯一的顺序 ID（其在组中的行号）
2. 编译时 `ComplexEntry.Compile()` 返回若干 SimpleEntry：
   - 若返回 1 个：子条目 ID = `"{Id}"`（如 `"5"`）
   - 若返回多个：子条目 ID = `"{Id}_1"`, `"{Id}_2"`, ...（如 `"5_1"`, `"5_2"`）
   - 所有子条目的 `SourceId` = 原复杂条目的 `Id`
3. 组加载时一次性编译全部复杂条目 → 得扁平 SimpleEntry 列表 → Runtime 执行
4. 热更新时：重新编译单个复杂条目 → 替换对应 ID 前缀的简单条目

编译时机：
- 编辑器保存组时触发；Runtime 加载组时触发（惰性编译 + 缓存）

**示例**：

```
复杂条目 (id=5):  show_character | id=alice, asset=alice_smile, x=0.3, y=0.5, z=10, sfx=sfx_appear, transition=fade
    ↓ Compile()
简单条目:
  5_1: show_layer  | id=alice, asset=alice_smile, x=0.3, y=0.5, z=10
  5_2: play_sfx    | channel=sfx1, asset=sfx_appear
  5_3: transition  | type=fade, duration=0.5
```

### 条目类型（简单条目层面）

- 对话文本、显示/隐藏/移动 Layer、播放/暂停/停止音频、视频、过渡效果、变量修改、等待

**唯一通用参数**：`condition`。其余参数均由条目类型自行定义。

条目只有两个状态：**Running**、**Completed**。

### EntryHandler 基类

```csharp
public abstract class EntryHandler
{
    public abstract string EntryType { get; }
    
    /// <summary>是否需要 Runtime 等待完成才推进下一句</summary>
    public virtual bool IsBlocking => true;
    
    /// <summary>开始执行条目，进入 Running 状态</summary>
    public virtual void Start(EntryContext ctx) { }
    
    /// <summary>检查是否执行完毕，返回 true 进入 Completed 状态</summary>
    public virtual bool IsCompleted(EntryContext ctx) => true;
    
    /// <summary>执行完毕后的收尾（进入 Completed 状态时调用一次）</summary>
    public virtual void Complete(EntryContext ctx) { }
    
    /// <summary>用户交互中断（handler 自行决定是否处理）</summary>
    public virtual void Interrupt(EntryContext ctx) { }
}
```

Runtime 对每个条目驱动的状态循环：

```
  Start() ──→ [Running] ──→ IsCompleted? ──→ Complete() ──→ 下一句
                │                           
                └── 用户点击 → handler.Interrupt()（handler 自己决定行为）
```

`IsCompleted` 由各 handler 定义：对话 = 逐字显示完毕且语音完毕（若 `wait_until_finished`）；BGM（`wait_until_finished=false`）= 立即 true。

### IGameView 契约

定义在 Core，Runtime 与 UI 的唯一接口。页面切换通过 `ShowPageAsync`。

```csharp
public interface IGameView
{
    // ── Layer 管理 ──
    void ShowLayer(string id, string assetId, float x, float y, float z = 0);
    void HideLayer(string id);
    void MoveLayer(string id, float x, float y, float z, float durationSec);

    // ── 控件实例管理 ──
    void ShowControl(string instanceId);
    void HideControl(string instanceId);

    // ── 页面切换 ──
    /// <summary>切换到指定页面实例。隐藏当前所有 Layer+控件，渲染目标 ScreenInstance。
    /// 返回被点击控件的索引（如选项索引），Runtime 据此决策。</summary>
    Task<int> ShowPageAsync(string ScreenInstanceId, CancellationToken ct);

    // ── 音频 ──
    void PlayAudio(string channel, string assetId, float volume, string mode, int times);
    void StopAudio(string channel);
    void PauseAudio(string channel);
    void ResumeAudio(string channel);
    void EnqueueAudio(string channel, string assetId, int times);
    void ConfigureAudioQueue(string channel, string onEnd, string onEmpty);

    // ── 视频 ──
    void PlayVideo(string assetId);
    void StopVideo();

    // ── 控件 ──
    void SetControlProperty(string instanceId, string property, string value);
    void ApplyTransition(string type, float durationSec);
    void ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters);

    // ── 异步操作 ──
    Task StartTypewriter(string WidgetInstanceId, string speaker, string text, CancellationToken ct);
    void SkipTypewriter(string WidgetInstanceId);
    void SetVoice(string assetId);

    // ── 阻塞交互 ──
    Task WaitForClickAsync(CancellationToken ct);
    Task<int> WaitForChoiceAsync(string WidgetInstanceId, string[] options, CancellationToken ct);
}
```

### ScreenInstance — 页面实例

所有界面（标题/设置/存档/鉴赏/自定义）均为 `ScreenInstance`，关联一个 `ScreenTemplate`。

页面模板用 XAML 定义布局（Grid/StackPanel + ControlPlaceholder），实例填入 Config 参数。

**预定义页面实例示例**：

| 页面实例 ID | 模板 | 包含的控件实例 |
|---|---|---|
| `title_page` | `Title` | `new_game_btn`, `load_btn`, `settings_btn`, `exit_btn` |
| `settings_page` | `Settings` | `bgm_vol_slider`, `sfx_vol_slider`, `fullscreen_toggle`, `back_btn` |
| `save_page` | `SaveLoad` | `save_slot_1`~`20`, `back_btn`, `page_prev_btn`, `page_next_btn` |
| `gallery_page` | `Gallery` | `cg_thumb_1`~`N`, `back_btn` |
| `game_page` | `Game` | `default_dialogue`, `fancy_choices` |
| `custom_page` | 自定义 XAML | 任意自定义控件实例 |

**Runtime 页面流转**：

```
ShowPageAsync("title_page")
  → 用户点击 "new_game_btn"  → 返回 "new_game_btn" → 新游戏 → ShowPageAsync("game_page")
  → 用户点击 "settings_btn" → 返回 "settings_btn" → ShowPageAsync("settings_page")
      → 拖滑块 → Slider 控件内部更新 → 点击 "back_btn" → ShowPageAsync("title_page")
  → 用户点击 "exit_btn" → 返回 "exit_btn" → 退出
```

### EntryContext

```csharp
public class EntryContext
{
    public IGameRuntime Runtime { get; }                        // 运行时状态入口
    public Dictionary<string, string> Params { get; }

    // 文本查找：通过 ICultureService 解析 key
    public string GetText(string key, string def = "")
        => Runtime.I18n.GetString(GetString(key), def);

    public string GetString(string key, string def = "") => Params.GetValueOrDefault(key, def);
    public bool GetBool(string key, bool def = false) => bool.TryParse(Params.GetValueOrDefault(key), out var v) ? v : def;
    public float GetFloat(string key, float def = 0f) => float.TryParse(Params.GetValueOrDefault(key), out var v) ? v : def;
    public int GetInt(string key, int def = 0) => int.TryParse(Params.GetValueOrDefault(key), out var v) ? v : def;
}
```

---

## 变量系统

变量为**封装对象**（非值类型），通过 **VariableRoute（树状键路径）** 标识。player 和 save 变量**共用命名空间**，不可重名。

| 类型 | 作用域 | 持久化 |
|---|---|---|
| 存档变量 | 单个存档内 | 随存档保存 |
| 用户变量 | 单个用户的某游戏内（不同游戏隔离） | 跨存档保存 |

表达式求值使用自建 **ExpressionEvaluator**（递归下降解析器，在 `GalNet.Runtime.Variables` 命名空间），支持：
- `[var]` 占位符引用变量值
- 比较运算符：`==` `!=` `<` `>` `<=` `>=`
- 逻辑运算符：`&&` `||`
- 算术运算符：`+` `-` `*` `/`

---

## i18n 国际化

**文本**：条目中的名称、对话内容等使用字符串 key，通过 **ICultureService** 查找当前语言对应的翻译。

- 创建项目时选择**目标语言**（如 `zh_cn`）
- 编辑器默认编辑目标语言文本，解析时先查当前语言 → 回退目标语言 → 空
- 编辑器提供 **i18n 翻译表页面**：每行一个键，列 = 各语言翻译，可增删语言列
- 项目设置可切换目标语言
- 条目中通过字符串 key 引用文本，EntryContext.GetText() 负责解析

**资源**（音频/视频/图片/Layer）：目录 fallback 模式，无需额外配置。

```
/Assets/
├── Layer/
│   ├── test.png              ← 默认资源
│   └── zh_cn/test.png        ← 中文版（同文件名）
├── Audio/
│   ├── bgm_01.ogg
│   └── zh_cn/bgm_01.ogg
└── Video/
    ├── op.mp4
    └── zh_cn/op.mp4
```

资源国际化通过目录 fallback 实现：加载时先查 `<locale>/<asset>`，不存在则回退默认路径。

---

## 音频 Channel 模型

全局 Channel 列表，同时可播放多个 Channel：

| Channel | 用途 |
|---|---|
| `bgm` | 背景音乐 |
| `voice` | 角色配音 |
| `sfx1` ~ `sfxN` | 音效（默认 4 个，可配置） |
| `ambient` | 环境音 |

播放模式：`once`（一遍）、`repeat`（循环）、`times:N`（N 遍）。

规则：同一 Channel 新播放自动替换旧播放。`play_audio` / `stop_audio` / `pause_audio` 均为独立条目类型。

**队列与直接播放交互规则**：

- 每个轨道维护独立队列（通过 `音频队列设置` 配置行为）
- `播放音频`（直接播放）强制结束当前音乐，插入新歌 → 被中断的歌重新入队首
- 直接播放不影响队列中后续歌曲
- 当前歌曲正在交叉淡入时，直接播放取消淡入

示例：队列设为 `保留并顺序播放`，正在播 BGM_A，突然 `播放音频 BGM_B (once)`：
→ BGM_A 停止并入队首 → BGM_B 播放一次 → 播放完毕 → 继续队列（队首 = BGM_A）

---

## 存档系统

- **默认槽位数**：60（项目设置可调）
- **显示**：由存档页面模板控制（如 4 列 × 3 行 × 10 页）
- 每个槽位存储：缩略图（自动截图）、时间戳、游玩时长、当前节点名
- 快存/快读由游戏页面模板控制：默认对话框下方放快存/快读/自动模式按钮，按钮均为 WidgetInstance 可替换

### 存档结构（GameSnapshot）

存档数据模型为 **GameSnapshot**（在 `GalNet.Core.Runtime` 命名空间），包含当前节点 ID、条目索引、场景快照和存档变量。由 **SaveManager** 负责序列化/反序列化。

```json
{
  "version": 1,
  "timestamp": "2026-07-04T12:00:00Z",
  "current_node_id": "group_05",
  "current_entry_index": 3,
  "scene": {
    "layers": [
      { "id": "bg",     "asset": "bg_classroom", "x": 0,   "y": 0,   "z": 0,  "visible": true },
      { "id": "alice",  "asset": "alice_smile",  "x": 0.3, "y": 0.5, "z": 10, "visible": true },
      { "id": "bob",    "asset": "bob_normal",   "x": 0.7, "y": 0.5, "z": 5,  "visible": true }
    ],
    "active_controls": ["default_dialogue", "fancy_choices"],
    "active_effects": ["shake"],
    "active_transition": null
  },
  "save_variables": {
    "affection_alice": 5,
    "route_flag": "alice_route"
  }
}
```

---

## 对话历史 (Backlog)

独立页面，记录自上次加载存档以来的所有对话文本。加载存档时清空重置。通过 ScreenTemplate 实现。

## 鉴赏解锁

编辑器提供鉴赏管理页面，可注册四类可解锁内容：

| 类别 | 存储 | 参数 |
|---|---|---|
| 立绘 | 文件引用 | 指定图片 |
| CG | 文件引用 | 指定图片 |
| OST | 文件引用 | 指定音频 |
| 场景 (Scene) | 组 ID | 指定组，显示组名 |

条目类型 `解锁 [S]`：

| 参数 | 类型 | 说明 |
|---|---|---|
| 类别 | string | `立绘` / `CG` / `OST` / `场景` |
| id | string | 对应类别下的内容 ID |

解锁状态持久化在用户变量中。

---

## 扩展点设计

系统提供以下扩展点。

### 1. 页面实例 (ScreenInstance)

**扩展方式**：编辑器中创建 ScreenInstance → 选 ScreenTemplate → 填 Config → 分配控件实例。

任何界面都是 ScreenInstance。自定义页面写 XAML ScreenTemplate。

```
难度：配置参数 = 极低；写 XAML 模板 = 高
```

### 2. 控件模板 (WidgetTemplate) — 插件新增控件类型

**扩展方式**：继承 `WidgetTemplate` + 对应 `WidgetConfig` + XAML UserControl，打包 DLL。

新增控件类型后，开发者可在编辑器中从该模板创建 WidgetInstance 并配置参数。

```
适用场景：全新对话框形态、特殊 UI 组件（小地图、状态栏）
难度：高（需编写 XAML + Config 类 + 打包 DLL）
替代方案：已有模板创建多实例、改 Config 参数即可（低难度，不动代码）
```

### 3. 条目类型 (EntryHandler) — 简单条目层

**扩展方式**：继承 `EntryHandler` 基类，注册到 Runtime。对应 SimpleEntry 的 Type。

示例——自定义震动条目：
```csharp
public class ShakeHandler : EntryHandler
{
    public override string EntryType => "shake";
    public override bool IsBlocking => false;
    public override void Start(EntryContext ctx)
        => ctx.View.ApplyEffect("shake", new() { ["intensity"] = ctx.GetFloat("intensity", 5f) });
}
runtime.RegisterHandler(new ShakeHandler());
```
参数完全自定义，通过 `EntryContext.GetString/GetFloat/GetBool` 读取。

### 4. 转场 (ITransition)

定义在 Core，默认实现在 Control。

```csharp
public interface ITransition
{
    string Name { get; }
    Task ExecuteAsync(IGameView view, string fromAsset, string toAsset, 
                      float durationSec, CancellationToken ct);
}
```

内置：`fade`、`slide_left`、`slide_right`、`dissolve`。

**扩展方式**：实现 `ITransition`，注册到 `TransitionRegistry`。在条目中通过 `transition` 参数按名称引用。

```
适用场景：自定义场景切换动画（翻页、百叶窗、缩放等）
难度：中（需实现异步动画逻辑）
```

### 5. 特效 (IEffect)

定义在 Core，默认实现在 Control。

```csharp
public interface IEffect
{
    string Name { get; }
    void Start(IGameView view, IReadOnlyDictionary<string, object> parameters);
    void Stop(IGameView view);
}
```

内置：`shake`（震动）、`vignette`（暗角）、`flash`（闪白）。

**扩展方式**：实现 `IEffect`，注册到 `EffectRegistry`。通过 `IGameView.ApplyEffect()` 按名称触发。

```
适用场景：屏幕震动、色调变换、粒子、雨雪效果等
难度：中（需实现视觉效果逻辑）
```

### 扩展点总览

| 扩展点 | 接口/基类 | 注册方式 | 难度 |
|---|---|---|---|
| 页面实例 | `ScreenInstance` | 编辑器创建 | 极低 |
| 页面模板 | `ScreenTemplate` | 插件 DLL | 高 |
| 控件模板 | `WidgetTemplate` | 插件 DLL | 高 |
| 控件实例 | `WidgetInstance` (Config 参数) | 编辑器创建 | 低 |
| 自定义条目 | `EntryHandler` | `Runtime.RegisterHandler()` | 低 |
| 自定义转场 | `ITransition` | `TransitionRegistry` | 中 |
| 自定义特效 | `IEffect` | `EffectRegistry` | 中 |

- 所有界面（标题/设置/存档/鉴赏/游戏/自定义）均为 ScreenDefinition + 控件实例组合
- 控件模板 (Shader) = 写 XAML + Config 类，一次开发；控件实例 (Material) = 填 Config，多次复用

---

## 错误处理策略

| 场景 | 行为 |
|---|---|
| 资源缺失 | Warning 日志，跳过条目 |
| 变量未定义 | Warning 日志，条件=false，表达式值=0 |
| 表达式求值异常（除零、溢出） | Warning 日志，结果=0 |
| 存档损坏 | 提示用户，拒绝加载 |
| 存档版本不兼容 | 提示用户，强制加载（尽力解析） |
| 插件加载失败 | 跳过该插件，Log Error |

---

## 日志

- Runtime 通过 `Microsoft.Extensions.Logging.ILogger` 输出
- 实际日志实现用 Serilog，在 Editor / Launcher 启动时配置 sink
- 资源缺失：跳过该条目，写入 Warning 日志，继续执行
- 变量未定义：Warning 日志，条件表达式按 false 算，数学表达式按 0 算
- 表达式除零：Warning 日志，结果为 0
- Editor 提供 Log Dock 面板，实时展示 Runtime 和编辑器自身日志

---

## 测试策略总览

| 层 | 测试类型 | 范围 | 工具/方式 |
|---|---|---|---|
| GalNet.Core | 单元测试 | 领域模型、序列化、表达式解析 | xUnit |
| GalNet.Runtime | 单元测试 + 集成测试 | GameRuntime 状态机、EntryHandler、变量系统、存档读写 | xUnit + HeadlessScene |
| GalNet.Runtime.Variables | 单元测试 | ExpressionEvaluator 递归下降解析、VariableRoute 树状路径 | xUnit |
| GalNet.Control | 需 UI 环境 | IGameView 实现、控件渲染、页面切换 | Avalonia Headless 测试 |
| GalNet.Headless | 集成测试 | 完整游戏流程（加载 → 执行 → 存档 → 读档） | CLI + xUnit |
| GalNet.Editor | 需 UI 环境 | 编辑器 UI 交互 | Avalonia Headless 测试 |
