# 引擎使用手册

本页说明 GalNet 已实现的引擎契约：内容如何进入 Runtime、剧情如何推进，以及存档和呈现如何协作。它面向使用引擎制作游戏的人；如果目标是给项目增加自己的原语、页面或平台能力，请阅读[二次开发手册](development.md)。

## 运行全景

```text
Graph + 已编译 .galgroup
  -> GameEngine
  -> IGameView.Dispatch
  -> PrimitiveInstance
  -> presenter / 场景渲染
```

`GameEngine` 是剧情流程和活动 primitive 队列的唯一拥有者。玩家和 UI 只应调用 `AdvanceAsync()` 推进；Choice、条件、分支映射和节点跳转属于 Engine 内置控制流，不能实现为普通 primitive。

`IGameRuntime` 是运行事实的唯一来源，保存当前节点、条目位置、变量和 `SceneState`。呈现端口负责显示，不反过来拥有剧情或可存档状态。

## 内容项目与发布

开发项目的最小布局如下：

```text
Project/
  settings.json
  Graph/
    graph.json
    groups/*.rawgalgroup
  Assets/
    **/*
    **/*.meta
  I18n/
```

- `graph.json` 是编辑图；Group 的编辑源是 `*.rawgalgroup`。
- Raw Group 可以包含 primitive 和 composite。编译后产生的 `*.galgroup` 只能包含 primitive，Runtime 只加载这一种文件。
- 原语参数始终是 JSON；内容和存档中不保存 CLR 类型名。
- 资源引用应使用 `.meta` 的稳定 `id`（通常为 GUID），而不是资源路径。

导出结果是 `.galpak`：它包含 Graph、I18n、settings、生成的 `gallery.json` 和资源 PAK。安装后，特殊内容由内容 provider 读取，只有 `Assets/Paks/**/*.pak` 会成为资源 provider。完整格式见[文件格式](spec/file-formats.md)和[资源](spec/assets.md)。

## 条目、原语和编译

一条剧情动作称为 Entry。它有两种形式：

- **primitive**：运行期直接执行的通用 envelope，字段为 `id`、`typeId`、`arguments`、`condition` 和可选 `batchId`。
- **composite**：仅作者期存在的高级指令。编译时展开成有序 primitive 序列，不能直接进入 Runtime。

例如，一个转场 composite 可展开为显示图层、等待和隐藏图层；Runtime 只会收到展开后的三个 primitive。

每次 primitive 调用都会创建一个 `PrimitiveInstance`。它在创建后固定 `IsBlocking` 和 `BatchId`，并通过 `Dispatch()` 启动，通过一次 `TryComplete()` 结束。`IsSkippable` 可随执行阶段变化，`Skip()` 必须幂等。

`batchId` 是单次 Group 执行内的跳过分组：同 batch 的可跳过实例会一起收到 skip；空值表示独立分组。它不是全局 ID，也不跨两次进入同一 Group 生效。

## 推进、阻塞和存档

一次 `AdvanceAsync()` 会清理已完成实例、在稳定边界更新快照，并继续消费剧情，直到遇到新的 blocking primitive、Choice 或结尾。若调用开始时已有 blocking primitive，Engine 只跳过其所属 batch，不会把同一次输入延伸到之后才分发的动作。

稳定快照只在以下条件同时满足时更新：剧情游标已提交、没有未完成 blocking primitive、没有 pending Choice。因此：

- non-blocking primitive 必须在 `Dispatch()` 返回前写入它的最终可存档逻辑状态；
- blocking primitive 的中间展示状态不会成为新的稳定存档；
- 读档不恢复 Task、CancellationToken、平台控件或活动 `PrimitiveInstance`；宿主必须根据 `SceneState` 重放长期展示。

影响场景的原语应先写 `SceneState`，再调用 presenter。这样存档恢复、Headless 和平台前端能保持同一游戏事实。

## 默认能力与宿主

`GalNet.Primitives.Builtins` 是可选的推荐模块集合，提供对话、NVL、图层、动画、效果、粒子、音频、视频、等待、变量和 Gallery 等 schema 与相应的已实现能力。具体媒体后端能力由所选宿主提供；宿主可以只挂载自定义模块，或在内置模块之外追加能力。

官方 Avalonia 宿主 `GalNet.Avalonia.GameView` 提供 `GameShell`、导航、页面和默认 presenter。它不读取项目目录，也不决定媒体或存档实现；这些由游戏宿主在组合根提供。页面集成见[Avalonia 游戏页面](spec/control.md)。

## 推荐验证路径

1. 在编辑器或 `GalgroupCompiler` 中用目标 profile 编译 Raw Group。
2. 用 Headless 宿主验证 primitive 的状态、条件、阻塞和存档语义。
3. 用平台宿主验证 presenter、资源解码和恢复后的场景重放。
4. 为同步、阻塞/跳过、取消和读档后重放分别写测试；不要只验证“画面曾出现”。

更多准确的逐项契约以[运行时参考](spec/runtime.md)、[条目类型](spec/entry-types.md)和[架构](spec/architecture.md)为准。
