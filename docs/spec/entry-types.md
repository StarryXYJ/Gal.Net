# 条目类型与 target profile

## 当前格式契约

- Primitive ID 必须是点分隔的稳定字符串，例如 `dialogue.text`、`layer.show` 或开发者定义的 `custom.pulse`。
- `.rawgalgroup` 的 `parameters` 与编译后 `.galgroup` 的 `arguments` 都是 JSON 对象；数字、布尔值、对象和数组不被压平为字符串。
- 某个 entry 是否可编辑、可编译，完全由当前 target profile 中挂载模块导出的 entry schema 决定。未挂载的类型被拒绝，不会回退到全局内置目录。
- 每个 entry 定义的 `DynamicParameterTable` 是只读 schema：每个参数以 `typeof(T)` 指定进程内运行时类型，并包含 required、JSON 默认值和 JSON constraints。CLR 类型名绝不写入内容、metadata、存档或 pak；内容只保存 JSON 值。
- Runtime 只接收通用 `PrimitiveEntry`，并通过 `IGameView.Dispatch` 动态路由。Core/Runtime 不内置或要求任何推荐 primitive 实现。

`EntryParameterType` 是当前编辑器的 UI hint。它由参数 constraints 生成，不是执行协议。

## Entry Module

每个 `IEntryModule` 持有两张冻结表：

- `PrimitiveEntries`：Runtime 可执行 primitive schema 与 instance 工厂。
- `CompositeEntries`：只服务编辑器和编译器的 authoring 展开。

同一模块内 primitive/composite 不得重名；多个挂载模块之间 primitive 名称也不得重复。组合根可以把同一组模块用于 `TargetProfileEntryCatalog` 和 `CompositeGameView`，使编辑器 schema 与 Runtime 工厂来自同一事实来源。

## Primitive Entry

编译后的 primitive envelope 包含：

- `typeId`
- `arguments`
- `batchId`
- `condition`
- 稳定 `id`

`batchId` 是可选局部分组字段。编译器会把 authoring 参数中的 `batchId` 提升到 envelope 顶层；GameView 创建 instance 时再把它原样传入 `PrimitiveCreateContext.BatchId` 和 `PrimitiveInstance.BatchId`。

## Composite Entry

Composite 仅存在于 `.rawgalgroup`，并在编译时展开为通用 `PrimitiveEntry` 序列；`.galgroup` 只包含 primitive envelope。Composite 自己决定展开结果的 batch 划分，框架不规定“一个 Composite 等于一个 Batch”。

推荐 Builtins 在 animation module 的 composite 表中提供：

```text
transition.crossFade
transition.slide
transition.blinds
transition.fadeBlack
transition.fadeWhite
transition.fadeColor
```

这些 entry 不是 Runtime primitive，也不绕过 target profile：展开后的每一个 primitive 都必须在所选 profile 中存在。

## 推荐 primitive ID

`GalNet.Primitives.Builtins` 提供可选推荐 authoring profile 与运行时模块。当前推荐 ID 为：

```text
dialogue.text, dialogue.show, dialogue.hide
layer.show, layer.showColor, layer.hide, layer.move, layer.replace
animation.animate, animation.play, animation.stop
audio.play, audio.stop, audio.pause, audio.resume, audio.enqueue
video.play, video.stop
effect.apply, effect.stop
particle.play, particle.stop
flow.wait, variable.set, gallery.unlock
```

`gallery.unlock` 的参数是正整数 `id`，其值必须是当前 `GalleryCatalog` 中存在的稳定 Gallery item ID。执行时写入 `player.gallery_<id>_unlocked = true`；Gallery item 从资源 `.meta.gallery[]` 聚合，发行运行读取其生成快照。旧的 category/sequence 参数不受支持。

这些名称和参数只是推荐语义。开发者可以只注册自定义模块，或在自己的 target profile 中定义完全不同的前缀、参数与语义；编辑器不会凭名称假定 `dialogue`、`layer` 或任何其他模块存在。

当前已实现 Runtime 行为的推荐能力见 [runtime.md](runtime.md)。
