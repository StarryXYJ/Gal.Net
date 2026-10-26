# 接入新的表现层

本手册适用于 WPF、WinUI 或其他新的 UI 框架。目标是复用 Core、Runtime、GameHost、Assets 和 Storage，而不是移植 Avalonia 的 View 或渲染对象。

## 最小接入路径

1. 新建框架专属项目，引用 `GalNet.GameHost`、`GalNet.Presentation.Abstractions`、`GalNet.Runtime` 和所需的内容/存储项目；禁止让这些内层项目反向引用新框架。
2. 以项目契约构建 `IEntryCatalog`，再通过 `GameRegistration` 注册对应的 `IPrimitiveModule`。只注册内容实际允许的 schema 和具有执行实现的 type ID。
3. 实现 `GalNet.Core.Execution.IPrimitiveDispatcher`，或组合框架专属的 dialogue、choice、layer、animation、effect、particle、video presenter 后构建它。Choice、快进同步和玩家播放控制协议同属 Core.Execution；Presenter 将 Runtime 的中立请求映射为框架对象，不得把 UI 控件、Dispatcher 或媒体对象写入 `GameState`。仅用自定义分发器的最小宿主可只引用 Core/Runtime，不要求呈现注册组合。
4. 创建 `GameSession`/`GameRun` 的应用组合根。它负责开始、存档、替换运行和退出；停止时先释放 Engine，再异步释放 view、presenter、媒体后端及资源。
5. 通过 `IAssetManager` 获取图片、音频和视频。若后端必须使用文件，使用 Asset 的 materializer/lease 产生临时实体文件，lease 存续期间才可使用。

## 必须保持的契约

- `GameEngine` 是唯一的剧情推进器；UI 不自行推进、跳过 batch 或推断 Engine 终态。
- primitive 先在 `GameState.Update` 提交可存档事实，再通知 presenter。读档后的展示由注册的恢复处理器和 `StateRestoreSession` 重建。
- `GameSnapshot` 当前版本为 4；不要序列化 UI、Task、取消源、播放器 identity 或媒体时间轴。
- 音频平台实现通过 `GalNet.Contracts.Builtins.Audio.IAudioPlaybackHost` 组合；同一逻辑轨道的队列和状态仍由 `AudioRuntime` 管理。
- 新的自定义 primitive 需要同时提供 schema、执行注册、参数/恢复验证和覆盖其生命周期的测试。

## 从项目设置映射模块配置

项目可以采用自己的普通 DTO，不必继承框架设置基类。组合根只传入模块需要的值：

```csharp
var playback = new PlaybackOptions
{
    AutoAdvanceIntervalMilliseconds = preferences.AutoDelay,
    QuickReadOnly = preferences.ReadOnlySkip
};
var configuration = new AudioConfiguration(projectTracks, projectGroups);
var audio = new BuiltinAudioModule(audioBackend, configuration: configuration);
audio.ApplyMix(new AudioMixOptions
{
    MasterGain = preferences.MasterVolume,
    Groups = new Dictionary<string, AudioMixLevel> { ["ambience"] = new(preferences.AmbienceVolume) },
    Tracks = new Dictionary<string, AudioMixLevel> { ["rain"] = new(preferences.RainVolume) }
});
// Register the configured audio module with the other implementations.
var runtime = new GameRuntime(textResolver, graph.RootNodeId, variableService: variables);
await using var engine = new GameEngine(graph, runtime, dispatcher, playbackOptions: playback);
await engine.UpdatePlaybackOptionsAsync(playback with { AutoAdvanceIntervalMilliseconds = 1500 });
```

PlaybackOptions 位于 Core.Execution，AudioMixOptions/AudioMixLevel 位于 Contracts.Builtins.Audio，BuiltinAudioModule 位于 Primitives.Builtins。默认 Avalonia.GameHost.Settings.GameSettings 提供示例映射方法，可直接使用或完全替换。音频模块不会读取 GameContent.Configuration，也不会自动创建轨道；默认宿主负责从配置文档投影项目声明的轨道。轨道声明和混音字典在入口隔离，后续修改项目对象不等于更新运行配置。文本速度传给文本呈现，窗口偏好交给显示服务，保存槽数交给保存服务，不把整个偏好对象塞进运行上下文。

## 声明音频分组与轨道

`AudioConfiguration` 在构造时验证并隔离轨道/分组声明；ID 由项目定义，一个轨道可以不分组，或用 `GroupId` 引用一个已声明分组。空配置合法。`BuiltinAudioDefaults.Create()` 是可选的三分组示例，Sample 和新项目模板显式选择它；Core 与音频模块不自动注入默认轨道。

```csharp
var configuration = new AudioConfiguration(
    [new() { Id = "rain", Name = "Rain", GroupId = "ambience", PreserveDuringCatchUp = true },
     new() { Id = "wind", Name = "Wind", GroupId = "ambience" },
     new() { Id = "alert", Name = "Alert" }],
    [new() { Id = "ambience", Name = "Environment" }]);
```

`AudioMixOptions` 分别接收 master、Groups 和 Tracks 的 gain/mute，缺省为 1/false。最终增益为 master × group × track × 轨道 DefaultGain × 播放内容 gain，最后限制到后端的 0–1；任一层静音则为零。ApplyMix 在入口复制字典，更新当前及后续 voice。玩家混音不写入剧情快照，恢复使用当前玩家值。

`PreserveDuringCatchUp` 显式控制快进回放结束时是否投影最终播放内容，与 ID/分组名称无关。普通音频指令和语音复合指令均须显式提供 `trackId`。

默认 Avalonia 设置服务从声明生成分组控件，以 `ShowTrackAudioControls` 选择是否同时显示单轨控件；隐藏单轨控件时仍恢复对应偏好。更高层可以完全采用自己的 DTO、UI 和持久化，只需映射上述配置。默认存储键按经过 URI 转义的 ID 区分 group/track，无固定三分类字段。

## 接入图片差分

内置 layers 模块包含 `variants.show/select`。资源 scope 建立后，先注册 decoder 并异步准备目录，再构造执行 view：

```csharp
using GalNet.Assets.Variants;

assets.RegisterImageVariantsDecoder();
var imageVariants = await assets.LoadImageVariantsCatalogAsync(cancellationToken);
using var registration = new GameRegistration().UseCatalog(projectCatalog);
var view = registration.AddBuiltinImplementations(projectCatalog,
    layerPresenter: layers, imageVariants: imageVariants).BuildView();
```

图集定义无需持有资源租约；sprite 图片仍按现有 presenter 生命周期管理。普通 `ILayerPresenter.ShowLayer/ReplaceLayer` 已足够，渲染器不解析文件名或维度。会话播放中目录不变，读档由同一目录进行同步内容预检；切换内容或项目时重新准备，不能把旧目录挂到新项目。默认 Avalonia 配置回调可从 `presentation.ImageVariants` 获取已准备目录，并传入 AddBuiltinImplementations。

## 项目配置声明

`ProjectSchemas.Configurations` 可返回 `ProjectConfigurationSchema`；未声明时为空。通过 `ConfigurationSection<MyOptions>` 为普通 DTO 显式提供 FieldDescriptor。配置统一使用标准 JSON 序列化和属性 converter，不另设生成 DTO 编解码入口。版本默认为 1，领域校验可通过声明时的委托提供。

通过 `content.Configuration.Read(section)` 取得独立 DTO，修改后用 `content.Configuration.Set(section, options)` 发布；读取缺省配置不会写文件。运行模块继续接收由 DTO 投影的小配置。Storage 加载通过 `GameContentLoadOptions.Configurations` 显式选择 schema，并通过 `SelectDefaultCulture` 提供文化代码。默认 Sample 的组合见 SampleCustomization.CreateHostOptions，默认字段定义见 DefaultProjectConfiguration；它们是可选示例，不由 Core/Runtime 自动选择。项目设置窗口按声明生成通用字段表单，复合值使用共享 JSON 编辑器；应用或保存全部时验证整份候选，一次提交可撤销的配置变更。未知数据保留，默认模型的语言声明继续在 I18n 面板维护。调试沿外部宿主路径进行。

CLI 可用 `execute <project> <patch.json>` 修改已声明的 section。例如：

```json
{"commandId":"project.configuration.patch","sectionId":"developer.options","values":{"caption":null},"remove":["count"]}
```

`values` 写入原始 JSON，`null` 是显式空值；`remove` 删除字段，使读取时恢复 schema 或 DTO 默认值。同一字段同时出现时以 `values` 为准。整份候选通过字段和领域校验后才发布，未知 section/字段不能通过补丁写入，已有未知数据仍保留。CLI 构建、两种导出及 Editor 导出/外部调试构建均传入项目 schema；直接调用共享服务时，通过 `configurations` 参数显式选择声明，无效配置在替换既有产物前失败。

默认 Avalonia 宿主通过 `AvaloniaGameHostOptions.SelectProjectOptions` 接收项目投影，返回 `AvaloniaProjectOptions` 的存档容量、默认语言和 `AudioConfiguration`。宿主启动时只投影一次，随后运行、重置偏好和重新开局复用这些简单值；它不读取 `DefaultProjectOptions`。Sample 在组合根显式完成默认示例模型的映射，自有 DTO 可替换该回调。

## 验证清单

- 只含自定义 schema/执行的最小内容可编译和运行，缺失执行会明确失败。
- 正常推进、Choice、阻塞实例自然完成、停止和重新打开不会并发推进或泄漏旧回调。
- 存档/读档能重放 layer、effect、particle、循环动画及音频的稳定状态。
- 资源、临时媒体文件、平台对象和 DI scope 均按失败路径释放。

详细模块契约请分别查看[运行时](../spec/runtime.md)、[条目类型](../spec/entry-types.md)、[资源](../spec/assets.md)和[架构](../spec/architecture.md)。

默认资源与 Gallery 工厂在 Contracts.Builtins.Assets/Gallery，差分图片契约在 Contracts.Builtins.Variants；不选择默认工厂时可直接组装自有目录，并按需要声明 DefaultType。创建 provider/PAK 时始终传入同一资源目录。宿主内容类型在 GameHost.Content，通用 Runtime 不再拥有 GameContent 或 Gallery 玩家包装。
