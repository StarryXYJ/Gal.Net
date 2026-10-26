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
- 音频平台实现通过 `GalNet.Core.Audio.IAudioPlaybackHost` 组合；同一逻辑轨道的队列和状态仍由 `AudioRuntime` 管理。
- 新的自定义 primitive 需要同时提供 schema、执行注册、参数/恢复验证和覆盖其生命周期的测试。

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

## 验证清单

- 只含自定义 schema/执行的最小内容可编译和运行，缺失执行会明确失败。
- 正常推进、Choice、阻塞实例自然完成、停止和重新打开不会并发推进或泄漏旧回调。
- 存档/读档能重放 layer、effect、particle、循环动画及音频的稳定状态。
- 资源、临时媒体文件、平台对象和 DI scope 均按失败路径释放。

详细模块契约请分别查看[运行时](../spec/runtime.md)、[条目类型](../spec/entry-types.md)、[资源](../spec/assets.md)和[架构](../spec/architecture.md)。
