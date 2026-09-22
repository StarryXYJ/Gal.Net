# 条目类型与 target profile

## 当前格式契约

- 原语 ID 必须是点分隔的稳定字符串，例如 `dialogue.text`、`layer.show` 或开发者定义的 `custom.pulse`。
- `.rawgalgroup` 的 `parameters` 与编译后 `.galgroup` 的 `arguments` 都是 JSON 对象；数字、布尔值、对象和数组不被压平为字符串。
- 某个原语是否可编辑、可编译，完全由当前 target profile 中挂载模块导出的 `PrimitiveDescriptor` 决定。未挂载的类型被拒绝，不会回退到全局内置目录。
- Descriptor 的 `DynamicParameterTable` 是只读 schema：每个参数以 `typeof(T)` 指定进程内运行时类型，并包含 required、JSON 默认值和 JSON constraints。`Type`/CLR 类型名绝不写入内容、metadata、存档或 pak；内容只保存 JSON 值。
- Runtime 只接收通用 `PrimitiveEntry` 信封，并通过 `IGameView.Dispatch` 动态路由。Core/Runtime 不内置或要求任何原语实现。

`EntryParameterType` 仍是当前编辑器的 UI hint。它由 descriptor 的 constraints 生成，不是执行协议；自动生成编辑控件不属于当前阶段。

## 可选推荐 authoring profile

`GalNet.Primitives.Builtins` 目前提供可选的推荐 authoring descriptors；编辑器的默认组合根显式选择该 profile。它不是 Runtime 的内建能力，也不代表宿主已经实现对应命令。实际执行时，宿主必须挂载拥有相同 descriptor 的模块；否则该调用按动态分发规则跳过并产生诊断。

每个 module 类通过 `IEntryModule` 自己持有独立且只读的 `PrimitiveEntries` 表与 `NonPrimitiveEntries` 表。前者描述 Runtime primitive schema，后者只服务编辑器/编译器的 authoring 展开；两表没有必须一一对应的关系。`PrimitiveModuleBase` 同时是 Runtime 和 authoring 基类，因此组合根可把同一已挂载模块实例传给 `CompositeGameView` 与 `TargetProfileEntryCatalog`；纯 authoring module 也可以只参与后者。这只是方便组织和编译的推荐结构，开发者仍可定义任意动态语义。

推荐 primitive ID 为：

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

这些名称和参数只是推荐语义。开发者可以只注册自定义模块，或在自己的 target profile 中定义完全不同的前缀、参数与语义；编辑器不会凭名称假定 `dialogue`、`layer` 或任何其他模块存在。

## NonPrimitive 条目

NonPrimitive 仅存在于 `.rawgalgroup`，并在编译时展开为通用 `PrimitiveEntry` 序列；`.galgroup` 只包含 primitive envelope。推荐 Builtins 在 animation catalog 的非原语表中提供 `transition.crossFade`、`transition.slide`、`transition.blinds`、`transition.fadeBlack`、`transition.fadeWhite` 和 `transition.fadeColor`；它们不是 Runtime Handler，也不绕过 target profile：展开后的每一个 primitive 都必须在所选 profile 中存在。

详情见[文件格式](file-formats.md)和[原语模块化运行时设计](../design/primitive-module-runtime-design.md)。
