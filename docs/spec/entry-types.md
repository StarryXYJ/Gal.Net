# 条目类型与 target profile

## 当前格式契约

- Primitive ID 必须是点分隔的稳定字符串，例如 `dialogue.text`、`layer.show` 或开发者定义的 `custom.pulse`。
- `.rawgalgroup` 的 `parameters` 与编译后 `.galgroup` 的 `arguments` 都是 JSON 对象；数字、布尔值、对象和数组不被压平为字符串。
- 某个 entry 是否可编辑、可编译，完全由当前 target profile 中挂载模块导出的 entry schema 决定。未挂载的类型被拒绝，不会回退到全局内置目录。
- 每个 entry 定义的 `DynamicParameterTable` 是只读 schema：每个参数以 `typeof(T)` 指定进程内运行时类型，并包含 required、JSON 默认值和 JSON constraints。CLR 类型名绝不写入内容、metadata、存档或 pak；内容只保存 JSON 值。
- Runtime 只接收通用 `PrimitiveEntry`，并通过 `IGameView.Dispatch` 动态路由。Core/Runtime 不要求任何内置 primitive 实现。

`EntryParameterType` 是当前编辑器的 UI hint。它由参数 constraints 生成，不是执行协议。

## Entry Module

每个 `IEntryModule` 持有两张冻结表：

- `PrimitiveEntries`：primitive 参数契约，不含执行工厂或平台依赖。
- `CompositeEntries`：只服务编辑器和编译器的 authoring 展开。

同一模块内 primitive/composite 不得重名；跨模块仅完整 type ID 不得重复，模块名称和指令前缀可重复。`TargetProfileEntryCatalog` 只依赖 schema；运行时通过 `GameRegistration` 独立注册 `IPrimitiveModule` 执行模块，再构建 `CompositeGameView`。一个执行模块可以共享服务与状态，并贡献多个指令；每个实现必须有 primitive schema。

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

内置 schema 模块的 composite 表提供：

```text
transition.crossFade
transition.slide
transition.blinds
transition.fadeBlack
transition.fadeWhite
transition.fadeColor
```

这些 entry 不是 Runtime primitive，也不绕过 target profile：展开后的每一个 primitive 都必须在所选 profile 中存在。

## 内置 primitive ID

`GalNet.Contracts.Builtins` 提供只依赖 Core 的作者契约；`GalNet.Primitives.Builtins` 提供运行时领域模块。`BuiltinSchemas.CreateAllModules()` 返回 13 个独立模块的只读列表，各组件也可通过 `Create<Domain>Module()` 单独选择；没有合成全内置大模块或内置专用 catalog 工厂。消费者使用 `new TargetProfileEntryCatalog(modules)`。

完整运行入口是 `GameRegistration.AddBuiltins`；也可单独调用 `AddBuiltinSchemas` 注册模块列表，再组合部分内置或自定义执行模块。内置 entry DTO、纯参数校验和 Composite 位于 `GalNet.Contracts.Builtins`，primitive instance 和执行 module 位于 `GalNet.Primitives.Builtins`；`GalNet.Core.Entry` 只包含通用基础设施。内容格式只保存下列稳定 `TypeId`，不保存 CLR 类型名。

`GameRegistration.UseCatalog(catalog)` 使用现有项目 catalog，禁止随后增加 schema。`AddBuiltinImplementations(catalog, ...)` 按该集合选择已声明的内置 primitive，不增加 schema；选择不拆散组件状态或释放所有权。宿主通过 `RequireInstructions(compiledTypes)` 检查实际内容需要的执行实现；可选 `RequireExecutionCoverage()` 检查整个 primitive 集合。工具契约加载不要求执行实现，Editor preview 缺少自定义实现时明确失败。

```text
dialogue.text, dialogue.textVoice, dialogue.show, dialogue.hide
nvl.show, nvl.hide, nvl.clear, nvl.append, nvl.appendVoice
layer.show, layer.showColor, layer.hide, layer.move, layer.replace
animation.animate, animation.play, animation.stop
audio.play, audio.stop, audio.pause, audio.resume, audio.enqueue, audio.next, audio.clear
audio.seffect.apply, audio.seffect.stop
video.play, video.stop
effect.apply, effect.stop
particle.play, particle.stop
flow.wait, variable.set, gallery.unlock
```

`gallery.unlock` 的参数是正整数 `id`，其值必须是当前 `GalleryCatalog` 中存在的稳定 Gallery item ID。执行时写入 `player.gallery_<id>_unlocked = true`；Gallery item 从资源 `.meta.gallery[]` 聚合，发行运行读取其生成快照。旧的 category/sequence 参数不受支持。

`nvl.show` 和 `nvl.hide` 只改变 NVL 覆盖层的可见性；`nvl.clear` 移除已累积的历史行；`nvl.append` 只接收长文本 `content`，以与 `dialogue.text` 相同的富文本和打字机语义追加一段，并在文字完成后等待一次玩家推进。它不会自动显示 NVL，剧情应显式调用 `nvl.show`。

这些名称和参数属于可选内置契约。开发者可以只注册自定义模块，或在自己的 target profile 中定义完全不同的前缀、参数与语义；编辑器不会凭名称假定 `dialogue`、`nvl`、`layer` 或任何其他模块存在。

当前已实现 Runtime 行为的内置能力见 [runtime.md](runtime.md)。

## 视频图层 entry

`video.play`：

| 参数 | 默认值 | 含义 |
| --- | --- | --- |
| `asset` | 必填 | video 资源 ID |
| `handleId` | `video` | 场景图层 handle；同 handle 新播放替换旧播放 |
| `blocking` | `true` | 是否等待视频结束；沿用推荐 entry 的 true/false 选项 |
| `skipPoints` | `[]` | 以秒为单位的 JSON 数字数组，运行时校验为非负、有限、严格递增 |
| `transform` | `{}` | 与 Layer 相同的位置、旋转和缩放 |
| `z` | `0` | 场景排序 |
| `opacity` | `1` | 图层透明度 |
| `displayMode` | `Uniform` | `Native` / `Tile` / `Fill` / `Uniform` / `UniformToFill` |

阻塞时，推进取严格大于当前实际播放位置与上次请求跳转位置的较大值的下一个时间戳；无下一个或目标达到/超过已知时长即结束。空列表点击直接结束。加载未就绪时不执行 seek；非阻塞播放不消费推进输入。自然播完也解除阻塞。

`video.stop` 接收 `handleId`（默认 `video`），停止对应视频并移除其图层。不同 handle 可独立播放。`layer.hide`、`layer.show`、`layer.replace` 对视频源同样执行释放，旧播放的延迟完成不会清理替换后的图层。停止与自然结束不保留末帧；视频自带声音由视频后端直接输出，尚未接入音频轨道。

视频图层可使用普通 Layer 的变换/透明度动画与纹理效果。阻塞视频存档采用此前稳定快照；非阻塞视频保存源定义并在读档时从头播放，无 presenter 的 Headless 安全完成视频 entry。
