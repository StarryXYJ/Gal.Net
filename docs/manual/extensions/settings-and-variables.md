# 设置、变量与会话扩展

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

例如在已有派生首页 VM 中增加两个参数属性，Graph 声明 chapter 并在入口判断其值：

```csharp
public IReadOnlyDictionary<string, object> ChapterOne { get; } =
    new Dictionary<string, object> { ["save.chapter"] = 1 };
public IReadOnlyDictionary<string, object> ChapterTwo { get; } =
    new Dictionary<string, object> { ["save.chapter"] = 2 };
```

首页 View 共用已有命令：

```xml
<Button Content="第一章" Command="{Binding StartNewGameWithVariablesCommand}"
        CommandParameter="{Binding ChapterOne}" />
<Button Content="第二章" Command="{Binding StartNewGameWithVariablesCommand}"
        CommandParameter="{Binding ChapterTwo}" />
```

首页替换继续使用本文的 DI 与 VM→View 注册方式；章节不要求新增 Graph。内容分包和资源矩阵见[内容打包手册](../editor/content-packaging.md)。

初始值只允许显式 `save.` 键及 bool、Int32 范围整数、有限 Single 范围数值和字符串。共享 GameSession 在替换运行前复制并验证参数，在新 Run 初始化默认值和 Prepare 完成后、执行入口前一次应用；参数不会先写进旧 Run。默认宿主每次新建运行从 Graph 的 save 声明初始化，player 已存值保留，缺省 player 值才补入。读档只恢复存档，不重新应用标题页的章节参数；需要持久记录解锁章节时，另用 player 变量。

外部 Editor 调试的启动配置、运行变量面板、路线定位和可选宿主接入见[对接外部游戏调试](../editor/preview-and-debugging.md)。

只需用两个字符串调用项目逻辑时，可用内置 `custom.invoke`，在运行组合中注册 action，无需为每个 action 新建项目契约；见[通用 action 注册与模式语义](actions-and-contracts.md)。
