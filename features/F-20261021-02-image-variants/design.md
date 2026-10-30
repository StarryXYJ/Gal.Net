# 多维图片差分图集设计

日期：2026-10-21
状态：实施中；P1–P3 已实现，见第 12–14 节。P4a 场景适配及 P4b 作者界面已接入，验证记录见各阶段文件；最终集成验收仍待 P5。

## 1. 问题与设计取向

立绘和场景经常有表情、服装、时间、天气等差分。逐条指定完整图片引用，会把组合规则和缺图处理散落在剧情中。本设计将一组完整图片组织成一个差分图集，由实例记住选择，再解析为一张普通图片。

“图集”在此表示图片变体集合，不表示纹理打包 atlas，不替代已有 Flipbook，也不等同于供玩家回看的 Gallery。

核心原则：文件名便于导入，维度有稳定身份；选择状态不因缺图丢失；回退可解释、可复现；绘制复用 Layer；运行时不扫描文件系统。

## 2. 当前事实与接入依据

- [资源规范](../../docs/spec/content/assets.md)：资源以 `.meta.id` 为正式身份，类型通过冻结的资源类型 catalog 注册；资源识别和 decoder 分离，发行资源由 PAK 提供。
- [条目规范](../../docs/spec/runtime/entries.md)：契约和执行模块分离，项目显式选择 schema 和实现；新增能力不能只注册编辑器 UI。
- `src/Shared/GalNet.Core/Scene/Layer.cs`：Layer 已有 AssetId、变换、透明度、显示模式和可选 Flipbook，属于可动画场景实例。
- `src/Shared/GalNet.Primitives.Builtins/Modules/BuiltinLayerModule.cs`：`builtin.layers` 已负责 Layer 状态和非视频可见图层恢复，Normal 与 CatchUp 共用状态操作。
- `src/Shared/GalNet.Primitives.Builtins/RuntimeActions/BuiltinRuntimeActions.Layers.cs`：状态修改后通知 presenter；现有 ReplaceLayer 重建 Layer，不会自动保留未来新增的差分状态。
- `src/Shared/GalNet.Presentation.Abstractions/View/LayerRenderRequest.cs`：展示请求已接收实际图片资源 ID，无需让 renderer 理解维度匹配。

上述为已检查事实；下文描述新增目标，不提前写入正式 spec。

## 3. 作者格式与导入

建议新增 `.variants` JSON 资源描述文件，类型暂称 `image-variants`；它和每张图片一样具有稳定资源 ID 的 sidecar。路径示例：

```text
Assets/Characters/alice.variants
Assets/Characters/alice.variants.meta
Assets/Characters/alice/smile_coat.png
Assets/Characters/alice/smile_skirt.png
Assets/Characters/alice/idle_coat.png
Assets/Characters/alice/idle_skirt.png
Assets/Characters/alice/weap.png
```

```json
{
  "version": 1,
  "folder": "./alice",
  "dimensions": [
    { "key": "expression", "label": "表情" },
    { "key": "outfit", "label": "服装" }
  ],
  "default": "idle_coat"
}
```

- folder 相对描述文件，只允许指向 Assets 内目录；第一版只识别该目录直接包含、且注册为 sprite 的静态图片，不递归。导入时建立各图片的正常资源身份。
- 文件主名按 `_` 分割，位置对应维度顺序；段数可不同，没有固定维度上限。空段、空主名非法；第一版不支持段内转义 `_`。
- 段值采用大小写敏感的 ordinal 精确比较，不做 trim 或模糊归一化。路径大小写不能成为区分两个变体的手段；仅大小写不同的文件名拒绝导入，避免跨文件系统差异。
- 多扩展名产生同一变体键，例如 `idle.png` 与 `idle.webp`，属于错误；禁止取扫描到的第一张。
- dimensions 可省略：按最大段数生成稳定位置 key `d1`、`d2` 等，标签显示“1”“2”等。显式提供时，key 非空且唯一，维度数不得少于最大段数；可多于当前图片段数以便后续补图。
- label 只负责显示；剧情按维度 key 引用，改 label 不改变选择。重排维度或改 key 会影响语义，编辑器须提供影响诊断，不能视作普通改标签。
- 选项来自该位置实际出现的段值。编辑器可显示 `1-idle` 等位置提示；配置 label 后显示“表情：idle”。
- default 必须明确指向已存在的完整变体键。创建向导可按 ordinal 排序预选，但必须写入文件；不得以未定义的“第一张”作为运行时回退。

目录增删/改名刷新索引并呈现错误，避免继续悄悄使用过期目录快照。第一版不加入手工逐张维度映射或别名。

## 4. 选择状态与实际显示

每个显示实例分别保存：

```text
图集资源 ID
请求选择：dimensionKey → value
本次被保护的维度集合
实际解析的变体键与图片资源 ID
解析原因：Exact / Reduced / Default
```

请求选择是玩家指令意图；实际图片是解析结果。回退不改写请求选择。不同实例即使引用同一图集，也不共享选择。

显示时先用默认图已有段值初始化选择，再覆盖显式传入的初始选择。默认图没有的维度保持未选择，不擅自填该维度第一项。显式提供的初始选择维度作为本次保护集合；没有覆盖时直接显示默认图。

切换时仅更新指定维度。本次指定的维度即被保护，即使值与上次相同也按本次指令重新解析。不存在的维度 key 或从未出现在该维度的值属于输入错误；“各值合法，但组合缺失”才进入回退。

示例：

```text
请求 smile + skirt，实际 smile_skirt
切换 expression=weap：请求 weap + skirt，实际 weap
切换 expression=smile：请求 smile + skirt，实际 smile_skirt
```

## 5. 精确匹配与回退算法

采用“删除未修改的维度后，按剩余位置精确匹配”的规则。删除不会使该维度变为通配符，也不会替换成另一个值。

设 Q 为完整请求映射，P 为本次保护维度集合：

1. 查找维度映射与 Q 完全相同的变体。
2. 若未命中，从最后一个维度向前遍历；遇到 Q 中存在且不在 P 的维度，删除该项，并用删除后的映射再做一次完全匹配。
3. 删除是累积的，不重新组合其他子集；跳过被保护维度及未选择维度。
4. 所有候选未命中，返回显式 default。只有最终 default 可以不满足本次保护值。

第一版文件名只能表达从第一个维度开始的连续前缀。因此候选若只剩第 2 维，不能拿一个单段文件当作第 2 维，也不能把它压缩成第 1 维。该候选就是未命中。这是位置式命名的明确边界，不加入隐含通配符。

| 请求与操作 | 候选顺序 | 结果说明 |
| --- | --- | --- |
| idle_skirt，切表情 smile | smile_skirt → smile | 有完整图优先完整图 |
| idle_skirt，切表情 weap | weap_skirt → weap | weap 存在则命中；请求仍保留 skirt |
| smile_coat，切服装 skirt | smile_skirt → 仅第 2 维 skirt | 若没有 smile_skirt，不会自动选 idle_skirt；最终默认图 |
| smile_coat_night，切第 1 维 idle | idle_coat_night → idle_coat → idle | 从后向前累积删除 |
| smile_coat_night，切第 2 维 skirt | smile_skirt_night → smile_skirt → 仅第 2 维 skirt | 不删除本次指定的第 2 维 |

多维初始覆盖采用同一算法，保护集合可有多个成员；第一版切换指令只改一个维度，避免引入多条同时切换的批次排序语义。

返回结果必须包含实际变体、被削减的维度和是否使用默认图。不得用有权重的“最接近图片”替换本算法。维度顺序同时决定削减优先级，编辑器应说明：越靠后的未修改维度越先放弃。

## 6. 编辑器交互

- 资源面板识别独立图集资源；属性区编辑目录、维度 key/标签及默认图，展示扫描错误和图片数量。
- 图集预览按维度提供选项；选项来自全图集，不因当前组合缺图而消失。允许主动选择会触发回退的值。
- 同时展示“当前选择”“实际图片”；出现回退时显示如“服装 skirt 未命中，实际 weap”，并可查看候选链。
- 独立的图集显示和切换条目使用资源选择器、场景对象选择器和依赖维度的选项控件。只显示已知目标的维度，无法静态确定时保留合法输入并给出不完整诊断，不伪造确定结果。
- 描述文件及资源操作复用项目事务、撤销与保存机制；目录刷新属于事实发现，不自动形成作者修改或重写文件。
- 发布校验检查非空图集、default、重复变体键、维度定义和全部图片引用。画布尺寸不一致给出提示；不通过尺寸相近推断锚点或裁切。

## 7. 资源和发布数据流

```text
描述文件 + 图片目录/sidecar
  → 编辑器/构建共用索引编译器
  → 校验后的不可变定义：维度、选项、默认键、变体键 → sprite ID
  → 本地预览或发布 PAK 中的运行时定义
  → 实例请求状态 + 纯解析器
  → 实际 sprite ID
  → 普通 Layer / ILayerPresenter
```

目录路径仅用于作者阶段收集；发行定义不包含对目录扫描的依赖。各图片仍是独立 sprite 资源，描述资源保留原稳定 ID，发布时其 payload 编译为运行时定义。需验证本地 decoder 与打包 producer 对该类型的协作，不能把 JSON 源文件不经处理直接当作发行索引。

移动图片时 sprite ID 不变；通过构建重编译索引。移动目录或描述文件时，由作者资源事务修正相对目录引用。改文件名会改变变体键，须报告引用/default/请求影响，不能承诺仅靠 sprite ID 保持维度语义。

坏描述、缺图、资源类型不符属于加载/构建错误，不进入“组合不存在”的正常回退。打包必须包含图集引用的全部图片依赖。

## 8. 模块职责和显示接口

- Core：平台无关的定义 DTO、位置映射与纯解析器、Layer 上可选差分状态；不扫描目录、不解码平台图片。
- Assets 与作者/构建基础设施：资源类型、定义 decoder、共享索引编译及发布依赖收集；文件系统遍历留在具备文件访问能力的适配层。
- Contracts.Builtins：图集指令参数、schema 与纯参数校验；动态资源事实由项目校验服务补充。
- Primitives.Builtins：加载定义、修改唯一 Layer 状态、解析实际 sprite、通知已有 presenter；目标模块以现有 Layer 模块为状态归属，避免重复注册 `builtin.layers`。
- Editor：图集属性、预览与字段适配；使用同一解析器和索引结果。
- Presentation/Avalonia：继续接收实际图片 ID，不自行匹配文件名。

建议独立作者入口（wire 名待定）：

```text
variants.show(handleId, assetId, selection?, transform?, z?, displayMode?, opacity?)
variants.select(handleId, dimension, value)
```

hide/move/普通连续动画继续使用 Layer 的现有能力。show 建立一个普通 Layer，附加差分状态，首次显示默认或解析后的图片。select 保留 ID、变换、透明度、层级、显示模式和已有连续属性动画，只更换实际图片及差分选择状态。

静态图、颜色、视频、Flipbook 和差分图集属于互斥来源。普通 `layer.replace` 明确退出图集模式并清除差分状态；不能留下“图片已换但后续 select 又切回”的悬空绑定。再次 show 同一 handle 按现有替换实例语义重置，hide 删除该实例全部状态。对非图集或不存在的 handle 执行 select 是明确执行错误。

第一版不为差分选择添加连续动画轨道，不附加专属交叉淡化；图片对齐沿用 Layer 的布局方式。推荐同一图集统一画布和人物基准位置，不自动补齐或调整锚点。

## 9. 状态、异步和恢复

P3 实施修订：现有 `IGameView.ValidateState` 是同步的读档预检，并在状态根替换前调用。为保持该边界且不以同步等待阻塞异步资源加载，宿主在会话准备时异步加载并验证全部图集，注入不可变 `ImageVariantsCatalog`。show/select 随后只做同步查找和原子状态修改；没有指令内 I/O，也不需要异步切换队列。定义加载的取消/失败在会话准备阶段处理，旧 scope 的任务不会持有新实例。下面原异步风险通过此预加载方案消除，不扩展或改写 Engine 的恢复协议。

- Layer 是唯一实例真源；在其可选差分状态中保存图集 ID、完整请求、保护集合及实际变体。AssetId 表示当前实际图片，不能在第二张状态表再维护一份平行 Layer。
- 定义加载/校验先完成，再在受控执行边界内原子更新请求和实际图片；失败不提交一半选择。并发 select 应保持引擎执行顺序，并校验目标实例仍属于同一次 show，避免旧加载覆盖新实例。
- Normal 与 CatchUp 共用解析和状态提交；CatchUp 不触发展示副作用，最终由统一状态恢复还原画面。
- 存档记录请求和实际结果。恢复时加载同一图集定义，用保存的保护集合重算并验证实际结果，再沿用已有 Layer 恢复路径。保存保护集合是必要的：不同最后修改维度可能产生不同回退。
- 同内容版本中恢复必须确定；内容更新导致维度/变体/图片缺失时报告存档与内容不一致，不静默丢请求。具体快照版本调整随实施变更评估，不提前宣称兼容旧格式。
- 图片句柄复用现有 presenter 的 acquire/释放机制；定义句柄按实例或项目作用域明确释放。快速切换的过期加载、hide 与关闭均须验证。

## 10. 候选方案与取舍

| 方案 | 取舍 |
| --- | --- |
| 剧情直接写完整图片 ID | 简单，但组合和回退分散，不能保留独立维度意图 |
| 文件夹运行时扫描 | 作者方便，但不适合 PAK 和稳定资源身份；改为作者扫描并编译 |
| 放弃条件即通配符，选择最接近组合 | 可覆盖更多缺图，但会自动改成其他表情/服装，候选并列复杂；第一版不采用 |
| 显式逐图维度映射与优先回退树 | 能表达中间维度缺省和复杂规则，但增加作者负担；留待真实需求 |
| 独立差分 renderer 和状态表 | 重复图层动画、恢复与资源生命周期；复用 Layer |
| 多层图片拼装 | 可降低组合素材数量，但涉及遮挡、锚点和组合渲染；作为另一能力设计 |

## 11. 风险和验证重点

1. 纯解析：完整命中、不同段数、从后向前累计删除、保护中间维度、多维初始覆盖、最终默认图；验证删维不变通配符、不移动位置。
2. 意图保留：`smile_skirt → weap → smile_skirt`；多个实例各自保留选择，同值重新选择的保护语义稳定。
3. 导入与发布：重复主名、多扩展名、大小写碰撞、空段、越界目录、缺图/坏 ID、默认图失效；本地定义与 PAK 定义结果一致。
4. 图层集成：切图保留变换与动画；replace 清除图集模式；hide、同 handle 再 show、视频/Flipbook 互斥；缺目标不产生幽灵实例。
5. 生命周期：异步加载失败与过期、快速切图、关闭、资源释放；CatchUp 与 Normal 最终状态一致。
6. 存档：在削减命中和默认命中后保存，读档后继续选择仍得到相同结果；内容不一致明确失败。
7. 编辑器：默认/选项/回退链真实挂载验证，撤销保存重开保持配置；场景静态分析识别新指令，不将其误判为无影响。

实施顺序见[阶段计划](phase-plan.md)：先锁定算法与资源契约，再推进运行时和编辑器。已执行的验证以分阶段实施记录为准。

## 12. P1 已实现接口与边界

Core 的 `GalNet.Core.Variants` 已提供：

- `ImageVariantsSource` / `ImageVariantDimensionSource`：带显式 JSON wire 名的作者 DTO，默认 version=1；dimensions 可省略，label 可为空值。目录边界检查仍归未来文件系统适配。
- `ImageVariantFile`：宿主提供的直接子文件事实（文件名、sprite ID、资源类型）；Core 不访问目录或解码图片。
- `ImageVariantsBuilder.Build`：收集带 code/path/message 的诊断；任何诊断都不发布部分定义。
- `ImageVariantsDefinition`：不可变维度/选项、变体及默认图，选项和变体按 ordinal 排序；无路径及文件系统依赖。
- `ImageVariantsResolver.Initialize/Select/Resolve`：初始化、单维切换和显式保护集合重算；非法请求抛出 `ArgumentException`，缺组合按第 5 节回退。
- `ImageVariantResolution`：独立冻结的完整请求、按维度顺序排列的保护集合、实际变体、Exact/Reduced/Default 原因、按删除顺序排列的维度及各次候选快照。最终 default 单独由原因和实际变体表示，不伪造成某个候选的精确命中。

源码位于 `src/Shared/GalNet.Core/Variants/`。P1 交付时未注册 `.variants` 或 `image-variants`，也未加入 Layer 状态及指令；运行时定义的发行序列化和加载由 P2 接续，不能将作者 DTO 直接用作发行协议。

P1 没有改变前文匹配规则。具体实现和验证见 [P1 记录](phase-1.md)。

## 13. P2 已实现接口与边界

- 确定资源类型为 `image-variants`，作者扩展名 `.variants`，metadata 为 `ImageVariantsAssetMeta`。
- `LocalAssetCompiler.CompileAsync` 接收调用方拥有的逻辑文件/物理备份路径与目录快照。LocalFileProvider 安全枚举磁盘，项目资源事务投影待发布快照；两者共用作者描述解析、目录解析与 P1 定义构建，不需要创建临时项目副本做预检。
- LocalFileProvider 在生成本地归档时，把图集源描述转换为同 ID、同类型、同逻辑路径的编译 IGameFile。图片仍是普通 sprite。编译后字节随该归档冻结，不将定义本身缓存为当前项目的可变全局状态；目录扫描按 watcher/Invalidate 失效并在编译后复核代次。
- `ImageVariantsCodec` 的发行 payload 为 kind=image-variants、version=1，含 dimensions、default 和 variants（key/assetId）；没有 folder。`ImageVariantsDecoder` 只解码该格式，不扫描文件系统。
- `RegisterImageVariantsDecoder()` 显式注册 decoder；默认 Avalonia 游戏宿主和项目级 EditorAssetManager 已接入，后者重建 manager 时重新注册。自定义 decoder 配置的宿主仍需自行注册。
- 项目范围 validate 与 build 使用本地编译器检查图集；GamePackageExporter 打包编译归档，包内校验沿用相同 catalog，不修改通用 PAK 格式。
- 资源事务对移动/复制改写已知 folder；导入生成 sidecar；发布前验证投影快照，破坏默认图等情况在写盘前拒绝，撤销/重做沿用同一文件事务。显式 metadata 的类型优先于扩展名。

这落实了第 7 节的作者准备层：准备发生在本地资源 provider 和事务预检，不放进 runtime decoder，也不把目录扫描引入 Core。当前没有为 P3 添加执行入口或 Layer 状态。稳定资源事实见[资源规范](../../docs/spec/content/assets.md)，验证见 [P2 记录](phase-2.md)。

## 14. P3 已实现接口与恢复

`variants.show/select` 加入现有 layers schema/执行模块，独立模块数不变；内置总数为 44（36 primitive、8 composite）。源参数由独立生成 DTO 校验，show 的 selection 是对象，transform 保持普通 Layer 的对象/嵌入 JSON 文本语义。

会话准备时，Assets 的 `LoadImageVariantsCatalogAsync` acquire 定义、验证所有 sprite 引用，完成后释放句柄并返回不可变 Core `ImageVariantsCatalog`。默认 Avalonia 呈现将其暴露给配置回调；Avalonia Sample、Headless Sample、Editor Preview 均将目录注入执行注册。AddBuiltins/AddBuiltinImplementations/AddBuiltinLayers 提供可选 imageVariants 参数；只使用普通图层时可不传。

Layer.Variants 保存图集 ID、完整请求、保护集合、实际变体键与解析原因；实际图片使用原 AssetId。select 原子修改同一 Layer 的图片与差分状态，不替换对象或停止循环动画。普通来源替换不携带 Variants，hide 删实例，重新 show 重置选择。

builtin.layers 节升至 v2，旧 v1 明确拒绝；GameSnapshot 外层仍为 v4。State 合约校验来源互斥和子状态结构；view.ValidateState 在 Engine 替换根之前，用已预载目录重算并核对实际结果。StateRestoreSession 在展示前再次验证，普通 Layer presenter 只接收实际 sprite ID。CatchUp 使用同一同步修改逻辑但不通知 presenter。

与初始设计的差异仅为异步工作的放置位置：没有指令内加载/队列，也不修改同步的 Engine 恢复预检 API。所有定义异步工作先于执行模块创建，取消/加载失败不产生场景实例；作用域间定义隔离。见 [P3 验证记录](phase-3.md)。

## 15. P5 示例素材适配

用户将真实立绘更名为 `xy_表情.png`；Sample 使用 pose / expression 两个维度，并复制 wink 为较短名称 `solo.png`，演示削减后保留表情。两张原有 classroom JPEG 使用 scene / mode / focus 三维命名，演示合法值缺组合时显式 Default 及后续精确命中。保留既有资源 ID、图片字节与 Gallery 引用；没有改写算法或引入别名机制。原始 smile/coat/weap 名称矩阵仍由纯解析和运行时测试覆盖。实际示例、验证范围和限制见 [P5](phase-5.md)。

## 2026-10-25 Inspector 简化修订

用户确认：维度数量、顺序及选项完全由文件决定，只保留显示名称编辑；稳定 key 不开放修改。此修订取代第 3 节手工维度数量与重排的设计。作者 dimensions 作为位置命名元数据，缺少的位置自动生成不冲突 key，多余的位置不活动但保留原文名称。扫描与编译统一执行此规则，自动扫描不改写配置。

默认图仅一个下拉框，目录用选择器；移除配置应用、维度增删排序、内嵌 JSON；有效配置自动进入项目历史，统一保存写盘。刷新只扫描与更新预览，文件变化通过既有资源监视通知被动触发。保留回退状态，完整候选链折叠显示。验证见 [本次记录](inspector-20261025.md)。
