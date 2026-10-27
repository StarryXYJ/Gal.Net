# 条目类型与 target profile

## 当前格式契约

- Primitive ID 必须是点分隔的稳定字符串，例如 `dialogue.text`、`layer.show` 或开发者定义的 `custom.pulse`。
- `.rawgalgroup` 的 `parameters` 与编译后 `.galgroup` 的 `arguments` 都是 JSON 对象；数字、布尔值、对象和数组不被压平为字符串。
- 某个 entry 是否可编辑、可编译，完全由当前 target profile 中挂载模块导出的 entry schema 决定。未挂载的类型被拒绝，不会回退到全局内置目录。
- 每个 entry 定义的 `DynamicParameterTable` 是只读 schema：每个参数以 `typeof(T)` 指定进程内运行时类型，并包含 required、JSON 默认值和 JSON constraints。CLR 类型名绝不写入内容、metadata、存档或 pak；内容只保存 JSON 值。
- Runtime 只接收通用 `PrimitiveEntry`，并通过 `IPrimitiveDispatcher.Dispatch` 动态路由。Core/Runtime 不要求任何内置 primitive 实现。

Core Entry 的参数权威是 `Arguments` JSON，不再保留字符串 Values。`AuthoringPrimitiveEntry` 与 `CompositeEntry` 是作者表示，均不继承编译后的 sealed `PrimitiveEntry`；运行与作者类型在类型边界区分，不使用 IsGeneric 标志。`IEntryCatalog.Create` 接收 JSON arguments、应用 JSON 默认并保留草稿未知字段；完整编译/运行校验仍拒绝未知参数。Editor/CLI 的字符串输入由 EditorEntryData 适配，显式 null 与缺省不混淆。

`EntryDefinition.DynamicParameters` 是唯一参数描述，不再保存平铺的 Parameters/Defaults/Options 字典。`EntryParameterType` 与按需投影的 `EntrySchema` 位于 `GalNet.Editor.Abstraction.Schemas`，仅服务编辑器；Core 通过开放的 `EditorHints` 提供展示提示，不依赖具体控件分类。内置描述不把 editorType 放入值 constraints。

视觉 Effect 与音频 SEffect 的参数类型、必填、有限数值、范围和选项校验复用 `FieldDescriptor` / `ValueValidation`。目录复制调用方的参数、选项和能力集合，使查询元数据与校验规则保持一致；效果 stage、目标和 scope 的规则仍归各自领域，校验返回诊断，由调用方决定处理方式。

`FieldDescriptor` 组合稳定 ID、值描述、required/默认值、标签 key 和可选编辑提示；`DynamicParameterDescriptor.Field` 是 Entry 的唯一字段元数据来源，`DynamicParameterTable.Fields` 按声明输入顺序冻结。`EditorHints.EditorId` 为开放字符串，内置值见 `ValueEditorIds`，宿主可以声明自己的 ID；Step/Format/UnitKey 仅描述展示，Step 不限制合法值。合法选项留在值约束，展示标签及动态选项留在宿主。CLI schema 查询单独输出 editor 提示，值 schema 不混入 UI 规则。共享 Avalonia SchemaForm/默认工厂及 Editor/设置最小宿主接入已实现，见 [表单契约](control.md#schema-字段表单)。

参数值的公共基础位于 `GalNet.Core.Parameters`：`ValueDescriptor` 提供类型、null、约束及冻结的 JSON 配置，`ValueCodec` 负责序列化/反序列化/结构描述，`ValueValidation` 返回包含 code/path 的校验问题。Entry 的 `DynamicParameterDescriptor` 组合该描述，名称、必填和默认值仍归 Entry；CLI 可独立组合相同值描述。对象/数组通过 JSON/CLR 契约处理，不增加新的值容器。支持范围、字符长度、数组长度、JSON enum（Choices 文本声明在构造时转为类型化 enum）；未知业务约束由契约自己的 validator 执行，结构描述不能替代完整代码校验。省略与显式 null 分开，根值规则不改变递归子类型；嵌入 schema 时需重定位其局部引用。

项目集合来自独立契约程序集的 `GalNet.Project.Contracts.ProjectSchemas.Modules`；CLI 每次调用加载，Editor 在打开项目时加载并由项目 scope 持有，关闭/切换释放。相同名称 DLL 可声明不同集合，模块名可重复但完整 typeId 排他。工具加载共享 Core/内置契约类型身份，拒绝缺失依赖和不兼容版本；不存在默认内置 fallback。命令见 [CLI README](../../src/Editor/GalNet.Editor.Headless/README.md)。

### 嵌套属性访问

字段生成器支持程序集级 `FieldSchemaJsonContext`，将显式 JSON context resolver 注入字段 codec，并生成 `FieldDescriptor.WithMember` 的 CLR/JSON 成员身份及 `ValueDescriptor.WithSchemaProvider` 的延迟子 schema。集合中的生成 DTO 同样保留子 schema；反射关闭的玩家通过这些元数据解析嵌套结构。手写 schema 可显式提供相同信息；反射只保留为启用了反射 JSON 的桌面兼容路径。扩展方式见 [NativeAOT 手册](../manual/native-aot.md)。

`ValueDescriptor.WithStructure` 为值关联不可变 ValueStructure，旧构造函数签名保留。原生 IFieldDto 使用其静态 Schema；固定 JsonElement 字段可用 Field.StructureType 关联结构类型，生成器输出延迟类型引用，不在初始化时递归展开 schema。StructureAllowsJsonString、StructureIgnoresCase、StructureUsesFieldIds 仅为显式兼容表示提供规则，不改变通用 JSON 的默认命名或运行 codec；内置 Transform 保留原成员拼写与字符串表示，Flipbook 新成员沿用字段 ID。

ValueStructure 提供 ForType、Object、Sequence、Map 和 Dynamic 声明，集合元素/Map 值可拥有独立字段约束与文本/角色语义。ValueStructureResolver 使用实际 JSON options 解析 wire 成员，缓存属于调用方作用域；无法解释的 converter/结构返回可定位诊断。IValueStructureProvider 只消费调用方提供的冻结上下文；动态领域查询及依赖有效性仍由调用方负责，接口本身不执行项目 I/O。

ValueDocument 持有不可变 JSON 快照，提供 Root、FindProperty、GetIterator、Validate 和纯 Patch。PropertyPath 区分 Member、Index、MapKey、JsonString；对外实际 JSON Pointer 与逻辑内部路径分别编码。迭代由显式栈、节点/深度预算和取消控制；Validate 的 Partial 默认不要求缺失兄弟字段，完整模式显式检查已出现对象中的必填字段。展开、扫描和查找不写入默认值。Patch 仅产生候选值，不替代作者身份检查、事务或领域校验；它保留扩展与兄弟值，拒绝歧义和不存在的索引，显式 null 容器需要先被替换。旧索引句柄是否因重排失效由宿主结构代次核对，纯快照操作不会猜测元素身份。

单平台执行可在外层模块直接借用具体页面；Sample 的 `sample.screenshot(fileName?)` 契约无 Avalonia 依赖，Avalonia 模块直接捕获 GamePage 场景，自动写入宿主 Pictures 目录。它不属于全内置模块，Headless 只可编译，缺执行实现时拒绝运行。Normal/Quick 生成 PNG，CatchUp 不写文件；参数、生命周期与示例见 [Sample.Contracts](../../src/Samples/GalNet.Sample.Contracts/README.md)。

同步执行可用 PrimitiveRegistry.Immediate / Immediate<T>，自动传递批次并同步完成；实例非阻塞且不可跳过。Normal/Quick 执行 dispatch，CatchUp 默认重复 dispatch，可显式覆盖。异常按原 PrimitiveInstance 协议传播，不推断取消、平台 dispatch 或异步生命周期。带参数入口沿用 Register<T> 的独立 DTO 和契约身份检查。内置 audio.pause/resume/next/clear 共用 AudioTrackOptions，flow.wait 使用 WaitOptions；旧手写参数表已删除。音频及语音复合指令的 trackId 为无默认值、不可为 null 的必填字符串，不再隐含选择 bgm/vocal；其他字段沿用其各自 schema。Contracts.Builtins 通过构建期 Analyzer 引用 SchemaGenerator，运行时仍只依赖 Core 和框架程序集。

dialogue.text、nvl.append、layer.hide/replace、animation.stop、audio.seffect.stop、video.stop、effect.stop、particle.stop、variable.set 和 gallery.unlock 已采用 DTO 参数与强类型执行读取；effect.stop/particle.stop 共用 InstanceIdOptions。对白/NVL 保留 reading 标记与独立实例；Gallery 的正整数和 catalog 检查仍在原执行边界。Editor 含专属字符串编辑提示的 DTO 保留补全、多行文本、资源选择和表达式控件投影，普通字段仍使用共享 binding，不另维护参数表。

其余带参数的内置 Primitive 也采用 DTO：layer.show/showColor/move、animation.animate、timeline.play、audio.play/enqueue/seffect.apply、video.play、effect.apply、particle.play/burst。audio.play/enqueue 共用 AudioPlayOptions，enqueue 仍显式选择 Enqueue 策略。嵌套 JSON 字段保留 JsonElement 与原解析规则；固定标量、布尔和枚举使用强类型属性。动画 from 的省略由规范化参数 presence 判断，batchId 仍由编译信封提取；快照/重放与复杂实例生命周期未改变。

`animation.animate.property` 的内置选择包含 `time`，供 effect-program 元数据将该逻辑参数映射到 shader 时间 uniform；具体 program 仍需把对应参数标为可动画。

DynamicParameterTable 的 ValidatePartialArguments 用于尚未补默认值的局部作者参数；可选回调省略时沿用完整 validator。ForDto<T> 可显式提供 validatePartialArguments；字段自身校验照常执行，Normalize 的完整业务校验仍使用 ValidateArguments。内置视频/粒子/动画计划将完整业务规则保留在编译/执行边界，逐字段创作不要求其他尚未填写的字段。

所有 14 条内置 Composite 使用 ForDto<T> 接收输入，普通 schema 类型/选项/未知字段检查由同一 Normalize 执行；业务展开、正数/颜色/变换校验保持显式。语音子指令及计划 Primitive 外层参数通过 ValueCodec.SerializeFields 使用对应子 DTO 的字段 codec；undefined JSON 字段省略，null 默认保留，可显式选择省略 null。黑白与自定义颜色过渡共用源基类的 12 个字段；生成器合并继承字段并拒绝隐藏/重写或 Order/ID 冲突。

BuiltinEntryIds 集中声明 51 个内置稳定 wire ID。内置 Primitive 的作者类壳及取 ID 的泛型工厂已移除；catalog.Create 继续返回 Core 的通用作者表示，运行时依旧使用通用 PrimitiveEntry。项目仍显式选择 14 个独立 schema modules，不通过 CLR 类扫描启用指令。

Field.DefaultJson 声明经值 codec 校验的 JSON 默认，不能与属性初始化值同时声明；Choices 提供固定合法选项。StringBooleanValueConverter / StringEnumValueConverter 通过 IStringValueEncoding 保留字符串 wire 类型、选项及编辑文本，同时提供强类型成员；普通 converter 行为不改变。audio.stop 已用 AudioStopOptions，clearQueue 保持字符串 true/false 与显式 null 的旧行为。DefaultCompositeEntryBase.ForDto<T> 提供类型化编译展开，通过 PrimitiveArgumentHelper.NormalizeFields<T> 在归一化时直接填入独立 DTO，源 JSON 保留 null/presence，输出仍通过目标 primitive 契约规范化。

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
transition.reveal
transition.blur
preset.motion
preset.shake
preset.speedLines
preset.impact
```

这些 entry 不是 Runtime primitive，也不绕过 target profile：展开后的每一个 primitive 都必须在所选 profile 中存在。

视觉预设均编译为 `timeline.play`，shader 由 Entry 的 program / blurProgram 显式资源参数传递。编辑器插入/类型切换时从项目 defaultVisualPrograms 用途绑定填入 GUID；编译器不查找隐藏默认 shader，不新增各 shader 专用 primitive。project/group validate、单组编译和 build/export 在项目资源边界校验所需 shader stage、参数种类、可动画性与数值范围。震动/冲击目标为 targets[].handleId，参与场景引用索引、复制重映射和时间线终态分析。参数及停止边界见[默认视觉库手册](../manual/default-visuals.md)。

## 内置 primitive ID

`GalNet.Contracts.Builtins` 提供只依赖 Core 的作者契约；`GalNet.Primitives.Builtins` 提供运行时领域模块。`BuiltinSchemas.CreateAllModules()` 返回 14 个独立模块的只读列表，各组件也可通过 `Create<Domain>Module()` 单独选择；没有合成全内置大模块或内置专用 catalog 工厂。消费者使用 `new TargetProfileEntryCatalog(modules)`。

完整运行入口是 `GameRegistration.AddBuiltins`；也可单独调用 `AddBuiltinSchemas` 注册模块列表，再组合部分内置或自定义执行模块。内置 entry DTO、纯参数校验和 Composite 位于 `GalNet.Contracts.Builtins`，primitive instance 和执行 module 位于 `GalNet.Primitives.Builtins`；`GalNet.Core.Entry` 只包含通用基础设施。内容格式只保存下列稳定 `TypeId`，不保存 CLR 类型名。

`GameRegistration.UseCatalog(catalog)` 使用现有项目 catalog，禁止随后增加 schema。`AddBuiltinImplementations(catalog, ...)` 按该集合选择已声明的内置 primitive，不增加 schema；选择不拆散组件状态或释放所有权。宿主通过 `RequireInstructions(compiledTypes)` 检查实际内容需要的执行实现；可选 `RequireExecutionCoverage()` 检查整个 primitive 集合。工具契约加载不要求执行实现，外部游戏宿主缺少自定义实现时明确失败。

```text
dialogue.text, dialogue.textVoice, dialogue.show, dialogue.hide
nvl.show, nvl.hide, nvl.clear, nvl.append, nvl.appendVoice
layer.show, layer.showColor, layer.hide, layer.move, layer.replace
variants.show, variants.select
animation.animate, animation.stop
timeline.play
audio.play, audio.stop, audio.pause, audio.resume, audio.enqueue, audio.next, audio.clear
audio.seffect.apply, audio.seffect.update, audio.seffect.stop, audio.mix.set
video.play, video.stop
effect.apply, effect.stop
particle.play, particle.stop
flow.wait, variable.set, gallery.unlock
custom.invoke
```

`audio.play` 与 `audio.enqueue` 支持 `loopCount`（非负整数，默认 1，0 为无限）及 `completion`（`discard` 或 `requeueTail`，默认 `discard`）。`loopCount` 控制一个播放项的连续播放次数，`completion` 控制次数耗尽后是否回到队尾；失败或显式移除不会重新入队。Editor 使用数字输入和选项下拉编辑，使用方式见[音频重复与轮播](../manual/audio-playback.md)。

`gallery.unlock` 的参数是正整数 `id`，其值必须是当前 `GalleryCatalog` 中存在的稳定 Gallery item ID。执行时写入 `player.gallery_<id>_unlocked = true`；Gallery item 从资源 `.meta.gallery[]` 聚合，发行运行读取其生成快照。旧的 category/sequence 参数不受支持。

## 图片差分指令

`variants.show` / `variants.select` 属于既有 layers schema 与执行模块，内置模块数仍为 13，不增加第二张 Layer 状态表。

- `variants.show(handleId, assetId, selection?, transform?, z?, opacity?, displayMode?)`：assetId 指向已准备的 image-variants 资源，handleId 非空；selection 为维度 key 到字符串选项的对象，缺省 `{}`，不接受重复 key。以默认图的已有维度填初值，再应用显式选择；显式选择形成初次保护集合。transform 默认为 `{}`，支持与普通 Layer 一致的对象/嵌入 JSON 文本，要求有限值且缩放不低于 0.001；opacity 在 0–1 之间。
- `variants.select(handleId, dimension, value)`：仅改该实例的一个维度；同值重选也以该维度作为本次保护维度。未知维度/值是输入错误，合法值但缺组合才执行回退。不存在或已退出图集模式的 handle 明确报错。

优先完整匹配，再按维度从后向前累计删除未保护项，按剩余的原位置精确匹配；删除不是通配符，也不压缩中间位置。全未命中时使用显式 default。请求选择不因回退丢失，例如 smile_skirt → weap → smile 时可回到 smile_skirt。

两条指令在 Dispatch 返回前完成逻辑状态提交，无指令内异步 I/O，保持非阻塞、不可跳过及 batchId。Normal/Quick 更新状态后通知普通 Layer presenter，CatchUp 仅更新同样的状态，由 StateRestoreSession 重建显示。宿主先异步准备 `ImageVariantsCatalog`，再通过 AddBuiltins/AddBuiltinImplementations/AddBuiltinLayers 的 imageVariants 参数注入；未注入时旧的普通图层仍可运行，但图集资源查询会失败。

select 保留同一个 Layer 对象、变换、显示模式、层级、透明度及活动连续属性动画。hide 删除完整实例；普通 layer.replace/show/showColor 或视频来源替换退出图集模式，再执行 select 会报错；重新 variants.show 按默认/初始选择重建实例。图集与 Color/IsVideo/Flipbook 互斥，没有增加连续差分动画轨道或专属切图淡化。编辑器提供 show 的资源/初始选择、select 的对象/维度/值联动表单，场景静态分析保留图集来源、请求和实际图片事实；来源不唯一时提供保守候选，见[图集手册](../manual/image-variants.md)。

`nvl.show` 和 `nvl.hide` 只改变 NVL 覆盖层的可见性；`nvl.clear` 移除已累积的历史行；`nvl.append` 只接收长文本 `content`，以与 `dialogue.text` 相同的富文本和打字机语义追加一段，并在文字完成后等待一次玩家推进。它不会自动显示 NVL，剧情应显式调用 `nvl.show`。

这些名称和参数属于可选内置契约。开发者可以只注册自定义模块，或在自己的 target profile 中定义完全不同的前缀、参数与语义；编辑器不会凭名称假定 `dialogue`、`nvl`、`layer` 或任何其他模块存在。

当前已实现 Runtime 行为的内置能力见 [runtime.md](runtime.md)。

`custom.invoke` 属于独立的 custom schema 模块，参数 DTO 为 `CustomInvokeOptions`：`action` 和 `argument` 均为无默认值、不可为 null 的必填 string，action 额外拒绝空白；argument 可为空。参数内容不求值、不解析也不标为本地化文本。编译期不检查 action 注册或其参数语法。执行模块与模式策略见[通用 action 手册](../manual/custom-actions.md)。

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

Timeline 使用 TimelineOptions.Timeline 对应的 timeline 根参数（JsonElement），内容为 TimelineDefinition；播放接口为 PlayTimelineAsync。旧 animation.play ID 和 plan 根参数没有别名。动画轨道、键与事件分别使用 TimelineTrackDefinition、TimelineKeyframeDefinition、TimelineEventDefinition，纯采样入口为 TimelineSampler。

Timeline 事件的 `execution` 默认为 `EachIteration`，按帧逐轮执行；`Once` 仅允许帧 0，属于整次播放的初始化；`OnCompletion` 仅允许结束帧的 layer.hide/effect.stop/particle.stop，属于整次播放的收尾，不随循环触发。恢复不重复已提交的首帧初始化。震屏/集中线预设支持 `playbackMode=Timed/UntilStopped`；持续模式编译为非阻塞、不可点击跳过的 Loop，集中线的创建和移除分别使用 Once/OnCompletion。其他预设保持单次。

音频效果条目目标为 `scope`（track/bus）与必填 `targetId`；播放/轨道控制仍使用 `trackId`。效果参数和新 PCM 后端见 [音频混音](../manual/audio-mixing.md)。

`audio.seffect.update` 使用 `AudioSEffectUpdateOptions`，以 `instanceId` 引用已有实例，`parameters` 为字段 patch；合并后验证，缺失实例无操作。`audio.seffect.stop` 的 `all` 默认 false；true 时停止目标上的全部效果且不能同时指定非空实例身份。Editor 自动隐藏 apply 的输出身份，类型/参数及 stop/update 的引用选择见 [作者目标查询](editor-scene-targets.md)。
