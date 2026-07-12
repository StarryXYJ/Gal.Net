# GalNet — Services Analysis

> 游戏运行所需的服务清单、职责边界、依赖关系。
> 
> **更新于 2026-07-11**：Editor 服务已拆分至 Abstraction（接口/模型，插件可用）和 Shared（实现，Headless + Editor 共用）。

---

## 项目分层（服务视角）

```
GalNet.Editor              ← UI 相关服务（本地化实现、主题、文件对话框、视图工厂）
GalNet.Editor.Shared       ← 非 UI 服务实现（Headless + Editor 共用，插件不可用）
GalNet.Editor.Abstraction  ← 抽象接口与模型（插件可用）
GalNet.Runtime             ← 游戏执行引擎
GalNet.Core                ← 领域模型 & 共享协议
```

---

## 服务清单总览

| # | 服务/接口 | Core 层 | 实现位置 | 状态 | 消费者 |
|---|----------|---------|---------|------|--------|
| 1 | **GameEngine** | 无接口（sealed） | Runtime | ✅ 已实现 | Launcher / Editor |
| 2 | **IGameRuntime** | 已有 | Runtime/GameRuntime | ✅ 已实现 | EntryHandler / GameEngine |
| 3 | **IGameView** | 已有 | Control/DefaultGameView | ⚠️ 需重构 | EntryHandler |
| 4 | **I18n** | `ICultureService`（外部） | DynamicLocalization | ✅ 已集成 | EntryHandler / UI |
| 5 | **INavigationHost** | 🆕 Core | GameHostView (Control) | 🆕 新建 | 所有 Screen ViewModel |
| 6 | **ISettingsService** | 🆕 Core | Control/Services | 🆕 新建 | SettingsScreen / DefaultGameView |
| 7 | **ISaveService** | 🆕 Core | Control/Services | 🆕 新建 | SaveLoadScreen / GameEngine |
| 8 | **IAudioService** | 🆕 Core | Control/Services | 🆕 新建 | EntryHandler(↔IGameView↔) / SettingsScreen |
| 9 | **IInputService** | 🆕 Core | Control/Services | 🆕 新建 | GameHostView |
| 10 | **ILogService** | `Serilog.ILogger`（外部） | 已集成 | ✅ 已集成 | 全局 |

---

## 逐个详细分析

### 1. GameEngine — 游戏状态机

**状态：** ✅ 已实现。

**职责：** 图加载 → 条目编译 → 状态机循环 → 节点转移 → 存档快照。

**备注：** 不需要接口（唯一实现），保持现状。

---

### 2. IGameRuntime — 运行时状态

**状态：** ✅ 已实现。

**职责：** 位置跟踪、变量读写、条件求值、场景状态、I18n 引用、快照。

**变动：** `SettingsContainer` 从 Runtime 剥离到 `ISettingsService`，Runtime 通过注入获取。

---

### 3. IGameView — 视图抽象（需重构）

**状态：** ⚠️ 需要清理 `DefaultGameView`。

**当前问题：** `DefaultGameView` 额外承担了页面切换（`ShowScreenOverlay`）、按钮扫描（`FindAllButtons`）、导航逻辑。

**重构方向：**

```csharp
public class DefaultGameView : UserControl, IGameView
{
    // ── IGameView 保留（游戏内渲染） ──
    // ShowLayer / HideLayer / MoveLayer
    // ShowControl / HideControl / SetControlProperty
    // StartTypewriter / SkipTypewriter / SetVoice
    // WaitForClickAsync / WaitForChoiceAsync
    // PlayAudio / StopAudio / ... (委托给 IAudioService)
    // PlayVideo / StopVideo

    // ── 外部通知（委托给 GameHostView） ──
    public event Action? SettingsRequested;
}
```

**`ShowPageAsync`** 保留在接口中，实现改为 `return screenInstanceId`（不自动处理页面切换）。

---

### 4. I18n — 多语言

**状态：** ✅ `DynamicLocalization.Core.ICultureService`。

**当前设计完全够用。** EntryHandler 通过 `ctx.GetText(key)` 解析展示文本。

---

### 5. INavigationHost — 导航中介（新建）

**为什么需要：** Screen ViewModel 需要触发导航/弹窗，但 ViewModel 不能直接引用 View。`INavigationHost` 是轻量中介。

**接口（Core/Services/）：**

```csharp
public interface INavigationHost
{
    /// <summary>替换主内容区。</summary>
    void NavigateTo(string key, object? parameter = null);

    /// <summary>通过 Ursa DialogHost 弹出模态。</summary>
    void ShowModal(string key, object? parameter = null);

    /// <summary>关闭当前模态。</summary>
    void CloseModal();

    /// <summary>Toast 通知。</summary>
    void ShowToast(string message);
}
```

**谁实现：** `GameHostView` 直接实现此接口。不独立注册到 DI，GameHostView 初始化时将自己注册为单例。

**ViewModel 使用示例：**

```csharp
public class TitleScreenViewModel : ViewModelBase
{
    private readonly INavigationHost _nav;

    public TitleScreenViewModel(INavigationHost nav)
    {
        _nav = nav;
    }

    [RelayCommand] void NewGame() => _nav.NavigateTo("ChapterSelect");
    [RelayCommand] void OpenSettings() => _nav.ShowModal("Settings");
}
```

---

### 6. ISettingsService — 游戏设置（新建）

**接口（Core/Services/）：**

```csharp
public interface ISettingsService
{
    double BgmVolume { get; set; }
    double SfxVolume { get; set; }
    double VoiceVolume { get; set; }
    double TextSpeed { get; set; }
    bool Fullscreen { get; set; }

    GameSettings GetSnapshot();
    void ApplySnapshot(GameSettings settings);

    Task LoadAsync(string path);
    Task SaveAsync(string path);

    event Action? Changed;
}
```

**实现：** `Control/Services/SettingsService`，内部持有 `GameSettings` 实例。

**消费者：**
- `SettingsScreenViewModel` — 绑定修改
- `DefaultGameView` — 读取 TextSpeed 决定打字机延迟
- `GameEngine` — 初始化时获取默认值

**与现有 `SettingsContainer` 的关系：** 共存。`SettingsContainer` 是 Core 通用容器，`SettingsService` 封装 `GameSettings` + 持久化。

---

### 7. ISaveService — 存档服务（新建）

**接口（Core/Services/）：**

```csharp
public interface ISaveService
{
    int MaxSlots { get; }
    IReadOnlyList<SaveSlotInfo> ListSlots();

    Task SaveAsync(int slot, GameSnapshot snapshot);
    Task<GameSnapshot?> LoadAsync(int slot);
    Task DeleteAsync(int slot);

    Task QuickSaveAsync(GameSnapshot snapshot);
    Task<GameSnapshot?> QuickLoadAsync();
}
```

**与现有 `SaveManager` 的关系：**
- `SaveManager`（Runtime）是纯静态序列化工具
- `ISaveService` 管理槽位、文件路径、索引
- `SaveService` 内部调用 `SaveManager.Serialize/SaveToFile`

---

### 8. IAudioService — 音频服务（新建）

**接口（Core/Services/）：**

```csharp
// Channel 约定：
//   0 = BGM    1 = BGM2    2 = Voice    3..N = SFX
public interface IAudioService
{
    void Play(int channel, string assetId, float volume = 1f, string mode = "once", int times = 1);
    void Stop(int channel);
    void Pause(int channel);
    void Resume(int channel);
    void Enqueue(int channel, string assetId, int times = 1);

    float GetVolume(int channel);
    void SetVolume(int channel, float volume);
    void SetMasterVolume(float volume);
}
```

**说明：**
- 用 `int` 而非 `string` 作为 channel 标识
- 0/1/2 约定为 BGM/Voice 保留，SFX 从 3 开始（数量由 `GameSettings.SfxChannelCount` 配置）
- `IGameView` 的方法委托给 `IAudioService`
- `SettingsScreenViewModel` 直接注入 `IAudioService` 控制音量

---

### 9. IInputService — 快捷键/输入（新建）

**接口（Core/Services/）：**

```csharp
public interface IInputService
{
    IDisposable RegisterHotkey(KeyGesture gesture, Action handler);
}
```

**消费者：** `GameHostView` 注册全局快捷键（Esc→设置、F5→快存、F9→快读、Ctrl→跳过）。

---

## 删减项

| 原设计项 | 决定 | 原因 |
|---------|------|------|
| `IGameState` | ❌ 删除 | 变量外部查看 → 直接读 `GameSnapshot.Variables` 即可 |
| `ISceneService` | ❌ 删除 | `SceneState` 已在 Runtime 中，图层操作在 `IGameView`，够用 |
| `IVariableService` | ❌ 不拆 | `IGameRuntime.SetVariable/GetVariable/EvaluateCondition` 已经覆盖，Handler 通过 `ctx.Runtime` 调用 |

---

## 依赖注入图

```
GameHostView : INavigationHost
  ├── IServiceProvider
  │
  ├── ContentControl (主内容)
  │     ├── GameScreenView → DefaultGameView : IGameView
  │     │     ├── ISettingsService
  │     │     └── IAudioService
  │     │
  │     └── TitleScreenView
  │           └── TitleScreenViewModel
  │                 └── INavigationHost
  │
  ├── Ursa DialogHost（模态宿主）
  │     ├── SettingsScreenView → VM ← ISettingsService
  │     ├── SaveLoadScreenView → VM ← ISaveService
  │     └── ConfirmDialog → VM ← INavigationHost
  │
  ├── Ursa ToastHost
  └── IInputService (快捷键)
```

---

## 线程模型

| 层 | 线程 | 访问的 Service |
|---|---|---|
| GameEngine.StepAsync | ThreadPool | IGameRuntime（所有操作同步） |
| EntryHandler | 引擎线程 | IGameRuntime + IGameView |
| DefaultGameView | **UI 线程**（Dispatcher） | ISettingsService, IAudioService |
| GameHostView | **UI 线程** | IServiceProvider, IInputService |
| Screen ViewModels | **UI 线程** | INavigationHost, ISettingsService, ISaveService |

**规则：**
- 修改 UI 状态的方法必须在 UI 线程调用
- 纯数据 Service（`ISaveService`）可跨线程访问（内部自行处理同步）
- Handler 不直接感知 UI Service — 通过 `IGameView` 间接使用音频

---

## 实现优先级

| 优先级 | 任务 | 依赖 |
|--------|------|------|
| P0 | 创建 `Core/Services/` 全部接口 | 无 |
| P0 | `GameHostView` — 导航 + DialogHost + ToastHost + INavigationHost | Ursa 库 |
| P0 | 清理 `DefaultGameView` | GameHostView |
| P1 | `SettingsService` 实现 | ISettingsService |
| P1 | `TitleScreenView` + ViewModel（注入 INavigationHost） | INavigationHost |
| P1 | `SettingsScreenView` + ViewModel（注入 ISettingsService + INavigationHost） | SettingsService |
| P1 | 更新 Editor MVP 适配新架构 | 以上全部 |
| P2 | `SaveService` 实现（复用 SaveManager） | ISaveService |
| P2 | `SaveLoadScreenView` + ViewModel | ISaveService |
| P2 | `InputService` 实现 + GameHostView 快捷键 | IInputService |
| P3 | `AudioService` 实现 | 平台音频后端 |
| P3 | `ChapterSelectView` + ViewModel | ISaveService |
