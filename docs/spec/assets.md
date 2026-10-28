# 资源文件管理

每个项目资源位于 `Assets/`，由相邻的 `.meta` 描述稳定资源 ID（项目通常使用 GUID）、类型和类型专属数据。资源的作者引用、PAK 索引和运行时查询均以该 ID 为正式身份；路径只用于导入、编辑和诊断。

## Metadata 与类型

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "type": "sprite",
  "path": "characters/alice.png",
  "filter": "bilinear",
  "compress": "brotli",
  "gallery": [{ "id": 100, "typeId": "cg", "title": "Alice" }]
}
```

- `id` 是非空、全项目唯一的稳定资源 ID；推荐使用 GUID，但当前 codec 不限制其字符串形态。
- `type` 是资源类型 ID；组合根通过冻结的 `IResourceTypeCatalog` 将其映射到具体 `AssetMeta` DTO。内置类型为 `sprite`、`image-variants`、`audio`、`video`、`font`、`effect-program` 和 `data`。
- `path` 相对 `Assets/`；provider 会以实际逻辑路径校正缺失或过时的值。
- 可选字段缺失时使用 DTO 默认值；未知字段保存在 extension data，供 Editor 重写时保留。未知 `type`、无效 JSON、缺失 GUID 或缺失源文件是加载错误，不能静默跳过。
- `gallery` 是 Gallery 的唯一作者真源。每项使用全局唯一正整数 ID；没有 `sortOrder` 或项目根可编辑 `gallery.json`。


通用 `AssetMeta` 与类型目录协议位于 Core.Assets；`BuiltinResourceTypes`、sprite metadata 及默认 Gallery/ImageVariants 契约位于 Contracts.Builtins。没有专属字段的默认资源共用 AssetMeta，身份由 type ID 决定。Gallery 用可选 ReadGallery/SetGallery 扩展读取、重写 extension data，未知 annotation 字段也保留。

资源注册可显式传入 JSON resolver，`AssetMetaCodec` 按注册类型组合源生成元数据并缓存 options；默认实例由注册时的泛型工厂创建，`Add/Replace<TMeta>` 要求公开无参构造函数。内置目录已提供元数据；反射关闭的玩家扩展须注册自己的 context，见 [NativeAOT 手册](../manual/native-aot.md)。

组合根可通过 `ResourceTypeCatalogBuilder.DefaultType(id)` 为未知扩展选择默认类型；没有显式 type、扩展映射或该默认值时才报无法解析类型。默认工厂明确选择 data，低层 codec 不假定 data 必须存在。LocalFileProvider、PakFileProvider、Archive 和 PakBuilder 必须接收调用方目录；例如 `PakBuilder.Build("assets", files, types)`，压缩模式为后续可选参数。

资源类型只负责识别、保存、枚举和打包；它不注册 decoder、Editor inspector 或 Avalonia 页面。decoder 以 `(typeId, target CLR type)` 另行注册。

Editor 将原始资源与 sidecar 作为一次项目文件编辑：移动保留 ID 并重定位 path，导入/复制生成新资源 ID，保留 Gallery 内容与 metadata 扩展；Gallery ID 与项目/同批内容冲突时为副本重新分配正整数 ID，type/title 保持原值；无法无损类型化的 metadata 拒绝重写。资源索引刷新只读，不负责补建 sidecar。GUI 操作支持项目历史撤销，CLI 共用事务而不保留 GUI 历史，见 [Editor 项目文件操作](editor-files.md)。

## 查询、解码与生命周期

```text
IAssetProvider / IArchive / IGameFile
  -> AssetManager（GUID 查询、decoder、缓存）
  -> AssetHandle<T>（一次成功 Acquire 的独立引用）
```

`IGameFile` 是可重复读取的源描述，不是资源句柄。`IAssetManager.AcquireAsync<T>(guid)` 以 `(GUID, T)` 缓存已解码对象，并返回独立 `AssetHandle<T>`；每个 handle 的幂等 `Dispose` 只释放自己的一次引用。引用计数归零时 manager 删除缓存，并释放缓存对象（若其实现 `IDisposable`）。

`GetFileAsync`、`GetFilesAsync` 及按路径查找只用于定位/枚举；正式剧情和运行时引用优先使用 GUID。Provider 可缓存目录或 PAK 的位置索引，但不缓存已解码对象，也不管理引用计数。

需要系统文件路径的媒体后端不能自行扫描项目或 PAK。它先从 manager acquire bytes，再在受控的会话临时目录实体化；该目录随使用它的 resolver 释放。图层贴图等可直接解码的资源保留 `AssetHandle<T>`。

## 来源与发布边界

目录导出与 `.galpak` 导出共用一份导出准备流程：在私有暂存内容上加载资源、编译图集与 Gallery、生成 PAK 和清单，随后由各自写出方式发布。资源编译结果在暂存目录移动前转为自有字节，不缓存指向旧目录的文件对象。普通 build 同样在暂存内容上检查图集，资源准备失败不会替换旧输出；最终产物的哈希及归档校验仍在发布前执行。

| 输入 | 内容 provider | 资源 provider |
| --- | --- | --- |
| 开发编译目录 | `ProjectGameContentProvider` 经资源 ID 加载剧情，读取 settings/I18n，并从 Assets metadata 编译 Gallery | `CompiledStoryAssetProvider(Graph/)` + `LocalFileProvider(Assets/)` |
| 已安装包目录 | `InstalledGameContentProvider` 读取 `content.json` 唯一 Graph 入口并按资源 ID 加载剧情，读取 settings/I18n 和生成的 gallery | 所有 `Assets/Paks/**/*.pak` |

同一宿主在创建内容 provider、资源 provider、Preview 与导出器时必须传入同一对冻结 `IResourceTypeCatalog` / `IGalleryTypeCatalog`。不能在下游入口重新创建 built-in catalog，否则插件注册无法贯穿完整路径。

安装内容的 PAK 当前按相对路径倒序注册；更靠后的路径优先，因此 `100-extra.pak` 可覆盖 `000-base.pak`。`I18n/`、`settings.json`、生成的 `gallery.json`、`content.json` 和 manifest 是特殊内容。发行 Graph/Group 已作为资源进入 PAK，不从 loose Graph 目录回退读取。

项目仍只有一张 Graph，清单指向保留 ID `galnet:graph:main`；Group 正文 ID 为 `galnet:group:<稳定节点ID>`，与节点身份分开。两种资源使用 `DependentAssetMeta`。这些 metadata 由编译适配层生成，不需要在作者目录补 sidecar 或新增选图配置；自定义发行资源目录也须注册 graph/group 的 DependentAssetMeta。`CompiledStoryResources` 为发布和开发适配生成同一剧情 payload，开发 provider 按会话持有不可变快照。开发输入需已有编译 Group，缺少正文不再静默当空 Group。

Group 依赖从编译后 primitive 参数的字段契约收集。`FieldAttribute.ResourceTypeId` / `FieldDescriptor.ResourceTypeId` 明确声明字符串字段为资源 ID；源生成器与结构遍历保留该声明，不从 EditorId、GUID 外形或任意字符串推断。opaque 结构或 custom.invoke 无法证明静态引用完整时，metadata 标记为不完整。开发 provider 没有编译 schema 上下文时不伪造非空 Group 的完整依赖，发行编译使用调用方 catalog 提供分析结果。

## PAK

`.pak` 当前版本为 3，使用 `GPAK` magic。magic/version 后写入长度前缀的 UTF-8 控制 JSON，再写条目数量、资源表及连续数据块。每个表项保存 UTF-8 GUID、逻辑路径、字符串 type ID、注册 DTO 序列化的 metadata JSON、数据偏移、原始/存储长度、压缩模式与 64 字符小写十六进制 SHA-256。读取时 metadata 的 GUID、路径和 type ID 必须与表项一致，重复 ID/逻辑路径、非法范围和尾随数据拒绝；读取资源时验证实际载荷长度与 SHA-256。

控制描述 `PakDescriptor` 的 `kind` 为 `base`、`story` 或 `override`，与资源 `typeId` 独立。共用 PAK 格式，未知 kind/控制字段、重复控制属性和不支持的版本拒绝。story 描述需要项目、基础构建和包身份及合法分片序号；override 需要包身份、单分片、整数 priority 和资源映射表，映射目标必须在本包内。base/story 不能携带 priority 或覆盖映射；`Archive.Descriptor` 返回隔离的映射快照。旧调用省略 descriptor 时生成匿名 base 包，游戏级构建身份由上层提供。

上述是归档格式与校验协议；剧情包安装/跳过和资源映射应用仍需由宿主的内容装载层接入，不能仅把任意新 kind PAK 放入旧扫描目录就认为其用途语义已生效。

数据块可使用 `none`、`deflate`、`gzip` 或 `brotli`；`PakBuilder` 默认选择 Brotli，并会跳过过小数据的压缩。写出按资源 ID 稳定排序，重新计算实际 payload 的 hash，不沿用可能过时的 IGameFile.Hash；控制映射按原 ID 排序，无归档名/时间戳写入。旧 enum PAK 和 v2 格式不受支持，需重新导出。

## 资源依赖 metadata

有依赖的 metadata DTO 可实现 Core 的 `IAssetDependencies`，返回直接引用列表及 `Complete`。`DependentAssetMeta` 提供可序列化的 `Dependencies` / `DependenciesComplete`；payload 编译器在同一快照生成依赖，不自动回写作者 sidecar。叶类型通过 `ResourceTypeCatalogBuilder.WithoutDependencies(...)` 明确声明；既没有接口也未声明叶类型时，按依赖分析不完整处理。内置 sprite、audio、video、font、effect-program 声明为叶；data 未默认视为无依赖。

`AssetDependencyCollector` 按规范化资源 ID 收集闭包，检查缺失与预期类型、处理循环、支持取消，返回稳定排序的资源与不完整项，不负责切包或运行时 acquire。构建入口可给出已经由人工依赖声明覆盖的不完整资源 ID；该声明只确认分析完整性，不自动增加资源根。只有 graph 的直接引用可以携带 `RequiredStoryPackage`，仅在收集对应剧情包时跨入；普通必需资源缺失不能静默忽略。

图集有效 metadata 使用 `DependentAssetMeta`，包含编译定义中全部 sprite ID，引用预期类型为 sprite。PAK 写出和打开时检查依赖与图集 payload 一致；本地目录扫描不为此修改源 `.meta`。缺少这些依赖 metadata 的旧编译图集必须重新构建。

## 内容分包规划

`ContentPackagePlanner` 先收集唯一 Graph、全部非补丁 Group 和额外根的闭包 B；每个剧情包独立取 C(P)−B，不扣除其他剧情包。基础 Gallery 项和默认视觉程序绑定也作为根。未知依赖必须由 `galnet.packaging` 的 includeResources 补根，并用 declaredCompleteResources 显式确认覆盖；后者可写资源 ID 或 Group 节点 ID，不会自动补根。

每个集合按资源 ID ordinal 排序，以未压缩编译载荷的累计 Length 切片。maxPakBytes 缺省 64 MiB，是软目标，超限单项独占，不拆资源；PAK 头部、压缩和索引开销不计入阈值。分片逐个写到私有磁盘阶段，不同时保留全部 PAK 字节。当前 Archive 仍以内存打开单个 PAK，64 MiB 并非内存上限。

只有闭包中的资源进入 PAK。图集包含全部声明图片、条件分支包含所有静态引用；UI/自定义运行代码的资源需显式补根。源资源校验仍覆盖整个目录，未被打包的坏作者资源也可能阻止构建。安装校验本体和每个剧情包的独立依赖闭包，重复 ID 的内容和 metadata 必须一致。

## 独立资源覆盖

`ResourceOverrideBuilder.Build(projectId, definition, available, types)` 验证作者 A/B 均存在且类型相同，生成单个 override PAK；映射和资源稳定排序，同内容重复导出 hash 稳定。`ResourceOverrideExporter.ExportAsync` 从 Assets 私有快照独立导出，不编译剧情、不要求 galpak、不写源 sidecar，失败/取消不替换旧目标。

`ResourceOverlayProvider` 是冻结的统一逻辑资源视图。A→B 直接绑定包内 B，一次请求只查映射一次，A→B/B→C 不把 A 串成 C。有效文件保留原 ID/路径，metadata 和字节来自替换资源；ID、路径、解码和媒体实体化因此一致。图集替换保留原维度键和全部原变体键，可增加变体；递归图片由覆盖包提供，后续图片请求可再独立命中显式映射。

夹带资源不会隐式覆盖本体同 ID。递归依赖若与本体/其他有效来源同 ID 却内容或 metadata 不一致，准备期拒绝，作者须为改动资源分配新 ID，再显式映射。直接替换目标 B 的来源被明确绑定，对原 B 的独立请求仍按自身映射或本体解析。有效视图和 manager 缓存限定于该安装会话；源文件定位继续使用作者目录。

## 图片差分资源

`image-variants` 的作者扩展名为 `.variants`，metadata DTO 为 `ImageVariantsAssetMeta`。它管理一组完整 sprite 图片，不表示纹理合并、Flipbook 或 CG Gallery。游戏显示/切换使用 [variants 指令](entry-types.md#图片差分指令)；编辑器支持从目录创建、配置和差分预览，操作见[图集手册](../manual/image-variants.md)。

```json
{
  "version": 1,
  "folder": "./alice",
  "default": "idle_coat",
  "dimensions": [
    { "key": "expression", "label": "表情" },
    { "key": "outfit", "label": "服装" }
  ]
}
```

folder 相对描述文件，允许 `.` 和不越过 Assets 的 `..`，兼容两种目录分隔符。仅收集指定目录直接包含的 sprite，不递归其子目录；空目录或缺省默认图无效。图片文件主名按 `_` 划分有序维度，空段、主名跨扩展名重复及仅大小写不同的变体键均无效。实际维度数量始终等于图片主名的最大段数。dimensions 仅命名对应位置，可省略；已有 key/label 保留，未命名的新位置生成 d1/d2 等 key，冲突时追加下划线，超出实际数量的命名保留于作者文件但不生成活动维度；选项及变体按 ordinal 稳定排序，维度和值本身采用精确比较，不做大小写或空格归一化。

`LocalFileProvider` 先读取资源文件和 sidecar 的同一目录索引，再通过 `LocalAssetCompiler` / Core 的纯定义构建器编译所有图集；不会补建 metadata。目标目录内的 sprite 必须已有正常资源 ID 和正确类型，缺图、缺 sidecar、坏 default、坏描述等拒绝该归档，而不是略过错误变体。目录树和祖先路径不允许经过链接/重解析点。扫描刷新、取消和销毁具有代次检查；`Invalidate()` 可在已知作者修改后显式使目录索引失效。已经返回的编译字节属于该次快照；持有中的解码句柄仍遵循一般 AssetManager 缓存语义，不承诺原地热更新。

编译后的 IGameFile 保留图集自身 ID、逻辑路径、类型和 metadata，payload 为 `kind="image-variants"`、version=1 的定义，包含维度 key/label、default 和各变体 key/sprite ID，不含 folder。解码后重新校验并建立不可变 `ImageVariantsDefinition`，选项从变体事实推导。`ImageVariantsCodec` 严格拒绝未知/重复 JSON 属性；作者描述不能当作发行 payload 解码。

`PakBuilder` 直接打包本地归档的编译结果及正常 sprite，PAK 格式为 3。导出和包内验证沿用调用方的同一 resource catalog；从 PAK 读取定义无需原始图片文件夹。`project.validate`（项目范围，包括 structure-only）与 build 校验该资源，业务错误报告 `variants.invalid`；原始目录索引刷新不写作者文件或 dirty 状态。

解码注册仍与类型注册分离。使用 Assets 中的 `RegisterImageVariantsDecoder()` 扩展方法注册 `(image-variants, ImageVariantsDefinition)`，默认 Avalonia 游戏宿主与项目级 EditorAssetManager 已接入；自定义宿主覆写 decoder 配置时需自行注册。编辑器资源刷新重建内部 manager 时重新注册该 decoder。

执行会话准备使用 `LoadImageVariantsCatalogAsync()` 一次异步加载图集定义，并检查全部变体引用都存在且为 sprite。返回的 Core `ImageVariantsCatalog` 只持有不可变定义，不持有资源句柄；加载失败/取消不会返回半份目录。资源 ID 使用与 AssetManager 相同的首尾空格处理和大小写无关查询，变体键与维度值仍精确比较。目录按会话注入内置 Layer 模块；播放中不扫描文件夹或异步加载定义，重新准备会话才采用新内容。默认 Avalonia 游戏呈现、Sample Headless 和 Editor Preview 已在注册执行模块前完成该准备。

项目资源文件事务在 Assets 改动发布前，使用投影后的文件/目录快照调用同一编译器预检。移动描述文件或图片目录会调整已知的相对 folder；复制完整目录时生成独立资源 ID 并将内部目录关系指向副本；这些改动与原操作一起撤销/重做。显式 metadata 的图集类型不因改扩展名而丢失。改名或删除若令 default 等定义失效，会在写盘前拒绝，不能靠文件路径变化静默迁移维度语义。显式导入包含图集的完整目录会在同一事务中为资源创建 sidecar。

## 后端媒体文件租约

`PakStreamProvider` 使用宿主显式提供的归档名 → 新流工厂清单打开 PAK，适用于应用包、嵌入资源等没有普通文件路径的来源。`Exists` 仅查询清单；每次打开独立拥有并释放流，读取不要求 Length/Position/Seek。同步与异步入口均在返回前读完 PAK，保持当前 Archive 的内存加载语义；异步复制支持取消。工厂应返回位于 PAK 起始位置的新可读流，不得共享已借用流。归档名忽略大小写并接受 `.pak` 后缀别名，重复归一化名称会拒绝。此适配不接管 Graph/settings/I18n 的安装内容加载。

`IAssetFileMaterializer` / `AssetFileLease` 位于 Core.Assets；Assets 的 `AssetFileMaterializer` 通过 AssetManager acquire 的字节实体化媒体。同一会话内资源 ID（忽略大小写）共享文件，最后一个租约释放才删除。关闭 materializer 取消待加载请求并拒绝新请求，已借用文件仍有效直到释放。Sample 将同一实例传给剧情音频、视频与 Gallery；视频先释放原生播放器再释放文件租约。Gallery 缩略图抽取和临时缓存由独立服务拥有。

`AssetFileMaterializer(assets, cacheDirectory)` 允许宿主传入应用可写缓存根；缺省使用 .NET 临时目录。每个实例只创建并清理该根下 `GalNet/Media/<唯一目录>`，不会删除宿主提供的根或其他缓存。实体化结果是后端可打开的普通文件路径；包内 URI 不直接交给文件型媒体解码器。

LibVLC 音频后端同样为每个 voice 持有独立文件租约，先销毁播放器再释放租约；自然结束、立即停止、淡出完成、创建失败与取消加载均归还租约。后端不再将播放过的文件保留到整个会话结束，也不另建音频引用计数或闲置缓存层。多个声音及视频/Gallery 仍通过同一个 materializer 共享文件，音频停止不会删除其他消费者仍在使用的文件。

## 默认视觉资源

默认契约项目初始化将 Editor.Shared 内嵌模板复制为 `Assets/Effects/Default/` 下的 8 个普通 effect-program 资源及 sidecar，每个项目生成自身 GUID。`galnet.project.defaultVisualPrograms` 保存创作用途绑定，不是运行时隐藏效果目录。`project visuals install` 通过现有作者文件事务显式补入缺失绑定及资源，保留已有绑定，拒绝覆盖被占用路径。自定义契约需声明相应配置字段及 effect.apply 能力才启用默认模板。

Shader 的可选保留 `layerZ` uniform 在 CPU/GPU 图层效果链每次绘制时取当前图层 Z；不可声明为作者参数。texture 参数由效果运行器通过 AssetManager 获得独立租约，按画布大小绑定，停止、替换、释放或准备失败时归还。预览和运行只读取项目资源；导出包含项目 shader 和纹理。使用方式及景深、停止边界见[默认视觉库](../manual/default-visuals.md)。
