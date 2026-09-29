# 二次开发手册

本手册面向“基于 GalNet 做自己的游戏/客户端”的开发者。默认原则是：**不修改 `src/Shared` 中的 Core、Runtime、Assets、Presentation.Abstractions 或 Builtins；在自己的外层程序集和组合根中扩展。** Shared 层是可升级的框架契约，而不是项目玩法代码的位置。

## 先决定扩展落点

| 需求 | 推荐落点 | 不应做的事 |
| --- | --- | --- |
| 自定义剧情指令 | 自己的 Entry Module | 往 Core 写某个项目专属 primitive |
| 自定义即时或异步玩法逻辑 | 自己的 `PrimitiveInstance` | 让 Engine 知道项目的指令名 |
| 高级作者指令 | 自己的 composite，编译为 primitive | 让 Runtime 直接执行 composite |
| 新页面、主题、窗口或媒体后端 | `MyGame.View` / 客户端宿主 | 修改 `GalNet.Avalonia.GameView` 默认页面 |
| 新资源类型、decoder、Gallery 页面 | 外层注册和平台程序集 | 让 Core 引用 Avalonia/Skia |
| 项目存档位置、账户、平台服务 | 客户端宿主基础设施 | 修改 Runtime 的状态机 |

推荐结构：

```text
MyGame/
  MyGame.Gameplay/       自定义 module、primitive、纯玩法服务
  MyGame.View/           Avalonia 页面、主题、presenter、renderer、decoder
  MyGame.Client/         Composition root、窗口和平台服务
  MyGame.Editor/         可选：编辑器扩展和同一 target profile
```

`MyGame.Gameplay` 可以依赖 Core、Runtime 所需的公开协议和 Presentation.Abstractions；`MyGame.View` 才依赖 Avalonia 或其他具体渲染技术。项目自有类型不要声明在 `GalNet.Core.*` 命名空间下。

## 创建一个自定义原语

### 1. 选定稳定 type ID 与参数

type ID 使用点分隔稳定名称，例如 `mygame.reputation.add`。模块 ID 必须是小写且不含点，例如 `mygame`。参数 schema 是编辑器、编译器和运行时共用的唯一事实来源；参数使用 JSON 类型，不写 CLR 类型名。

```csharp
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;

var reputationParameters = new DynamicParameterTable(
[
    new DynamicParameterDescriptor("faction", typeof(string), isRequired: true),
    new DynamicParameterDescriptor(
        "amount", typeof(int),
        defaultValue: JsonSerializer.SerializeToElement(1))
]);
```

`DynamicParameterDescriptor` 可声明 required、JSON 默认值与 JSON constraints。若主要目的是提供编辑器控件提示，也可用 `EntrySchema.DynamicParameters(...)` 由 `EntryParameterType` 构建 schema。

### 2. 选择原语的生命周期

同步且立即结束的逻辑使用 `ImmediatePrimitiveInstance`：

```csharp
new DefaultPrimitiveEntryBase(
    "mygame.reputation.add",
    reputationParameters,
    context => new ImmediatePrimitiveInstance(() =>
    {
        var faction = context.Arguments.GetProperty("faction").GetString()!;
        var amount = context.Arguments.GetProperty("amount").GetInt32();
        reputationService.Add(faction, amount);
    }, batchId: context.BatchId));
```

有等待、动画或用户跳过语义时，继承 `PrimitiveInstance`：

```csharp
public sealed class WaitForSignalInstance : PrimitiveInstance
{
    private readonly IMySignalService _signals;
    private readonly string _signal;
    private CancellationTokenSource? _cancellation;

    public WaitForSignalInstance(IMySignalService signals, string signal,
        string? batchId, CancellationToken scopeCancellation) : base(batchId)
    {
        _signals = signals;
        _signal = signal;
        ScopeCancellation = scopeCancellation;
    }

    private CancellationToken ScopeCancellation { get; }
    public override bool IsBlocking => true;
    public override bool IsSkippable => true;

    protected override async void OnDispatch()
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(ScopeCancellation);
        try { await _signals.WaitAsync(_signal, _cancellation.Token); }
        catch (OperationCanceledException) { }
        finally { TryComplete(); }
    }

    protected override void OnSkip() => _cancellation?.Cancel();
}
```

原语实现必须遵守以下规则：

- 让基类调用 `Dispatch()`；只在 `OnDispatch()` 启动一次行为。
- 成功、取消或错误路径最终都只调用一次 `TryComplete()`。
- 创建实例时原样传入 `context.BatchId`；不要生成不同的 batch ID。
- `OnSkip()` 必须幂等，且只处理本实例自己的资源。
- non-blocking 原语在返回前提交可存档状态；平台 Task、控件和播放游标不属于存档。

### 3. 将定义组成模块

```csharp
public sealed class MyGameModule : EntryModuleBase
{
    private static readonly DynamicParameterTable ReputationParameters = new(
    [
        new DynamicParameterDescriptor("faction", typeof(string), isRequired: true),
        new DynamicParameterDescriptor(
            "amount", typeof(int),
            defaultValue: JsonSerializer.SerializeToElement(1))
    ]);

    public MyGameModule(IReputationService reputationService)
        : base("mygame",
        [
            new DefaultPrimitiveEntryBase(
                "mygame.reputation.add",
                ReputationParameters,
                context => new ImmediatePrimitiveInstance(() =>
                {
                    var faction = context.Arguments.GetProperty("faction").GetString()!;
                    var amount = context.Arguments.GetProperty("amount").GetInt32();
                    reputationService.Add(faction, amount);
                }, batchId: context.BatchId))
        ])
    { }
}
```

模块构造时会冻结 primitive 与 composite 两张表，并拒绝模块内或跨模块的重复 type ID。模块有资源时覆写 `Dispose()`；`CompositeGameView` 会按相反顺序释放已挂载模块。

### 4. 同时接入作者期与运行期

必须让编辑器/编译器的 target profile 与运行时 view 使用**等价的模块集合和 schema**。实际宿主可以各自创建模块实例（例如编辑器没有 renderer，游戏有 renderer），但 type ID、参数和 composite 展开契约必须一致。

```csharp
// 编辑器或导出器：决定允许作者使用哪些 entry。
var authoringModules = new IEntryModule[]
{
    new MyGameModule(editorReputationService)
};
var profile = new TargetProfileEntryCatalog(authoringModules);

// 游戏会话：提供同一批 primitive 的实际工厂和平台依赖。
using IGameView view = new CompositeGameView(
[
    new MyGameModule(runtimeReputationService)
]);
```

只把模块挂到 `CompositeGameView` 会使 Runtime 能执行、编辑器却不能作者化/编译；只挂到 `TargetProfileEntryCatalog` 则相反。不要依赖全局内置目录作为回退。

## 创建 composite（可选）

Composite 用于改善作者体验，不是另一种 Runtime 指令。继承 `CompositeEntry`，在 `Compile(EntryCompileContext)` 中返回 primitive 序列，并通过 `DefaultCompositeEntryBase` 注册到模块的 `compositeEntries`。

```csharp
public sealed class AlertCompositeEntry : CompositeEntry
{
    public override string Type => "mygame.alert";

    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context) =>
    [
        new PrimitiveEntry("mygame.ui.showAlert",
            JsonSerializer.SerializeToElement(new { message = Values["message"] }))
        { Condition = context.Condition }
    ];
}
```

展开得到的每一个 primitive 都必须已在 target profile 注册。Composite 本身不会写入 `.galgroup`，也不应该创建平台对象。

## 平台相关逻辑怎么写

有两种合规路径：

1. **推荐：窄端口。** 在 `MyGame.Gameplay` 定义项目自己的小接口，例如 `IMyNotificationPresenter`；Avalonia/Unity/其他宿主在 `MyGame.View` 实现它。primitive 依赖接口，逻辑仍可 Headless 测试。
2. **单平台项目：直接依赖。** 让模块闭包捕获 Avalonia 页面服务、Skia renderer 或媒体后端也可行，但该模块必须留在 `MyGame.View` 或客户端层。它将无法用于 Headless/其他前端，且读档后仍需由宿主按 `SceneState` 或自己的稳定状态重建视觉结果。

无论哪种路径，都不能让 `GalNet.Core`、`GalNet.Runtime` 或 `GalNet.Editor.Shared` 反向依赖具体平台程序集。

## 扩展页面、资源和宿主

- 在 `MyGame.View` 使用 `AddAvaloniaGameViewPages()` 的注册回调替换默认 View，或追加页面；运行期 registry 构建后不可修改。
- 新 Gallery 类型通过 `IGalleryPageRegistry` 按精确 `typeId` 注册，不按资源类型推断 fallback。
- 新资源类型要在组合期注册同一份 `IResourceTypeCatalog`；decoder 按 `(typeId, CLR 类型)` 注册到资产管理器。开发内容、导出器、安装内容和预览必须使用同一份冻结 catalog。
- 平台媒体若需要文件路径，先通过 `IAssetManager` acquire 内容，再实体化到受控会话临时目录；不要扫描项目目录或 PAK。
- 每个游戏会话使用独立的 Runtime、页面 scope 与模块实例；切换项目、重载或关闭时先停止 runner，再释放资源与 scope。

页面和资源边界的详细约束见[Avalonia 游戏页面](spec/control.md)、[资源](spec/assets.md)和[架构](spec/architecture.md)。

## 二次开发的验收清单

- 自定义 type ID 在目标 profile 中存在，且无重复。
- Raw Group 可编译为只含 primitive 的 `.galgroup`。
- Runtime 的 `CompositeGameView` 挂载等价模块集合。
- 参数默认值、必填项和 JSON 类型有测试。
- blocking、skip、取消和完成事件分别有测试。
- 任何影响长期画面的状态先写入可恢复的状态模型，再调用平台呈现。
- 读档后验证状态重放，而不是只验证第一次播放。
- 项目程序集没有让 Shared/Core/Runtime 依赖具体 UI、渲染或文件系统实现。
