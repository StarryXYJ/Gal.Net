# Phase Plan：Entry 模块与 PrimitiveInstance 运行时收敛

## Phase 1：核心 Entry 与实例契约

状态：verified

**目标：** 用两张冻结 Entry 表和 PrimitiveInstance 工厂替代 Descriptor/Handler 双重注册。

**前置条件：** 当前动态参数表和 target profile 改动保留；编译文件仍输出通用 PrimitiveEntry。

**涉及模块：** `GalNet.Core`、`GalNet.Primitives.Builtins`、`GeneralTest`。

**任务：**

1. 定义 `EntryBase`、`PrimitiveEntryBase`、`DefaultPrimitiveEntryBase`、`CompositeEntryBase` 和 `PrimitiveInstance`。
2. 让每个 Entry 定义持有唯一参数表，增加只读取该表的无状态参数 helper，并让工厂上下文暴露参数表、规范化参数 JSON、Runtime、Scope cancellation 和本次 BatchId。
3. 将模块收敛为冻结的 Primitive/Composite 两张表，并验证模块 ID、Entry 名称和重复注册。
4. 调整 target profile 从 Entry 定义直接建立编辑器/编译目录。
5. 迁移推荐 authoring 模块定义，把编辑器/Composite 提供的可选 BatchId 写入通用 `PrimitiveEntry` 及序列化 DTO，不生成全局 ID。
6. 删除被替代的 Handler、Dispatch Policy 和 ExecutionControl 契约。

**测试：** 模块冻结、重复项、默认工厂、共享参数 helper、工厂上下文字段、可选 BatchId 原样传递、实例单次 Dispatch/幂等完成、空 profile 和纯 Composite 模块。

**风险：** 当前工作树包含未提交的模块表迁移，修改时必须保留已经成立的 DynamicParameterTable 与 transition authoring 迁移。

**验证（2026-09-22）：** `EntryBase`、`PrimitiveEntryBase`、`DefaultPrimitiveEntryBase`、`CompositeEntryBase`、`DefaultCompositeEntryBase`、`PrimitiveInstance` 与 `DefaultEntryModule` 已实现；模块两表使用只读字典冻结。旧 Handler、Dispatch Policy 与 ExecutionControl 契约已删除。工厂上下文、共享参数规范化和编译数据 BatchId 均有自动化测试覆盖。

**退出条件：** Core 不再需要 Handler/Policy 才能描述一次原语调用；工厂上下文与编译数据 BatchId 测试通过；相关 Core/Entry 测试通过。

## Phase 2：Engine 队列与 Advance 调度

状态：verified

**目标：** 让 `IGameView` 只执行单条 primitive 调用，由 `GameEngine` 唯一拥有活动队列并通过 `AdvanceAsync` 协调 Skip、Group 步进、节点跳转和稳定快照。

**前置条件：** Phase 1 的 PrimitiveInstance 工厂可用。

**涉及模块：** `GalNet.Presentation.Abstractions`、`GalNet.Presentation.Defaults`、`GalNet.Runtime`、`GeneralTest`。

**任务：**

1. 重写 `CompositeGameView` 的完整 ID 路由、参数规范化、实例创建和单次 Dispatch，并删除其活动队列。
2. 在 Engine 实现 `GroupExecutionId`、Sequence、活动实例队列和完成清理。
3. `GameEngine.AdvanceAsync` 在调用开始时只选择最早 Blocking 实例的局部 Batch，并 Skip 同一次 Group 执行、同 Batch 的全部已分发可跳过实例，包括 NonBlocking。
4. 当前阻塞解除后继续顺序分发到下一阻塞点，但本次调用不得 Skip 新遇到的 Batch。
5. Blocking 自然完成只触发无 Skip 权限的私有 continue；NonBlocking 完成不唤醒 Engine，在后续步进、Group 结束或 Dispose 时惰性清理。
6. 将条件、Choice 和 Jump 保留为 Engine 内置控制流；Choice 通过窄展示端口取回可见索引，不再创建 `interaction.choice` primitive。
7. Choice 等待作为 Engine 流程阻塞，在调度门外等待；完成后回到门内校验索引、跳转并继续。
8. 用同一个异步调度门串行化 Advance、Blocking 完成通知和 Engine 队列清理，完成回调只排队、不递归推进。
9. 删除 Runtime `OperationManager`，调整 Engine 与 NullGameView。

**测试：** 一次 Advance 只 Skip 一个既有 Batch、解除后运行到下一阻塞点、同 Group 同批跨 Blocking 属性 Skip、跨 Group 同名 Batch 隔离、动态 Skippable 原子切换、Blocking 自然完成继续、NonBlocking 惰性清理、条件分支、Choice 索引映射/无效索引/取消/读档重建、Choice 不响应 Advance、未知原语安全跳过、完成/Skip 幂等和完成通知重入。

**风险：** 无公开 Pump 时，Blocking 定时完成必须通过实例完成通知唤醒调度，不能复用玩家 Advance；NonBlocking 不唤醒，只惰性清理。

**验证（2026-09-22）：** `CompositeGameView` 只解析并分发一条 primitive；活动队列、局部 Batch、自然完成、条件、Choice 和节点跳转均由 Engine 管理。推荐模块集成测试覆盖 Layer、Dialogue、未知指令跳过和 Choice 可见索引映射的完整路径。

**退出条件：** Engine 是活动实例队列唯一所有者；剧情阻塞和局部批次跳过由新队列测试覆盖；玩家入口真正统一为 `GameEngine.AdvanceAsync`；旧 OperationManager 无引用。

## Phase 3：快照边界与 Dialogue `\skip`

状态：verified

**目标：** 落实无 Blocking 的快照边界，并将打字机分段跳过放入 dialogue 模块实例。

**前置条件：** Phase 2 的实例队列和内部继续路径可用。

**涉及模块：** `GalNet.Runtime`、`GalNet.Core.Text`、`GalNet.Avalonia.Controls`、`GalNet.Avalonia.GameView`、推荐 dialogue 模块、`GeneralTest`。

**任务：**

1. 将快照更新条件改为逻辑游标已提交且无未完成 Blocking/Choice 流程等待，不要求活动队列为空，并删除 `CreatesCheckpoint` 判断。
2. 提取共享打字机指令识别并加入 `\skip` 与 `\\`。
3. 实现 dialogue PrimitiveInstance 的分段跳过和全文后再次 Advance 完成语义。
4. 保证 Skip 不能被旧异步写入覆盖最终显示状态。
5. 迁移现有 Avalonia/Headless 对话入口。

**测试：** 普通/富文本一致解析、转义、连续 `\skip`、富文本跨边界、NonBlocking 活动时快照更新、Blocking 期间保留旧快照。

**风险：** 当前 TypewriterTextBlock 的 Skip 会永久立即显示剩余全文，不能直接作为新分段语义使用。

**验证（2026-09-22）：** Engine 快照条件已改为“无未完成 Blocking 实例”。普通/富文本解析共享反斜杠指令识别；Dialogue PrimitiveInstance、Avalonia 与 Headless 均已迁移。Headless 在打字完成后通过同一个 `GameEngine.AdvanceAsync` 入口继续剧情。

**退出条件：** 对话每次 Advance 至多跨一个边界；存读档不序列化活动实例且稳定状态测试通过。

## Phase 4：推荐模块迁移与清理

状态：partially verified

**目标：** 迁移其余推荐原语并删除旧执行路径和矛盾文档。

**前置条件：** 前三阶段契约稳定。

**涉及模块：** 全部推荐 primitive 模块、Sample、Editor Preview、文档和测试。

**任务：**

1. 迁移 layer、animation、effect、particle、flow、variable、gallery 及现有媒体模块；animation plan 整体作为一个 instance，不迁移通用嵌套 Entry 分发。
2. 长期行为使用可序列化 Handle，启动实例及时完成。
3. 删除 Avalonia 页面旧活动动画批次决策和 Runtime Handler 残留。
4. 同步 `primitive-module-runtime-design.md`、长期 phase plan、runtime 和 entry spec。

**测试：** 解决方案构建、全量单元测试、Editor/Headless 组合根测试及关键运行路径冒烟。

**风险：** 音频系统仍有独立 discovery feature；本阶段只迁移现有能力，不扩张其产品范围。

**退出条件：** 新模型是唯一 Entry 执行路径，旧契约无引用，相关文档与实现一致，全量验证通过。

**当前范围（2026-09-22）：** 本轮已在新 instance 模型下实现 layer、dialogue/typewriter、Engine 内置流程跳转、animation、effect、flow.wait 和 variable.set，并接入 Avalonia Sample、Editor Preview 与 Headless Sample 的组合根。Animation plan 整体作为一个 `AnimationPlanPrimitiveInstance`；内部 layer/effect 事件仅作为 animation 模块私有事件处理，不重新进入通用 Entry 分发。Audio、video、particle 和 gallery 仍保留推荐 schema，完整产品行为留给后续 feature。

**计划变更（2026-09-24，设计已确认）：** 已实现的 `gallery.unlock` 仍临时通过 `IGameProgressService` 写入玩家级进度；Gallery 的最终方向已确认为内置、平台无关的数据层，而不是通用功能模块。Gallery 类型只登记 `(typeId, resourceTypeName)`，item 使用稳定字符串 ID 并生成 Player bool，Avalonia 自主消费类型、资源和解锁状态。完整 Gallery 产品切片移到 Phase 5-8；Phase 4 只负责确认现有 primitive 已进入新 instance 执行路径。Audio、video 与 particle 的剧情 primitive 产品行为仍等待各自范围确认。

**验证证据（2026-09-22）：** `GeneralTest` 全量 219 项通过；新增测试覆盖 animation/effect runtime state、presenter 调用、plan final state 和 `flow.wait` skip。受限环境中构建/测试需设置 `AVALONIA_TELEMETRY_OPTOUT=1` 并传入 `-p:UseSharedCompilation=false`。文档已重写 `docs/spec/runtime.md`、`docs/spec/entry-types.md`、`docs/spec/architecture.md`、`docs/glossary.md`、`docs/design/primitive-module-runtime-design.md` 和长期 phase plan。

## Phase 5：Gallery 数据契约与内容加载

状态：verified

**目标：** 用字符串类型注册和稳定 item ID 建立平台无关的 Gallery catalog，并让所有内容提供路径交付同一份数据。

**前置条件：** Phase 4 的 Entry instance 路径保持可用；当前临时 Gallery progress 行为暂不删除。

**涉及模块：** `GalNet.Core`、`GalNet.Storage.Abstractions`、`GalNet.Storage.FileSystem`、`GalNet.Editor.Shared`、Sample/Editor Preview 内容提供者、相关测试。

**任务：**

1. 将目标模型从 `GalleryCategory`、`SequenceId`、`IsVideo` 收敛为 `GalleryTypeRegistration(TypeId, ResourceTypeName)` 与稳定字符串 ID 的 `GalleryItem`。
2. 建立冻结的 Gallery 类型集合，拒绝空 ID、重复 type ID、无效资源类型字符串和 item 对未知类型的引用。
3. 定义只读 Gallery catalog/data source，能够按类型返回资源条目；UI 所需的 `IsUnlocked` 在 Phase 6 接入，当前接口应预留而不引入 UI 类型。
4. 将 Gallery catalog 放入 `GameContent`，同时接通目录加载、Editor Preview 和导出输入；不把标注写入媒体源文件。
5. 为当前 `ResourceType` enum 和 `.galgroup` 内容提供名称适配，但不改造资源类型系统或 pak 编码。

**测试：** 类型规范化、重复注册、稳定 item ID、未知类型引用、按类型聚合、资源类型字符串保留，以及 FileSystem/Editor Preview 内容等价性。

**文档：** 在实现验证后同步 Gallery catalog 文件格式和 `GameContent` 当前事实；实现前不改 `docs/spec`。

**风险：** 当前 `ResourceType` 是封闭 enum，而 `.galgroup` 不走 `IAssetManager`；适配层必须显式产生稳定字符串，不能假装资源系统已经模块化。

**验证（2026-09-25）：** 已实现版本 1 的 `gallery.json` DTO、验证后冻结的 `GalleryCatalog`、稳定字符串 type/item ID、按类型和 item 查询，以及 `GameContent.Gallery`。目录内容提供者与 Editor Preview 均读取项目根目录的可选 `gallery.json`，导出器会把该文件收入 `Assets/content.pak`；共享 `GameTestCase` 已包含一个真实 CG 条目。旧 `GalleryCategory` 仅为 Phase 6 前的 progress-backed primitive 单独保留，新 catalog 不使用它。`GeneralTest` 228/228 通过。全解决方案构建中 Core、Storage、Editor、Desktop Sample、Headless Sample 和测试项目均编译成功；Android、Browser、iOS 仅因受限环境无权枚举 `C:\Users\Starry\AppData\Local\Microsoft SDKs` 而未完成平台 target 验证，已记录为 agent lesson。

**退出条件：** Headless、Avalonia Sample 和 Editor Preview 都能读取相同 Gallery 类型与 item 集合，Core/Runtime 中没有 Avalonia 类型或页面信息。

## Phase 6：系统 Player bool 与 `gallery.unlock`

状态：verified

**目标：** 让 Gallery 解锁以生成的 Player bool 为唯一真源，并移除长期双写风险。

**前置条件：** Phase 5 catalog 能稳定枚举 item ID。

**涉及模块：** `GalNet.Core`、`GalNet.Runtime`、`GalNet.Primitives.Builtins`、变量服务、`GalNet.Storage.FileSystem`、Editor player-variable store、相关测试。

**任务：**

1. 定义 `gallery_<item-id>_unlocked` 的规范化、验证和无碰撞生成规则；名称不包含 Gallery type ID、资源路径或标题。
2. 从 Gallery catalog 投影不可删除、默认 `false` 的系统 Player bool，并与用户变量定义进行冲突检查。
3. 让文件与 Editor 变量服务将生成名称稳定解析为 Player scope，重载时不得清理系统变量。
4. 把 `gallery.unlock` 参数收敛为稳定 item ID，经 catalog 校验后调用 `IGameRuntime.SetVariable()`；保持同步 NonBlocking instance。
5. 删除 `IGameProgressService` 的 Gallery 专用方法和 `GalleryEntries` 持久化；如果确认存在已发行旧数据，则增加一次性导入，不保留双写。
6. 让 Gallery data source 合并 catalog 与 player variables，对 UI 只返回 `IsUnlocked`。

**测试：** 默认未解锁、primitive 解锁、通用变量操作解锁、跨存档槽保留、普通读档不回滚、Editor 重载保留、名称冲突、未知 item 拒绝，以及旧 progress 迁移策略（若需要）。

**文档：** 实现验证后更新变量 scope、Gallery 解锁和 progress 存储事实。

**风险：** `EditorPlayerVariableStore` 当前会清理不在项目声明中的 player 变量；系统变量投影必须先落地，再切换 primitive 真源。

**验证（2026-09-25）：** Gallery item 现在确定性生成 `gallery_<item-id>_unlocked` 系统 Player bool；文件变量服务和 Editor 变量服务均接受通用系统变量定义并初始化默认值，Editor store 会在 catalog 尚未配置前保留符合保留规则的已有 Gallery 变量。`gallery.unlock` 已收敛为稳定 item ID、先经 catalog 校验，再通过 `IGameRuntime.SetVariable("player....", true)` 写入唯一真源。`IGameProgressService` 的 Gallery API、`GalleryEntries` 与旧 `GalleryCategory` 已删除。`GalleryDataSource` 合并 catalog 与 Player snapshot，只向 UI 投影 `IsUnlocked`。自动化测试覆盖默认值、primitive、未知 ID、跨重载持久化、读档不回滚、查询投影与用户变量名称冲突；`GeneralTest` 232/232 通过。

**退出条件：** Gallery 解锁只有 Player bool 一个真源，旧 progress 集合已移除或仅作为一次性迁移输入，UI 查询与剧情条件观察到相同状态。

## Phase 7：Editor Gallery 标注与聚合

状态：verified

**目标：** 让资源编辑入口根据资源类型字符串创建集中式 Gallery item，并覆盖预览和导出工作流。

**前置条件：** Phase 5 catalog 契约稳定；Phase 6 的 item ID/变量规则可用于即时校验。

**涉及模块：** `GalNet.Editor.Abstraction`、`GalNet.Editor.Shared`、`GalNet.Editor`、项目命令/撤销保存、预览与导出、相关测试。

**任务：**

1. 在资源选择或检查器中，根据所选资源的类型字符串筛选全部匹配 Gallery type registration。
2. 提供新增、修改、移除 Gallery 标注的项目命令，生成不可变 item ID，并支持标题与排序等内容元数据。
3. 将集中 catalog 纳入 EditorProjectDocument、保存调度、撤销/重做、项目重开和导出。
4. 检查资源删除/移动后的引用诊断；不修改图片、音频、视频或 galgroup 源文件。
5. 在 Editor 中显示生成的系统变量为只读定义，并阻止用户声明同名变量。

**测试：** 同一资源类型对应多个 Gallery 类型、标注增删改与撤销重做、项目重开、资源引用失效诊断、预览/导出一致性和系统变量只读冲突。

**文档：** 实现验证后记录 Gallery authoring 文件位置、标注流程与导出行为。

**风险：** 资源移动的身份更新能力在现有资源系统中可能不完整；首轮必须至少保留稳定 asset ID 或产生明确的断链诊断。

**验证（2026-09-25）：** Editor 聚合中的 `GalleryConfiguration` 以 `[JsonIgnore]` 留在图文档内存模型、由 repository 单独读写项目根 `gallery.json`；新建或旧项目缺省获得 `cg -> sprite`、`video -> video`、`audio -> audio` 三个内置 registration。资源检查器按 `.meta` 的资源类型字符串反查全部匹配类型，可对同一资源分别标注，创建时生成稳定 GUID item ID，后续标题/排序修改保持 ID；改动进入 Graph undo/redo 与保存调度。Preview 会把当前内存 catalog 写入临时目录再加载，导出会验证已知媒体类型的 asset ID 与类型，删除资源后明确失败。Headless editor 命令已增加 Gallery type/item 的注册、更新与删除；生成变量在条件建议与 Preview Inspector 中以只读名称显示。持久化、命令稳定 ID、内置类型、导出断链测试均通过；`GeneralTest` 235/235 通过。

**退出条件：** 用户无需手写 JSON 即可把图片、音频和视频加入 Gallery，保存、重开、预览和导出均得到同一 catalog。

## Phase 8：Avalonia Gallery 导航与默认 UI

状态：verified

**目标：** 由 Avalonia 完全消费 Gallery 数据，完成零/单/多类型导航以及图片、视频、音频默认展示。

**前置条件：** Phase 5-7 能提供带 `IsUnlocked` 的稳定 Gallery 数据；媒体资源读取服务可由宿主注入。

**涉及模块：** `GalNet.Avalonia.GameView`、必要的 Avalonia 媒体适配、Sample/Editor Preview 组合根、相关测试。

**任务：**

1. 增加前端 Gallery launch coordinator：零个有内容类型时隐藏/禁用入口，一个类型时直达内容页，多个类型时进入类型选择页，并保持正确返回历史。
2. 让前端优先按 Gallery type ID 选择专用 UI，未命中时按资源类型字符串选择默认 UI；该映射不写入 Core catalog。
3. 实现图片网格：锁定占位与文字/图标、已解锁缩略图、完整图片查看和已解锁项切换。
4. 实现视频网格：锁定占位、首帧缩略图缓存和完整播放页。
5. 实现音频列表：曲名、播放/暂停、时间与进度，并保证同一会话只有一个活动播放项。
6. 对没有可用 UI 的类型给出可诊断的前端状态；自定义类型可复用资源类型默认 UI或由宿主提供专用 UI。

**测试：** 零/单/多类型导航、返回栈、类型专用 UI 优先级、资源类型 fallback、锁定资源不展示内容、图片全屏、视频播放入口、音频单播放会话和无 UI 诊断。

**文档：** 实现验证后同步 Avalonia Gallery 页面与宿主扩展点；场景回放仍标为未实现。

**风险：** 视频首帧若在每次页面进入时实时解码会造成明显延迟；应使用导入/构建产物或受控缓存。锁定状态不能只使用灰度颜色表达。

**验证（2026-09-26）：** `IGameGallerySession` 作为可选宿主能力向 Avalonia 暴露 `IGalleryDataSource` 与资源 ID 解析器，Core/Runtime 未引入 UI 依赖。标题页只消费有内容类型：零类型隐藏入口，单类型直接激活内容页，多类型进入类型选择页；返回历史由现有 navigation service 保持。前端先按 `typeId` 选择 `cg`/`video`/`audio` 专用 renderer，未命中再按 `sprite|image`、`video`、`audio` 资源类型 fallback，未知类型显示诊断。图片和视频使用带 `Locked` 文本的纯色锁定卡片；解锁图片可完整查看，视频首帧写入临时 SHA-256 缓存并进入 LibVLC 播放页；音频列表显示曲名、播放/暂停、时间与进度，整个 game scope 共享一个播放器。缩略图后台加载，不阻塞页面导航；Sample 复用 `GameContent.AssetRoot`，Editor Preview 使用项目 `Assets` 根，目录解析器对嵌套 `.meta` 有定向测试。Windows Sample 与 Editor 条件性携带原生 LibVLC runtime。导航分派、renderer fallback 与资源 ID 解析测试已覆盖，`GeneralTest` 245/245 通过，Sample Avalonia 与 Editor 构建通过。

**退出条件：** 默认 Avalonia 宿主可完整浏览 CG、视频和音频 Gallery；Core/Runtime 未新增 UI 依赖，未知自定义类型不会导致导航崩溃。
