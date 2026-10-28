# 定制 Avalonia Sample

`GalNet.Sample.Avalonia` 是项目级组合根示例：保留启动参数、项目选择、本地化和调试策略；通用会话、页面与展示组合应优先复用 `Avalonia.GameHost`、`Avalonia.GameView` 和 Themes，而不是复制其生命周期代码。

## 调试构建与玩家发行

Sample 通过 MSBuild 属性 `EnableGameDebugging` 控制游戏调试能力，启用时定义 `GALNET_GAME_DEBUG`。Debug 配置默认开启，Release 默认关闭；也可以显式构建带调试能力的优化版本：

```powershell
dotnet build src/Samples/GalNet.Sample.Avalonia -c Release -p:EnableGameDebugging=true
```

关闭时不编译调试服务、存档辅助工具、日志窗口和菜单，不引用 `GalNet.Debugging`，也不接入 Avalonia Developer Tools。`--debug-*` 参数明确报错，不会静默启动一个无法连接 Editor 的玩家程序。普通游戏路径、profile 参数和运行错误日志仍然保留。

`ConfigureServices` 的 `enableDebug` 只允许在已经编入调试能力的构建中抑制服务注册，不能给玩家构建重新启用调试。Release 关闭模式使用独立的 `packages.lock.NoGameDebugging.json`；切换开关后必须重新 restore，不能复用另一种依赖图的 `--no-restore` 输出。WindowsReadyToRun 发布沿用该 profile 的独立锁文件。

仓库发行脚本显式关闭游戏调试，并用 `CopyOutputSymbolsToPublishDirectory=false` 排除 PDB；不改变契约程序集的编译参数和严格字节身份。符号文件仍可在构建输出中供开发者保留。此开关不改变游戏内容选择、PAK 覆盖顺序或补丁策略。

默认宿主的扩展目标是让项目自己实现功能。手机、小游戏、商城等可以使用项目服务、XAML、primitive 与状态恢复组合，不需要给框架增加同名业务系统。

## 替换服务与页面行为

`AddAvaloniaGameViewPages` 通过 `TryAddScoped` 提供默认服务，项目可以在调用前注册替代实现，或在调用后显式覆盖。标题、设置、游戏页面和 Shell 的默认 VM 可派生；标题命令的处理方法可以重载。注册派生 VM 的别名和实际 View：

```csharp
services.AddScoped<TitlePageViewModel, MyTitleViewModel>();
services.AddScoped<MyNotebookViewModel>();
services.AddAvaloniaGameViewPages(theme,
    configureViews: views =>
    {
        views.Register<MyTitleViewModel, MyTitleView>();
        views.Register<MyNotebookViewModel, MyNotebookView>();
    });
```

`MyTitleViewModel` 派生 `TitlePageViewModel`，自己的按钮命令可调用 `IGameNavigationService.Navigate<MyNotebookViewModel>()`；返回使用现有导航历史。工厂按实际 VM 类型选择 View，因此派生类型也需要映射。更大幅度的定制可以替换 `IGameNavigationService`、`IPageViewFactory`，或由自己的窗口构造 Shell。页面的 XAML 和业务服务不需要字段生成器。

## 自有设置模型与手写 XAML

默认会话消费 `IAvaloniaPlayerSettings`，仅要求投影 `PlaybackOptions`、`AudioMix`、`TextSpeed`，接收玩家存储与音频声明，并提供变更通知和 `FlushAsync`。默认实现是 `AvaloniaPlayerSettingsService`，项目可以实现接口、持有自己的 DTO 和持久化规则，再注册到 DI。`IPlayerSettingsService` 应解析到同一实例；`IPlayerStartup` 可替换首次启动策略。窗口绑定和显示恢复可按需要实现。

自动表单使用 `Categories` 和 `Editors`；手写设置页可以直接绑定自己的模型/命令，分类可为空。复杂偏好可以拆成玩家变量，或由项目显式序列化到自己的 profile 数据；不要把完整设置 DTO 放进 Engine。应用后发布 `SettingsChanged`，默认宿主将投影值更新到当前运行。预览、草稿取消、校验和格式迁移由项目设置服务定义。

实际可运行示例见 `test/GalNet.IntegrationTests/ProjectSettingsView.axaml` 与 `SampleExtensibilityTests.cs`：独立偏好模型、手写 XAML、应用命令、保存重开，以及自定义标题按钮/页面。

## 玩家与运行绑定

`AvaloniaGameHostOptions.AttachPlayer` 在内容、首次启动及设置恢复完成后接入，收到借用的 `PlayerSession`、内容与 profile 路径；`AttachRun` 在每次运行 Prepare 完成后接入，收到当前 Engine。回调返回的 `IDisposable` 由宿主释放；若同时实现 `IAsyncDisposable`，宿主等待异步释放，项目可取消并排空自己的后台任务。

运行替换/停止先终止 Engine，再释放运行绑定；玩家绑定在所有运行结束后、玩家存储和资源释放前结束。回调抛错会进入正常准备失败清理。需要多个项目服务时，在组合根中组合它们的绑定；不要在回调中留下未观察的任务或 `async void`。

活动运行中的外部变量修改使用：

```csharp
await engine.SetVariablesAsync(new Dictionary<string, object>
{
    ["save.phoneReply"] = "accept",
    ["player.chapterUnlocked"] = true
}, cancellationToken);
```

条件扣款、计数累加可用 `UpdateVariablesAsync(current => changes)`：在同一执行门内读取带作用域前缀的独立快照并计算修改批次，校验通过后提交；计算委托必须同步，抛错时不修改变量。

名称必须显式带 `save.` 或 `player.`。入口先验证全部值，再通过 Engine 唯一执行门修改；排队请求遇到恢复/停止会失效。显式修改的存档变量会合入稳定快照，保留原游标和呈现状态；玩家变量仍由玩家服务 flush，默认宿主退出时等待落盘。读取使用 `engine.Runtime.GetVariables(scope)` 的独立快照。玩家/profile 服务可以在没有活动运行时保存玩家级数据；不要在活动运行中绕过该入口修改剧情要读取的变量。

此入口不是任意业务事务、商城订单协议或小游戏原地续玩的实现。项目仍负责奖励幂等、交互窗口校验、自定义状态及恢复策略；后台操作应绑定其实际所属的玩家或运行生命周期。

## 单图章节启动参数

项目保留唯一 Graph，章节可以在图入口按变量分支。默认标题 VM 提供 `StartNewGameWithVariablesCommand`，参数为 `IReadOnlyDictionary<string, object>`；派生首页 VM 可暴露不同字典供章节按钮绑定，例如 `new Dictionary<string, object> { ["save.chapter"] = 2 }`，无需每个按钮单独实现启动流程或填写 Graph ID。标题命令沿用 Loading → Prepare → 游戏页 → Begin 的流程。

自定义应用也可通过 `IParameterizedGameSessionService.PrepareNewGameAsync(initialSaveVariables, ct)` 准备，再调用继承的 `BeginPreparedGameAsync`；不需要页面切换时可直接调用带字典的 `StartNewGameAsync`。默认 AvaloniaGameSession 实现该可选接口，原有无参启动和其他宿主实现无需强制接收新参数。

初始值只允许显式 `save.` 键及 bool、Int32 范围整数、有限 Single 范围数值和字符串。共享 GameSession 在替换运行前复制并验证参数，在新 Run 初始化默认值和 Prepare 完成后、执行入口前一次应用；参数不会先写进旧 Run。默认宿主每次新建运行从 Graph 的 save 声明初始化，player 已存值保留，缺省 player 值才补入。读档只恢复存档，不重新应用标题页的章节参数；需要持久记录解锁章节时，另用 player 变量。

外部 Editor 调试的启动配置、运行变量面板、路线定位和可选宿主接入见[对接外部游戏调试](external-game-debugging.md)。

只需用两个字符串调用项目逻辑时，可用内置 `custom.invoke`，在运行组合中注册 action，无需为每个 action 新建项目契约；见[通用 action 注册与模式语义](custom-actions.md)。

## 推荐改造顺序

1. **建立项目契约。** 复制或引用 `GalNet.Sample.Contracts`，在 `ProjectSchemas.Modules` 中列出项目允许的 `IEntryModule`。构建后用 `scripts/contracts/build-project-contracts.ps1` 部署到内容项目的 `Contracts/`。CLI/Editor 与玩家必须消费同一契约集合。
2. **配置运行组合。** 在 Sample 的 `SampleCustomization` 中配置 catalog、执行模块、资源/玩家存储、音频 host 和项目启动策略。把项目专属 primitive 注册在这里；不要修改 `App`、Session 或关闭链来接入一条业务能力。
3. **定制外观。** 通过 `AddAvaloniaGameViewPages` 选择 `ClassicTheme`、`MinimalTheme` 或 `PaperTheme`，也可在 `configureViews` 覆盖单个页面。运行期主题切换不受支持。
4. **扩展项目功能。** schema/DTO 与 composite 放在契约项目；执行模块放在 Avalonia 或项目执行程序集。参数 DTO 用 `GenerateFieldSchema`/字段特性生成 descriptor，平台控件仅在页面/设置工厂中处理。

## 页面与主题边界

- `GamePage` 是稳定的游戏页面宿主；其模板必须保留 `PART_Scene`、`PART_Dialogue`、`PART_Nvl` 和 `PART_Choice` 四个 ContentPresenter。
- 对话与 NVL 的内部部件分别需要 `PART_Typewriter`、`PART_Lines` 和 `PART_ScrollViewer`。不要在剧情播放中重建这些部件。
- 主题页面复用共享 ViewModel 和命令；只替换布局/样式，不复制 presenter、媒体或游戏会话逻辑。
- Palette 使用完整的语义资源字典。宿主若覆写资源，以玩家根的资源为准；不要跨多个根复用同一 `Styles` 实例。

## 上线前检查

- Project `Contracts/` 是与当前 GalNet API 匹配的可信构建产物；重新构建游戏或 CLI 不会自动更新该目录。
- 自定义 entry 已在 CLI 校验/编译、Avalonia 运行和保存恢复路径中测试；Headless 缺少平台实现时应清晰报错。
- 主题的普通页面映射完整，`GamePage` 布局可在目标分辨率和输入方式下工作。
- 退出、切换项目和读档均由会话链路释放旧的 presentation、媒体与资源。

主题的可复制示例在[Themes README](../../src/Presentation/GalNet.Avalonia.Themes/README.md)；运行与页面的精确约束见[Avalonia 游戏页面规范](../spec/control.md)。

## 注册 DTO 参数控件

共享 UI 构建位于 GalNet.Avalonia.Controls 项目，命名空间为 GalNet.Game.Controls.Forms。Sample 的 SampleCustomization.CreateFieldEditors 是组合入口：先 CreateDefault，再 RegisterDto<T>、RegisterMember<T> 或 RegisterDefault<T>；内置已有类型用 ReplaceDefault<T>，最后 Freeze。每个工厂返回新的 FieldEditorLease，不能重用已挂载 Control。

~~~csharp
var editors = EditorRegistry.CreateDefault();
editors.RegisterDto<MyOptions>(context =>
{
    var panel = new StackPanel();
    panel.Children.Add(new TextBlock { Text = "Custom settings" });
    panel.Children.Add(context.CreateDefaultForm());
    return new FieldEditorLease(panel);
});
return editors.Freeze();
~~~

MyOptions 代表项目自己的 schema DTO 类型；真实业务提交仍由宿主 binding 负责。只要替换一个字段，使用 RegisterMember<MyOptions>(x => x.SomeProperty, factory)，当前只支持直接的生成 DTO 属性，字段 JsonPropertyName 会自动解析。在根工厂中调用 CreateDefaultForm("已由自定义控件负责的字段ID") 可以保留剩余字段，排除参数使用 schema 的字段 ID。

多项 DTO 用各自 DtoBinding 或作者文档 binding 创建 DtoEditorItem，再传给 DtoPanel.ItemsSource；每项 Id 唯一，同一 Type 可出现多次。只展示一个 DTO 时也可使用同一分派逻辑的 DtoEditorHost。IFieldDto 的 CLR 对象仍由宿主管理，Editor 直接绑定作者 JSON；只为查找控件不需要创建完整 DTO。原子多字段 patch 必须提供 DtoEditorItem 的 commit 委托；普通单字段 DTO/设置绑定继续沿用现有提交路径。

普通生成 DTO 的嵌套对象和常用列表/一维数组会自动获得可折叠表单，不需要逐类型注册；内部字段继续使用相同 registry。展开创建子控件，折叠释放；未知类型、复杂 converter、过深或过长的结构使用明确 JSON 回退。专业时间轴、曲线等仍通过成员或整 DTO 覆盖接入，不为所有 JsonElement 注册同一个专属控件。

共享默认样式中的 FieldEditorActions 提供显式默认值/null/缺省/恢复草稿。缺省提交需要宿主保存 presence，普通 CLR DtoBinding 没有这种能力时返回 dto.presence，不能把删除字段等同于 CLR 赋 null；Sample 的设置应用/保存规则保持由原宿主管理。

具体生命周期、冻结、错误及当前支持范围见[Schema 字段表单](../spec/control.md#schema-字段表单)。
