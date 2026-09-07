# 游戏运行时与展示层解耦：分阶段改造计划

## 1. 目标与边界

本计划把游戏运行逻辑、视觉实现、文件系统实现和编辑器职责分开：

```text
游戏文件（group / graph / asset references）
                    |
                    v
      GalNet.Core + GalNet.Runtime（平台无关）
                    |
     +--------------+----------------+----------------+
     |                               |                |
     v                               v                v
Presentation.Abstractions     Storage.Abstractions   GameState/Snapshot
     |                               |
     v                               v
Avalonia 示例客户端 / CLI / 编辑器预览     文件系统 / pak / 云端等实现
```

最终原则：

- `GalNet.Runtime` 只处理图推进、条目语义、变量、场景状态、存档状态和等待条件；不得引用 Avalonia、WPF、Qt、音频库或具体文件系统。
- 最终游戏客户端是独立工程。官方只提供一个完整 Avalonia 示例工程，开发者可复制、替换服务或自行从零实现宿主。
- 编辑器只编辑游戏文件，并提供继承编辑器主题的基础预览；不再保存或编辑最终游戏 UI 的主题、布局或视觉控件配置。
- 转场和特效是可高度定制的扩展点，分别定义接口和数据对象。音频、视频、图层、对话等保持专用、强类型的固定接口。
- 文件读取、资源访问、存档、玩家全局变量和进度均以接口定义，默认文件系统实现位于外层基础设施项目。

## 2. 目标项目与依赖方向

新增项目均放在 `src/`。仓库当前未跟踪 `.sln` 文件；开始实施时应先确认实际解决方案位置，或在仓库根目录新建并纳入版本控制。

```text
GalNet.Presentation.Abstractions
  └─ 视觉与交互接口、转场/特效请求 DTO；零 UI 框架依赖

GalNet.Core
  └─ 游戏文件模型、Graph、Entry、SceneState、Snapshot、值对象

GalNet.Storage.Abstractions       --> GalNet.Core
  └─ 内容、资源、存档、玩家变量、游戏进度接口

GalNet.Runtime                    --> Core + 两个 Abstractions
  └─ GameEngine、GameRuntime、内建 EntryHandler、表达式与状态机

GalNet.Assets                     --> Core + Storage.Abstractions
  └─ pak、归档、资源文件实现

GalNet.Storage.FileSystem         --> Core + Runtime + Storage.Abstractions
  └─ 目录游戏内容、文件存档、玩家变量、游戏进度实现

GalNet.Avalonia.Controls          --> Presentation.Abstractions
  └─ 无样式/默认样式 Avalonia 控件；不依赖 Runtime 或 Assets

GalNet.Sample.Avalonia            --> Runtime + Assets + Storage.FileSystem + Avalonia.Controls
GalNet.Player.Console             --> Runtime + Assets + Storage.FileSystem
GalNet.Editor                     --> Runtime + Assets + Storage.FileSystem + Avalonia.Controls
```

`GalNet.Control` 与 `GalNet.Control.Abstraction` 在迁移期间保留；所有调用方完成迁移、测试通过后再删除。

## 3. 核心契约设计

### 3.1 视觉与输入：`GalNet.Presentation.Abstractions`

保留 `IGameView` 作为组合入口，便于宿主一次性实现和注入；但它只组合小接口，运行时代码优先依赖实际所需的小接口，不新增 UI 框架类型。

```csharp
public interface IGameView :
    ILayerView,
    IDialogueView,
    IAudioView,
    IVideoView,
    IInteractionView,
    ITransitionView,
    IEffectView
{
}
```

- 图层、对话、音频、视频保持现有专用接口和参数语义，例如 `PlayAudio(channel, assetId, volume, mode, times)`。
- `IInteractionView` 保持等待继续和选择输入的职责；它是平台输入适配层，不包含状态机规则。
- `IGameView` 不再放在 `IGameRuntime` 中。由 `GameEngine` 构造函数显式接收，或通过运行时组装选项传入；DI 容器只在 CLI、Avalonia 示例工程、编辑器等组合根使用。

转场和特效必须分开：

```csharp
public interface ITransitionView
{
    Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct);
}

public interface IEffectView
{
    Task StartEffectAsync(EffectRequest request, CancellationToken ct);
    Task StopEffectAsync(string instanceId, CancellationToken ct);
}

public sealed record TransitionRequest(
    string Id,
    string? FromImageId,
    string? ToImageId,
    TimeSpan Duration,
    bool IsBlocking,
    string Parameters);

public sealed record EffectRequest(
    string Id,
    string InstanceId,
    TimeSpan? Duration,
    bool IsBlocking,
    string Parameters);
```

`Parameters` 保存原始 JSON 字符串（也允许空字符串）。核心不解析高定制参数；示例客户端按 `Id` 从注册表查找实现，并由该实现自行解析。`IsBlocking` 决定引擎是否 `await` 宿主任务。`TransitionRequest` 的 `FromImageId`、`ToImageId` 保留当前转场需要的源/目标图像信息。

### 3.2 文件、存档与全局变量：`GalNet.Storage.Abstractions`

将现有散落在 `GalNet.Core.Services` 和具体项目中的契约按用途迁移：

- `IGameContentProvider`：加载 Graph、group、元数据等游戏内容。
- `IAssetProvider`、`IAssetManager`、`IArchive`：按资源 ID 读取资源与归档。
- `ISaveService`：槽位存档、快速存档、缩略图及 `GameSnapshot`。
- `IPlayerVariableStore`：跨存档的玩家变量读写；与单个存档内变量分离。
- `IGameProgressService`：画廊解锁、已读记录等跨局进度。

`GalNet.Storage.FileSystem` 提供默认的目录、文件和 pak 组合实现。以后接入云存档、Steam Cloud 或自定义加密时，只需替换实现并在宿主工程注册。

### 3.3 状态机职责

`GameRuntime` 只包含游戏事实：当前位置、变量、调用栈、`SceneState`、当前对话/选项/等待原因。`GameEngine` 负责把 Entry 转换为状态变化和接口调用。

- 每个会影响场景的条目必须先更新 `SceneState`，再通知对应展示接口。
- 读取存档后，引擎根据 `SceneState` 调用视图恢复接口；视图不作为状态唯一来源。
- 文本条目：写入当前对话状态，调用打字机接口；继续/跳过由 `IInteractionView` 返回，再推进状态机。
- 选择条目：写入可见选项并等待输入；引擎只接受合法索引。
- 等待条目：改为注入 `IClock`/计时抽象以保证可测试性，禁止 Handler 直接使用 `DateTime.UtcNow`。
- 可视异步操作仅在对应 `IsBlocking` 为真时阻塞状态机；音频、视频和普通图层操作默认不阻塞。

## 4. 分阶段执行

### Phase 0：基线与项目骨架（已完成）

- 已确认根解决方案为 `GalNet.slnx`，并将六个新项目加入对应解决方案文件夹。
- 已创建 `GalNet.Presentation.Abstractions`、`GalNet.Storage.Abstractions`、`GalNet.Storage.FileSystem`、`GalNet.Avalonia.Controls`、`GalNet.Sample.Avalonia`、`GalNet.Player.Console` 骨架。
- 六个新增项目均已独立构建通过。既有资源测试共 77 项通过；完整解决方案构建受正在运行的编辑器锁定 `GalNet.Runtime.dll` 影响，未修改任何运行中进程。

### Phase 1：抽取视觉与存储契约（已完成）

- 已将所有 `View` 契约迁入 `GalNet.Presentation.Abstractions`，保留原命名空间以保持旧宿主兼容；`IGameView` 现为小接口组合。
- 已新增独立的 `ITransitionView`/`TransitionRequest` 与 `IEffectView`/`EffectRequest`；旧转场/特效成员作为 Phase 2 前的兼容层保留。
- 已将资源、内容、存档、变量和进度契约迁入 `GalNet.Storage.Abstractions`，新增 `IPlayerVariableStore`。
- 已将目录内容读取、文件存档、文件进度和文件玩家变量实现迁入 `GalNet.Storage.FileSystem`；编辑器预览已改用该实现。
- 已更新项目引用并验证：存储测试 78 项通过，Control 和 Editor 均在隔离输出目录构建成功。
- `GalNet.Core` 中遗留的 Avalonia `UiProject`/调色板模型属于编辑器 UI 定制功能，将随 Phase 6 一并移除，而非在本阶段拆出。

### Phase 2：运行时状态机与宿主边界（已完成）

- `IGameRuntime` 与 Runtime Context 已移除 `IGameView`；Core 不再引用展示抽象项目。
- `GameEngine` 显式接收每会话的 `IGameView` 和 `TimeProvider`，Runtime Handler 通过显式参数执行，不再隐藏地从状态对象取得 UI 服务。
- 图层状态会先写入 `SceneState`；读档恢复 Runtime 状态后由 Engine 重放可见图层。文本、选择、等待、音视频、对话、变量及画廊 Handler 已迁入新执行模型。
- 转场条目使用 `transitionId`、`transitionDuration`、`transitionBlocking`、`transitionParameters`；特效条目使用 `id`、`instanceId`、`duration`、`blocking`、`parameters`。动态参数保持原始 JSON，Core 不解析。
- 已实现构造函数注入的 `CompositeGameView`。组合根创建各展示服务后传入 facade；Runtime 与 facade 均不依赖 `IServiceProvider`。
- `NullGameView` 已切换到新契约；`GalNet.Player.Console` 可运行真实游戏目录并演示组合根注入。旧的硬编码 `GalNet.Headless` 已删除。
- `GalNet.Control.Tests` 已合并到 `GeneralTest`，保留项目生命周期测试；旧 UI 配置测试已删除。
- `GalNet.Control` 仅保留现有预览的适配维护：效果接口为空实现；转场按动态 ID 识别 `black`、`white`、`cross` 等入口，暂不承载动画实现。

验证：`GeneralTest` 127 项通过；命令行 Player、Runtime、Control 和 Editor（隔离输出）均已构建通过。

`AssetPickerFilter` 暂不移动：`GalNet.Editor.Abstraction` 当前反向依赖 `GalNet.Control.Abstraction`，直接迁移会形成项目循环。它随 Phase 5 删除旧 UI 定制链后自然消失，而非引入错误依赖。

项目取舍：

| 项目 | Phase 2 决策 | 原因 |
| --- | --- | --- |
| `GalNet.Presentation.Abstractions` | 保留 | 面向所有宿主的稳定展示端口。 |
| `GalNet.Avalonia.Controls` | 保留 | 是用户可直接引用和覆写模板的控件库，不应并入示例应用。 |
| `GalNet.Storage.Abstractions` + `GalNet.Storage.FileSystem` | 保留为两个项目 | 前者定义可替换存储端口，后者只是默认磁盘实现；合并会重新耦合云端或平台存储。 |
| `GalNet.Editor.Abstraction` + `GalNet.Editor.Shared` | 暂不合并 | 前者是编辑器协议/扩展边界，后者是文件与命令实现；目前边界有效。 |
| `GalNet.Editor.Headless` | 保留，可在后续重命名为 `GalNet.Editor.Cli` | 它提供真实的项目创建、校验、命令执行与导出 CLI，不是游戏播放器重复实现。 |
| `GalNet.Headless` | 迁移后删除 | 当前是写死示例数据的旧游戏演示；由 `GalNet.Player.Console` 取代。 |
| `GalNet.Control.Tests` | 合并后删除 | 仅两类测试，独立测试程序集没有长期价值。 |
| `GalNet.Launcher.Headless` | 已删除 | 仅包含 `Hello World`，没有调用方或产品职责。 |
| `GalNet.Launcher` 及 Android/iOS/Browser/Desktop 外壳 | 暂不删除 | 目前仍是模板级实现，是否作为“游戏库启动器”保留与最终示例客户端是产品决策；Phase 2 不再让它引用或承载新的游戏客户端功能。 |

验收：Runtime 单元测试能借助假接口覆盖剧情推进、阻塞/非阻塞转场、特效、读档恢复和取消。

### Phase 3：命令行持久化补全

工作项：

1. 为命令行 Player 接入默认文件系统存档、读档和玩家变量持久化。
2. 把项目定义的逐字符特殊记号解析收敛为可复用实现，并用于命令行与后续 Avalonia 控件库。

验收：命令行可运行真实项目并支持持久化存档/全局变量；文本记号在各官方宿主一致。

### Phase 4：Avalonia 控件库

工作项：

1. 创建无业务逻辑的 `TypewriterTextBlock`，支持当前特殊记号、速度、完成与跳过。
2. 创建 `DialoguePresenter`、`ChoiceList`、`SaveSlotCard`、`SceneLayerHost` 等绑定型控件。
3. 提供默认 `Generic.axaml` 样式与资源字典；控件必须允许应用工程覆盖模板和样式。
4. 控件库不读取游戏文件、不创建 `GameEngine`、不注册存档或音频服务。

验收：任意 Avalonia 应用可引用控件库并自行绑定 ViewModel；控件库不依赖 `Runtime`、`Assets` 或编辑器。

### Phase 5：官方 Avalonia 示例客户端

工作项：

1. 创建 `GalNet.Sample.Avalonia` 作为完整可运行参考项目。
2. 在应用组合根注册内容、资源、存档、玩家变量、进度、设置、音频、视频、转场和特效服务。
3. 使用控件库实现默认游戏屏幕、对话、选项、存读档、标题、设置、画廊和截图流程。
4. 在组合根按 `Id` 分派转场/特效请求。转场首先实现 black、white、cross；特效保留动态入口，按需求由最终客户端添加，不设每个效果一个框架抽象类型。
5. 将视觉实现放在示例工程；开发者可替换单个服务、覆盖控件模板或复制整个工程，不影响 `Core`/`Runtime`。

验收：示例工程能加载发布包并完整运行；替换一个转场/特效/音频实现不修改运行时工程。

### Phase 6：编辑器瘦身与基础预览

工作项：

1. 删除编辑器对 `GalNet.Control`、`GameFlowFactory`、`DefaultGameView` 和完整游戏页面的依赖。
2. 移除 `UiProject`、预设注册、UI 调色板与 UI 定制面板；项目文件不再保存最终游戏 UI 配置。
3. 将游戏预览改为 `EditorPreviewHost + IGameView`：它用控件库显示基础图层、文本、选项和变量。
4. 预览样式只继承编辑器主题；不允许项目级 UI 定制。
5. 音频、视频、特效和复杂转场使用基础实现、占位或日志；预览目标是验证剧情而非还原最终客户端。
6. 保留临时预览数据构建、变量调试、重启预览和图编辑工作流。

验收：编辑器可编辑并基础预览项目，但不引用官方示例客户端，也不承担最终游戏 UI 定制职责。

### Phase 7：文件格式迁移、清理与发布

工作项：

1. 增加游戏文件格式版本，写入转场/特效新字段。
2. 固化新字段与校验规则；不提供旧字段兼容读取。
3. 更新导出器：只导出游戏数据和资源，不导出编辑器 UI 配置。
4. 更新架构、运行时、条目、文件格式与控件库文档。
5. 所有调用方迁移后，删除旧 `GalNet.Control`、`GalNet.Control.Abstraction` 及遗留 UI 定制链。

验收：新格式项目可由 CLI 和示例 Avalonia 客户端运行；最终依赖图无反向引用，完整测试通过。

## 5. 不在本次短期范围

- 编辑器直接加载、调试或热重载某个开发者自定义客户端工程。
- 允许编辑器设计最终客户端 UI。
- 云存档、平台账户、Steam 集成等具体基础设施实现。
- 为所有转场或特效定义统一参数架构；它们只共享 ID、生命周期字段和原始参数载荷。

## 6. 实施顺序

严格按 Phase 0 → 3 先稳定核心和 CLI，再实施控件库、示例客户端和编辑器。这样可以先以 `NullGameView` 与命令行验证状态机、存档和扩展命令语义，避免 UI 实现反复牵动核心协议。
