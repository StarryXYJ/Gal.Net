# 游戏宿主与 Avalonia 展示职责拆分设计

## 当前问题

`AvaloniaGamePageView` 同时实现 dialogue、choice、layer、animation 和 particle presenter，持有 UI 调度、初始展示握手、动画混合状态与粒子生命周期。任一展示域变化都需要理解约 560 行聚合实现。

`SampleGameSessionService` 同时打开安装内容、预加载资源、创建文件存档服务、管理 Gallery、创建引擎与 presenter、运行/停止流程、恢复持久场景并更新页面状态。生命周期字段相互依赖，使资源失败清理与会话行为难以脱离完整 UI 验证。

`CompositeGameView` 当前只冻结 entry modules、规范化 primitive 参数并完成单次 dispatch，这一边界清晰。本 feature 不把 presenter 生命周期加入该类；宿主继续通过 `BuiltinEntryModules.CreateRecommended` 把独立 presenter 组合为 modules，再交给 `CompositeGameView`。

## 目标边界

### Avalonia 展示

- `IAvaloniaUiDispatcher` / `AvaloniaUiDispatcher`：封装 `CheckAccess`、同步投递和异步投递。
- `AvaloniaDialoguePresenter`：对话可见性、打字机、advance/choice 等待和 initial-presentation 握手。
- `AvaloniaLayerPresenter`：图层 show/replace/hide/move。
- `AvaloniaParticlePresenter`：emitter 创建、burst、停止、drain 延迟移除和粒子动画注册。
- `AvaloniaAnimationPresenter`：单属性动画、plan 采样、replace/additive 竞争与播放完成。
- `AvaloniaGamePageView`：创建并暴露上述 presenter，转发页面级 advance/readiness 信号并按逆序释放；不再实现细分 presenter 接口。

动画仍通过 `GamePageViewModel` 的统一 animation value API 操作 layer/effect/particle 值，避免 presenter 彼此引用。粒子 presenter 负责注册这些值，动画 presenter 只消费状态表面。

### Sample 会话

- `SampleGameResourceScope`：一次已加载游戏安装的 content provider、assets、预加载 texture handles、effect programs、Gallery resolver/data source 及其释放。
- `SampleSaveSession`：save service、player variables、progress、settings、槽位刷新、保存/读取与玩家状态清理。
- `PersistentSceneRestorer`：把 Runtime `SceneState` 通过 builtins replay API 重放到 presenter。
- `SampleGameRunner`：engine/page presentation 的 prepared、start、advance、stop 和完成状态；继续复用 `GameRunCoordinator`。
- `SampleGameSessionService`：串行化公开命令、连接页面状态并协调上述对象。

具体文件系统、资源 decoder、媒体/effect factory 均留在 Sample 外层，不新增 Runtime 依赖。

## 生命周期与数据流

```text
SampleGameSessionService
  |-- SampleGameResourceScope ---- content/assets/gallery
  |-- SampleSaveSession ---------- slots/save/variables/progress/settings
  |-- SampleGameRunner ----------- engine + CompositeGameView
  `-- PersistentSceneRestorer ---- SceneState -> presenters

AvaloniaGamePageView
  |-- AvaloniaDialoguePresenter
  |-- AvaloniaLayerPresenter
  |-- AvaloniaAnimationPresenter
  `-- AvaloniaParticlePresenter
            |
      GamePageViewModel / GamePage / shared dispatcher
```

资源 scope 必须在 engine/page presenters 释放后释放。prepared load 必须先恢复 Runtime snapshot，再重放持久展示，最后才允许 run 启动。停止只取消当前 run；reload/dispose 再释放 engine 和资源。

## 兼容性与取舍

- `AvaloniaGamePageView` 保留构造入口、页面级 `AdvanceRequested`、`InitialPresentationReady`、complete/fail 方法，降低 Sample 与 Editor Preview 迁移风险；细分 presenter 改为属性暴露。
- `CompositeGameView` public API 不改，现有 dispatch 和 dispose 测试继续作为回归保护。
- 不创建跨平台 dispatcher 抽象；adapter 属于 Avalonia 项目，避免平台概念进入 Presentation.Abstractions。
- `AssetManager` 优先补竞态测试。只有测试暴露可独立且不破坏原子性的内部边界时才拆实现。

## 主要风险

- 拆 presenter 时 animation 与 particle 注册/释放顺序改变，可能留下 active playback 或 renderable。
- 拆 Sample scope 时异常路径可能重复释放 handle，或在 engine 仍引用 presenter 时提前释放资源。
- prepared load 的恢复顺序改变会造成首帧缺失或重复播放。
- UI dispatcher 的 fire-and-forget 与 await 语义不可互换，需通过 adapter 测试和现有交互测试固定。

