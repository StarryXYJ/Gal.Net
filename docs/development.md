# 二次开发手册

本手册面向“基于 GalNet 做自己的游戏/客户端”的开发者。默认原则是：**不修改 `src/Shared` 中的 Core、Runtime、Assets、Presentation.Abstractions 或 Builtins；在自己的外层程序集和组合根中扩展。** Shared 层是可升级的框架契约，而不是项目玩法代码的位置。

## 先决定扩展落点

| 需求 | 推荐落点 | 不应做的事 |
| --- | --- | --- |
| 自定义剧情指令 | 自己的 Entry Module | 往 Core 写某个项目专属 primitive |
| 自定义即时或异步玩法逻辑 | 自己的 `PrimitiveInstance` | 让 Engine 知道项目的指令名 |
| 高级作者指令 | 自己的 composite，编译为 primitive | 让 Runtime 直接执行 composite |
| 新页面、主题、窗口或媒体后端 | `MyGame.View` / 客户端宿主 | 把项目布局写入 `GalNet.Avalonia.GameView` |
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

## 配置玩家界面主题

引用可选 `GalNet.Avalonia.Themes`，在创建游戏 Scope 前配置布局和配色：

```csharp
services.AddAvaloniaGameViewPages(
    theme: new ClassicTheme(GamePalette.Ocean),
    configureViews: views => views.Register<TitlePageViewModel, MyTitlePage>());
```

主题参数必需，普通页面没有默认回退；也可实现自己的 IGameLayoutTheme 而不引用内置 Themes。主题注册先于宿主覆盖；仅替换一个页面不需要另写主题。GameShell 自动加载对应玩家样式，其他玩家根用同一主题的 CreateStyles 加载。普通页保持 View + 共享 ViewModel；GamePage 通过 ControlTheme 安排稳定场景/对白/选项内容。当前配置在运行期固定。

三套布局、三套配色和模板扩展示例见 [Themes README](../src/Presentation/GalNet.Avalonia.Themes/README.md)，契约见 [游戏页面规范](spec/control.md)。Sample 的 App.PlayerTheme 是实际启动配置入口。

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

### 2. 分别定义契约和执行模块

schema 不引用游戏服务或平台对象。执行模块持有共享数据和服务，贡献完整指令 ID 与实例工厂：

```csharp
using GalNet.Presentation.Abstractions.Execution;

public sealed class MyGameSchemas : EntryModuleBase
{
    public MyGameSchemas()
        : base("mygame",
        [
            new DefaultPrimitiveEntryBase(
                "mygame.reputation.add",
                new DynamicParameterTable(
                [
                    new DynamicParameterDescriptor("faction", typeof(string), isRequired: true),
                    new DynamicParameterDescriptor("amount", typeof(int),
                        defaultValue: JsonSerializer.SerializeToElement(1))
                ]))
        ])
    { }
}

public sealed class MyGameModule : PrimitiveModule
{
    public MyGameModule(IReputationService reputationService)
        : base(
        [
            new PrimitiveImplementation("mygame.reputation.add", context =>
                new ImmediatePrimitiveInstance(() =>
                {
                    var faction = context.Arguments.GetProperty("faction").GetString()!;
                    var amount = context.Arguments.GetProperty("amount").GetInt32();
                    reputationService.Add(faction, amount);
                }, batchId: context.BatchId))
        ])
    { }
}
```

不必逐条在组合根注册。一个领域模块可以贡献多个指令，并在实例间共享轨道、缓存或服务；也可用 `RegisterInstruction` 单独贡献指令。同前缀可以由多个模块贡献不同指令，只有完整 type ID 重复才冲突。schema 模块无资源生命周期；执行模块可覆写 `Dispose` / `DisposeAsync`。

异步或可跳过行为继承 `PrimitiveInstance`，在 `OnDispatch` 启动受 scope cancellation 控制的任务；成功、取消和失败都应完成实例，失败需明确诊断。不要用未经观察异常的 `async void`。创建实例时原样传入 `context.BatchId`，`OnSkip` 必须幂等且只处理自己的资源。non-blocking 原语应先提交可存档状态，再启动展示。

### 3. 在游戏组合根独立注册

```csharp
using GalNet.Presentation.Abstractions.View;

// 编译器只需契约，不构造执行服务。
using var registration = new GameRegistration()
    .RegisterSchema(new MyGameSchemas())
    .RegisterImplementation(new MyGameModule(reputationService));
var profile = registration.BuildCatalog();

// BuildView 冻结注册并将执行模块所有权转移给 view。
await using var view = registration.BuildView();
```

编译期 target profile 和运行期 view 使用等价的 schema；执行模块只在运行期创建。宿主先停止/释放 Engine，再异步释放 view，最后释放它借用的 presenter 和平台资源。注册失败时释放 registration，避免遗留已创建的执行模块。

平台实现只需要提供 presenter/后端，不需要实现 Runner、跟踪 Engine continuation 或自行推断剧情结束。宿主调用 `AdvanceAsync()`，通过 `StateChanged` 接收统一运行状态，关闭时调用 `StopAsync()` 或异步释放 Engine。选择或阻塞实例自然完成后的推进与终态通知由 Engine 负责。页面切换前的快照恢复及 Builtins 展示重放仍由宿主编排。

### 4. 自由选择内置能力

```csharp
// 主入口：所有内置 schema 与实现，平台能力通过端口提供。
await using var full = new GameRegistration()
    .AddBuiltins(dialoguePresenter: dialogue, audioHost: audioHost)
    .BuildView();

// 内置 schema + 部分内置逻辑 + 自己的实现。
await using var custom = new GameRegistration()
    .AddBuiltinSchemas()
    .AddBuiltinVariables()
    .RegisterImplementation(new MyPlatformModule())
    .BuildView();
```

`AddBuiltinSchemas` 不创建播放器或运行期状态；`AddBuiltinImplementations` 注册全部内置实现；`AddBuiltinAudio`、`AddBuiltinAnimations`、`AddBuiltinVariables` 等按领域选择实现。也可以完全不使用内置契约。

作者契约位于独立的 `GalNet.Contracts.Builtins` 项目，只依赖 Core。`BuiltinSchemas.CreateAllModules()` 返回模块列表；可单独选择 `CreateDialogueModule()`、`CreateAudioModule()` 等组件，使用 `new TargetProfileEntryCatalog(modules)` 构造通用 catalog。选择部分 schema 时应注册与之对应的执行模块，不能直接注册全部内置实现。参数类型、内置 Entry DTO 和 Composite 使用 `GalNet.Contracts.Builtins` 命名空间，执行模块仍使用 `GalNet.Primitives.Builtins`。

只注册 schema 的 view 可以构建，但不能执行尚未提供实现的指令。Engine 创建时检查 Graph 引用的所有 primitive；缺失实现直接报错，不静默跳过。没有 presenter 的内置模块不等于具备平台播放能力：部分模块可只提交状态，对话等需要交互的能力仍要求对应 presenter。

音频用组合而非强制继承：`BuiltinAudioModule`/`AudioRuntime` 管理轨道与 voice，`IAudioPlaybackHost` 管理平台播放。Avalonia 可提供 LibVLC host，Unity 可提供原生音频 host；若轨道语义也需替换，只注册内置 schema 和自己的音频执行模块。

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

- 在 `MyGame.View` 使用 `AddAvaloniaGameViewPages(theme)` 的注册回调替换主题 View，或追加页面；运行期 registry 构建后不可修改。
- 新 Gallery 类型通过 `IGalleryPageRegistry` 按精确 `typeId` 注册，不按资源类型推断 fallback。
- 新资源类型要在组合期注册同一份 `IResourceTypeCatalog`；decoder 按 `(typeId, CLR 类型)` 注册到资产管理器。开发内容、导出器、安装内容和预览必须使用同一份冻结 catalog。
- 平台媒体若需要文件路径，先通过 `IAssetManager` acquire 内容，再实体化到受控会话临时目录；不要扫描项目目录或 PAK。
- 每个游戏会话使用独立的 Runtime、页面 scope 与模块实例；切换项目、重载或关闭时先停止 runner，再释放资源与 scope。

页面和资源边界的详细约束见[Avalonia 游戏页面](spec/control.md)、[资源](spec/assets.md)和[架构](spec/architecture.md)。

## 二次开发的验收清单

- 自定义 type ID 在目标 profile 中存在，且无重复。
- Raw Group 可编译为只含 primitive 的 `.galgroup`。
- Runtime 的 view 使用等价 schema，且 Graph 所需指令都有实现。
- 参数默认值、必填项和 JSON 类型有测试。
- blocking、skip、取消和完成事件分别有测试。
- 任何影响长期画面的状态先写入可恢复的状态模型，再调用平台呈现。
- 读档后验证状态重放，而不是只验证第一次播放。
- 项目程序集没有让 Shared/Core/Runtime 依赖具体 UI、渲染或文件系统实现。
