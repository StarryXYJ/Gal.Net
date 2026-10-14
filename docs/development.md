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

三套布局、三套配色和模板扩展示例见 [Themes README](../src/Presentation/GalNet.Avalonia.Themes/README.md)，契约见 [游戏页面规范](spec/control.md)。Sample 的 SampleCustomization.PlayerTheme 是实际启动配置入口。

## 创建一个自定义原语

先建立独立契约项目（仅依赖 Core，以及选用的 Contracts.Builtins），程序集名为 `GalNet.Project.Contracts`，公开 `GalNet.Project.Contracts.ProjectSchemas.Modules : IReadOnlyList<IEntryModule>` 静态属性。游戏引用该项目，另行注册执行模块；内容项目在 `Contracts/` 部署 DLL 及依赖，CLI/Editor 从这里加载同一集合。无需导出第二份手写 schema，也无需启动 Engine。模块名可重复，仅完整 typeId 冲突；部署和单平台截图示例见 [Sample.Contracts README](../src/Samples/GalNet.Sample.Contracts/README.md)。

可复用的值规则用 `GalNet.Core.Parameters.ValueDescriptor` 声明，Entry 参数通过 `new DynamicParameterDescriptor(name, valueDescriptor, isRequired, defaultValue)` 组合；CLI/设置等消费者也可调用 `ValueCodec` 和 `ValueValidation`，无需依赖 Entry 模型。描述允许传入 JSON 配置，构建时复制并冻结；JSON 名称和 converter 使用相同配置。必填/省略与默认值属于字段使用方，允许 null 单独指定。通用约束是值级规则，业务及跨字段校验继续手写；结构描述并非自定义代码的完整替代。公共工具与共享表单已实现，生成器尚未接入，不需要新增 Helper 项目。

Agent 可以直接写作者 JSON，再使用 `help --search <keyword> --json`、`files`、`find`、`references`、`validate`、`graph/group validate` 和 `group compile` 检查。需要稳定单元操作时通过 `execute` 使用帮助中的 JSON schema。命令协议、项目/Group 生命周期和 dry-run 边界以 [CLI README](../src/Editor/GalNet.Editor.Headless/README.md) 为准；当前版本一次调用一个进程，不提供旧扁平命令兼容别名。

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

字段可直接手写并组合进 Entry，表单顺序取 `DynamicParameterTable.Fields`：

```csharp
var amount = new FieldDescriptor("amount",
    new ValueDescriptor(typeof(int), constraints: JsonSerializer.SerializeToElement(new { minimum = 0, maximum = 100 })),
    defaultValue: JsonSerializer.SerializeToElement(1),
    editor: new EditorHints(ValueEditorIds.Slider, Step: 1), labelKey: "Amount");
var parameters = new DynamicParameterTable([new DynamicParameterDescriptor(amount)]);
```

编辑文本转换必须传完整 ValueDescriptor：`ValueCodec.FromEditorText(valueDescriptor, text)` / `ToEditorText(valueDescriptor, json)`，这样枚举 converter、约束及 JSON 配置不会丢失。真实 string 使用原始文本；其他类型（包括用 JSON string 表示的枚举）使用 JSON 文本。

设置的 `SettingItem<T>.Field` 提供同一字段结构；数值设置的范围校验与字段描述共用 ValueContract，EditorHints 可显式指定自定义 ID。设置 DefaultValue 可由宿主动态修改，重置及有效性语义仍归设置，不复制进冻结字段；ActionSetting 没有值字段，ChoiceSetting 的动态选项与标签仍由宿主通知。现有 NumericEditorKind 仅供尚未迁移的主题消费，后续表单工厂使用 EditorId。

共享 Avalonia 表单使用宿主提供的 registry 和 binding；主题可在组合入口贡献工厂和样式：

```csharp
using GalNet.Game.Controls.Forms;

var registry = EditorRegistry.CreateDefault();
registry.Register("game.nickname", context =>
{
    var lease = DefaultFieldEditors.CreateText(context);
    lease.Control.Classes.Add("game-nickname");
    return lease;
});
var form = new SchemaForm
{
    Schema = parameters.Fields,
    Registry = registry,
    Binding = field => bindings[field.Id] // 宿主提供的 IFieldBinding 字典
};
```

在玩家/编辑器根加载 `avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml`，或提供自己的表单模板。布局与视觉归主题，registry 实例和业务 binding 归宿主。binding 的 Changed 更新值/选项/可用性；CommitAsync 接收 FieldValue 与取消令牌，返回业务 ValueIssue 或 null。省略使用 default(FieldValue)，显式 null 使用包含 JSON null 的 FieldValue。Text/Numeric/JSON 工厂保留草稿，未编辑的空/null/default 不自动写回；宿主需要强制提交或删除时显式调用 Context.CommitAsync。自定义工厂返回独立控件及释放回调，借用业务服务不由 lease 释放；详见 [表单契约](spec/control.md#schema-字段表单)。Editor 和 Sample 设置已经接入：Editor 的通用参数提交经 Entry.Parameters 进入 workspace 历史/保存，专属资源/表达式等保留原控件；Sample 的 SettingItem<T> 提交继续经过 SettingsCoordinator 的异步应用与保存队列。IPlayerSettingsService.Editors 提供宿主 registry，三种主题复用同一个设置结构。

设置宿主创建一次 `EditorRegistry.CreateDefault()`，在 `IPlayerSettingsService.Editors` 返回它，并显式调用主题或项目的工厂扩展方法。新增普通设置直接复用 `SettingItem<T>`；业务 read/apply/Validator 留在设置对象，不复制到工厂。动态选项继续调用 `ChoiceSetting<T>.UpdateOptionsAsync`。仅改变外观时覆写主题模板/资源；改变控件时注册/替换工厂，例如：

```csharp
// 项目或主题提供扩展方法，宿主在展示前显式调用。
public static void RegisterProjectEditors(EditorRegistry registry)
{
    registry.Register("game.precise", DefaultFieldEditors.CreateNumeric);
}

var editors = EditorRegistry.CreateDefault();
RegisterProjectEditors(editors);
var wait = new IntSetting("wait", "Settings.Wait", 50, 0, 5000, 10,
    () => currentWait, (value, token) => ApplyWaitAsync(value, token))
{ Editor = new("game.precise") };
```

自定义控件工厂通过 context 读取类型化值、提交以及订阅变化，卸载时由 lease 退订控件自己的事件；不要另建保存队列。Editor 的对应扩展入口是项目作用域 Group 面板的 FieldEditors。项目 schema 只写 EditorId，不引用平台控件/工厂。当前 Editor 的字符串文档无法无损表示显式 null 字符串，适配器拒绝这类提交；需要原始 JSON 时使用 CLI。缺少自定义工厂会显示诊断，不自动替换为文本控件。

### DTO 字段生成与成员绑定

可在契约项目通过 Analyzer ProjectReference 引用 `GalNet.SchemaGenerator`（`ReferenceOutputAssembly="false"`，`GlobalPropertiesToRemove="OutputPath;OutDir"`），Roslyn 不进入运行时依赖。DTO 声明示例：

```csharp
[GenerateFieldSchema]
public partial class PulseOptions
{
    [Field(0, Required = true, AllowNull = false)]
    public string? Faction { get; set; }

    [Field(1, Minimum = 0, EditorId = "numeric")]
    public int Amount { get; set; } = 1;
}

var entry = DefaultPrimitiveEntryBase.ForDto<PulseOptions>("game.pulse");
```

生成 `PulseOptions.Schema` 以及 `IFieldDto` 的成员访问代码。ForDto 从同一 schema 建立参数表并记录 FieldDataType；不手写第二份参数描述，也不生成执行 module/保存逻辑。首轮支持顶层非泛型 partial class 上的公开可读写属性；private backing field、init-only 属性、非恒定默认表达式等报告 GNF001。字段顺序由 Field 的 order 明确指定，成员类型和常量属性初始化值来自 DTO；省略初始化值不等于声明 CLR 默认值为 schema 默认值。禁止执行任意 DTO 构造函数推断默认值。

字段 ID 使用 JsonPropertyName，未指定时为 camelCase；支持属性 JsonConverter、JsonRequired。Field 还可声明 Required/AllowNull、范围/MaxLength/ValueFormat，以及 EditorId/Step/Format/UnitKey/标签和说明。C# nullable 注解不等于 JSON null/presence 策略；AllowNull 未指定时遵循现有 ValueDescriptor 的 CLR 类型规则。复杂业务校验继续传给 ForDto 的 validateArguments。

UI 不必继承固定基类。普通 DTO 使用 `DtoBinding.Refresh()` 显式刷新；实现 INotifyPropertyChanged 会自动通知，ObservableObject 是一种可选实现。CommunityToolkit 的 `[ObservableProperty] public partial int Amount { get; set; }` 可同时标记 Field，两个生成器读取同一属性声明；不要求生成器读取彼此产物，首轮不支持给私有 ObservableProperty backing field 标记 Field。

```csharp
using var binding = new DtoBinding(dto);
var form = new SchemaForm
{
    Schema = binding.Fields,
    Binding = binding.Binding,
    Registry = EditorRegistry.CreateDefault()
};
```

SchemaForm 只需要字段列表和 IFieldBinding，不要求数据源是 DTO。DtoBinding 的普通提交经值校验后写成员；async apply/save 使用其宿主提交回调，由宿主更新数据并通知/刷新。绑定借用 DTO，表单拥有草稿/控件订阅；宿主在换对象时释放 DtoBinding。对象/数组继续用 JSON 编辑器，不生成递归面板。

Editor 对 DTO 契约 Entry 使用整组 SchemaForm 和 EntryParameterFormBinding，直接读写作者参数字典，不实例化 DTO。省略与显式 null 保留在文档层；提交替换 Parameters，由既有 workspace 历史/保存链路观察。默认值仅用于展示，打开面板不写入文档；无效但可解析的 JSON 值仍可读取并修正。旧字符串作者文档无法无损表达的值（如显式 null 字符串）仍明确拒绝。手写契约和专属资源/表达式控件继续使用原路径。

执行模块可选择强类型入口，与契约使用相同 DTO：

```csharp
var implementation = PrimitiveImplementation.For<PulseOptions>(
    "game.pulse", (context, options) =>
        new ImmediatePrimitiveInstance(
            () => ApplyPulse(options.Faction!, options.Amount),
            batchId: context.BatchId));
```

CompositeGameView 构建时检查执行工厂的 DTO 类型与契约 FieldDataType 一致。Dispatch 先用契约规范化和校验 JSON，再由工厂使用每个字段的 ValueCodec 和生成成员访问填充独立 DTO；参数默认值和属性 converter 不另写一份。Context 继续提供 Runtime、BatchId 和 ScopeCancellation，业务 DTO 不需要 ObservableObject。未提供且无 schema 默认值的字段保持新 DTO 的成员值；需要判断 presence 时查询 context.Arguments，其中包含规范化后的默认值，并非原始作者输入。普通 JSON 工厂仍可用于手写/动态契约；For<T> 不支持直接把未经规范化的参数交给工厂执行。

Sample 的 ScreenshotOptions 同时作为契约定义和截图执行参数；Editor 只消费其 schema，不维护副本。见 [阶段证据](../features/F-20261013-02-parameter-and-sample-simplification/phase-2e.md)。

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

`AddBuiltinSchemas` 不创建播放器或运行期状态；`AddBuiltinImplementations(catalog, ...)` 只暴露传入 catalog 声明的内置 primitive，保留整个组件的共享状态和释放所有权；`AddBuiltinAudio`、`AddBuiltinAnimations`、`AddBuiltinVariables` 等按领域选择完整组件实现。也可以完全不使用内置契约。

作者契约位于独立的 `GalNet.Contracts.Builtins` 项目，只依赖 Core。`BuiltinSchemas.CreateAllModules()` 返回模块列表；可单独选择 `CreateDialogueModule()`、`CreateAudioModule()` 等组件，使用 `new TargetProfileEntryCatalog(modules)` 构造通用 catalog。游戏注册独立契约项目的 `ProjectSchemas.Modules` 后，将 `registration.BuildCatalog()` 传给内置实现选择入口；Editor preview 使用 `UseCatalog(projectCatalog)`，不能扩充项目的 schema 集合。参数类型、内置 Entry DTO 和 Composite 使用 `GalNet.Contracts.Builtins` 命名空间，执行模块仍使用 `GalNet.Primitives.Builtins`。

只注册 schema 的 view 可以构建，但不能执行尚未提供实现的指令。宿主启动前通过 `RequireInstructions(compiledTypes)` 检查实际编译内容的执行覆盖；`RequireExecutionCoverage()` 可要求整个契约集合都有实现。Engine 创建时也检查 Graph 引用的所有 primitive；缺失实现直接报错，不静默跳过。Editor 不会自动获得自定义平台执行模块。没有 presenter 的内置模块不等于具备平台播放能力：部分模块可只提交状态，对话等需要交互的能力仍要求对应 presenter。

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

## Sample 项目组合入口

Avalonia 的 `src/Samples/GalNet.Sample.Avalonia/SampleCustomization.cs` 集中项目选择：`ConfigureServices` 选择主题、本地化启动和可选调试（`enableDebug`）；`CreateFieldEditors` 创建每个设置作用域的默认/自定义字段工厂；`CreateContentCatalogs`、`ConfigureDecoders`、`SelectTexturePreloadsAsync` 和 `SelectEffectProgramsAsync` 选择资源/Gallery 类型、解码器和预加载策略；`ConfigureExecution` 选择项目契约并注册内置及自定义执行模块。业务工厂借用明确的平台对象，不通过服务定位器查找运行中的对象。

契约的唯一选择源仍是 `ProjectSchemas.Modules`；游戏的 Avalonia 和 Headless 经 `ProjectSchemas.CreateCatalog()` 构造 catalog，Editor/CLI 继续加载项目 DLL 的 Modules。只选择部分内置时修改 Modules，游戏不会自动追加全量内置 schema。Headless 不注册平台截图，运行含截图的剧情时明确报告缺少实现。

新增截图一类的业务时：在契约项目声明参数/schema 并加入 Modules，在平台业务文件实现 module，然后在 SampleCustomization.ConfigureExecution 注册。需要专属字段编辑器时在 CreateFieldEditors 注册工厂，需要新资源类型时在资源策略入口配置。App、Session、Runner 和关闭流程不需要认识新 module；BuildView 将执行模块所有权转交给 view，模块只释放自己的资源，借用的页面和媒体端口由 Presentation 在 view 之后释放。契约变化后重新部署 DLL 给工具使用。

App 负责应用初始化/关闭；Session 协调游戏、设置和存档；Runner 拥有单次 Engine；ResourceScope 持有 assets/预加载租约；Presentation 持有平台端口；SaveSession 持有 profile 的变量、进度和 Gallery resolver。设置条目和首次启动策略分别留在 SamplePlayerSettingsService 和 SampleStartupService，二次开发者可以直接改业务策略，不需把它们合并到生命周期类。默认 layer resolver 使用预加载纹理并拥有 fallback，Gallery resolver 使用独立资源租约，两者不能共享释放逻辑。
