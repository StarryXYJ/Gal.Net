# 条目类型与 target profile

## 当前格式契约

- Primitive ID 必须是点分隔的稳定字符串，例如 `dialogue.text`、`layer.show` 或开发者定义的 `custom.pulse`。
- `.rawgalgroup` 的 `parameters` 与编译后 `.galgroup` 的 `arguments` 都是 JSON 对象；数字、布尔值、对象和数组不被压平为字符串。
- 某个 entry 是否可编辑、可编译，完全由当前 target profile 中挂载模块导出的 entry schema 决定。未挂载的类型被拒绝，不会回退到全局内置目录。
- 每个 entry 定义的 `DynamicParameterTable` 是只读 schema：每个参数以 `typeof(T)` 指定进程内运行时类型，并包含 required、JSON 默认值和 JSON constraints。CLR 类型名绝不写入内容、metadata、存档或 pak；内容只保存 JSON 值。
- Runtime 只接收通用 `PrimitiveEntry`，并通过 `IGameView.Dispatch` 动态路由。Core/Runtime 不要求任何内置 primitive 实现。

`EntryParameterType` 是当前编辑器的 UI hint，由独立的 `EditorHints` 适配，不是执行协议。内置描述不再把 editorType 放入值 constraints。

`FieldDescriptor` 组合稳定 ID、值描述、required/默认值、标签 key 和可选编辑提示；`DynamicParameterDescriptor.Field` 是 Entry 的唯一字段元数据来源，`DynamicParameterTable.Fields` 按声明输入顺序冻结。`EditorHints.EditorId` 为开放字符串，内置值见 `ValueEditorIds`，宿主可以声明自己的 ID；Step/Format/UnitKey 仅描述展示，Step 不限制合法值。合法选项留在值约束，展示标签及动态选项留在宿主。CLI schema 查询单独输出 editor 提示，值 schema 不混入 UI 规则。共享 Avalonia SchemaForm/默认工厂及 Editor/设置最小宿主接入已实现，见 [表单契约](control.md#schema-字段表单)。

参数值的公共基础位于 `GalNet.Core.Parameters`：`ValueDescriptor` 提供类型、null、约束及冻结的 JSON 配置，`ValueCodec` 负责序列化/反序列化/结构描述，`ValueValidation` 返回包含 code/path 的校验问题。Entry 的 `DynamicParameterDescriptor` 组合该描述，名称、必填和默认值仍归 Entry；CLI 可独立组合相同值描述。对象/数组通过 JSON/CLR 契约处理，不增加新的值容器。支持范围、字符长度、数组长度、enum/options；未知业务约束由契约自己的 validator 执行，结构描述不能替代完整代码校验。省略与显式 null 分开，根值规则不改变递归子类型；嵌入 schema 时需重定位其局部引用。

项目集合来自独立契约程序集的 `GalNet.Project.Contracts.ProjectSchemas.Modules`；CLI 每次调用加载，Editor 在打开项目时加载并由项目 scope 持有，关闭/切换释放。相同名称 DLL 可声明不同集合，模块名可重复但完整 typeId 排他。工具加载共享 Core/内置契约类型身份，拒绝缺失依赖和不兼容版本；不存在默认内置 fallback。命令见 [CLI README](../../src/Editor/GalNet.Editor.Headless/README.md)。

单平台执行可在外层模块直接借用具体页面；Sample 的 `sample.screenshot(fileName?)` 契约无 Avalonia 依赖，Avalonia 模块直接捕获 GamePage 场景，自动写入宿主 Pictures 目录。它不属于全内置模块，Headless 只可编译，缺执行实现时拒绝运行。Normal/Quick 生成 PNG，CatchUp 不写文件；参数、生命周期与示例见 [Sample.Contracts](../../src/Samples/GalNet.Sample.Contracts/README.md)。

同步执行可用 PrimitiveRegistry.Immediate / Immediate<T>，自动传递批次并同步完成；实例非阻塞且不可跳过。Normal/Quick 执行 dispatch，CatchUp 默认重复 dispatch，可显式覆盖。异常按原 PrimitiveInstance 协议传播，不推断取消、平台 dispatch 或异步生命周期。带参数入口沿用 Register<T> 的独立 DTO 和契约身份检查。内置 audio.pause/resume/next/clear 共用 AudioTrackOptions，flow.wait 使用 WaitOptions；旧手写参数表已删除，JSON 字段、默认值和 null 策略保持原有行为。Contracts.Builtins 通过构建期 Analyzer 引用 SchemaGenerator，运行时仍只依赖 Core 和框架程序集。

dialogue.text、nvl.append、layer.hide/replace、animation.stop、audio.seffect.stop、video.stop、effect.stop、particle.stop、variable.set 和 gallery.unlock 已采用 DTO 参数与强类型执行读取；effect.stop/particle.stop 共用 InstanceIdOptions。对白/NVL 保留 reading 标记与独立实例；Gallery 的正整数和 catalog 检查仍在原执行边界。Editor 含专属字符串编辑提示的 DTO 保留补全、多行文本、资源选择和表达式控件投影，普通字段仍使用共享 binding，不另维护参数表。

其余带参数的内置 Primitive 也采用 DTO：layer.show/showColor/move、animation.animate/play、audio.play/enqueue/seffect.apply、video.play、effect.apply、particle.play/burst。audio.play/enqueue 共用 AudioPlayOptions，enqueue 仍显式选择 Enqueue 策略。嵌套 JSON 字段保留 JsonElement 与原解析规则；固定标量、布尔和枚举使用强类型属性。动画 from 的省略由规范化参数 presence 判断，batchId 仍由编译信封提取；快照/重放与复杂实例生命周期未改变。

DynamicParameterTable 的 ValidatePartialArguments 用于尚未补默认值的局部作者参数；可选回调省略时沿用完整 validator。ForDto<T> 可显式提供 validatePartialArguments；字段自身校验照常执行，Normalize 的完整业务校验仍使用 ValidateArguments。内置视频/粒子/动画计划将完整业务规则保留在编译/执行边界，逐字段创作不要求其他尚未填写的字段。

所有 8 条内置 Composite 使用 ForDto<T> 接收输入，普通 schema 类型/选项/未知字段检查由同一 Normalize 执行；业务展开、正数/颜色/变换校验保持显式。语音子指令及计划 Primitive 外层参数通过 ValueCodec.SerializeFields 使用对应子 DTO 的字段 codec；undefined JSON 字段省略，null 默认保留，可显式选择省略 null。黑白与自定义颜色过渡共用源基类的 12 个字段；生成器合并继承字段并拒绝隐藏/重写或 Order/ID 冲突。

BuiltinEntryIds 集中声明 42 个内置稳定 wire ID。内置 Primitive 的作者类壳及取 ID 的泛型工厂已移除；catalog.Create 继续返回 Core 的通用作者表示，运行时依旧使用通用 PrimitiveEntry。项目仍显式选择 13 个独立 schema modules，不通过 CLR 类扫描启用指令。

Field.DefaultJson 声明经值 codec 校验的 JSON 默认，不能与属性初始化值同时声明；Choices 提供固定合法选项。StringBooleanValueConverter / StringEnumValueConverter 通过 IStringValueEncoding 保留字符串 wire 类型、选项及编辑文本，同时提供强类型成员；普通 converter 行为不改变。audio.stop 已用 AudioStopOptions，clearQueue 保持字符串 true/false 与显式 null 的旧行为。DefaultCompositeEntryBase.ForDto<T> 提供类型化编译展开，共用 ValueCodec.DeserializeFields，源 JSON 保留 null/presence，输出仍通过目标 primitive 契约规范化。

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

## DTO 字段声明

GenerateFieldSchema + Field 属性生成中立 FieldDescriptor 列表及 IFieldDto 成员访问，不生成平台控件。DefaultPrimitiveEntryBase.ForDto<T> 使用 T.Schema 建表，并记录 FieldDataType，不提供 Editor DTO 工厂。运行时仍调度通用 PrimitiveEntry；执行模块可使用 PrimitiveRegistry.Register<T>，注册时检查 DTO 类型，执行时用规范化参数和逐字段 ValueCodec 填充独立 DTO。契约运行时不依赖 Roslyn，生成器仅为构建工具。默认值通过完整 ValueCodec 投影；旧字符串文档不能无损表示的默认值不填入 Defaults，仍保留在字段描述中，避免 JSON null 字符串和字符串编码枚举被改写。具体接入方式见[定制 Avalonia Sample 手册](../manual/customize-avalonia-sample.md)。
