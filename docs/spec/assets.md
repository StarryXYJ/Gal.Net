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

| 输入 | 内容 provider | 资源 provider |
| --- | --- | --- |
| 开发项目目录 | `ProjectGameContentProvider` 读取 `Graph/`、`settings.json`、`I18n/`，并从 `Assets/**/*.meta` 编译 Gallery | `LocalFileProvider(Assets/)` |
| 已安装包目录 | `InstalledGameContentProvider` 读取 `Graph/`、`settings.json`、`I18n/` 和生成的 `gallery.json` | 所有 `Assets/Paks/**/*.pak` |

同一宿主在创建内容 provider、资源 provider、Preview 与导出器时必须传入同一对冻结 `IResourceTypeCatalog` / `IGalleryTypeCatalog`。不能在下游入口重新创建 built-in catalog，否则插件注册无法贯穿完整路径。

安装内容的 PAK 按相对路径倒序注册；更靠后的路径优先，因此 `100-extra.pak` 可覆盖 `000-base.pak`。`Graph/`、`I18n/`、`settings.json`、生成的 `gallery.json` 和 manifest 是特殊内容，永不注册为资源 provider。

## PAK

`.pak` 当前版本为 2，使用 `GPAK` magic。每个表项保存 UTF-8 GUID、逻辑路径、字符串 type ID、注册 DTO 序列化的 metadata JSON、数据偏移、原始/存储长度、压缩模式与 SHA-256。读取时 metadata 的 GUID、路径和 type ID 必须与表项一致，否则包无效。

数据块可使用 `none`、`deflate`、`gzip` 或 `brotli`；`PakBuilder` 默认选择 Brotli，并会跳过过小数据的压缩。旧 enum PAK 格式不受支持。

## 图片差分资源

`image-variants` 的作者扩展名为 `.variants`，metadata DTO 为 `ImageVariantsAssetMeta`。它管理一组完整 sprite 图片，不表示纹理合并、Flipbook 或 CG Gallery；当前资源编译能力不包含图集游戏指令或专属编辑 UI。

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

folder 相对描述文件，允许 `.` 和不越过 Assets 的 `..`，兼容两种目录分隔符。仅收集指定目录直接包含的 sprite，不递归其子目录；空目录或缺省默认图无效。图片文件主名按 `_` 划分有序维度，空段、主名跨扩展名重复及仅大小写不同的变体键均无效。dimensions 可省略以生成 d1/d2 等位置 key；选项及变体按 ordinal 稳定排序，维度和值本身采用精确比较，不做大小写或空格归一化。

`LocalFileProvider` 先读取资源文件和 sidecar 的同一目录索引，再通过 `LocalAssetCompiler` / Core 的纯定义构建器编译所有图集；不会补建 metadata。目标目录内的 sprite 必须已有正常资源 ID 和正确类型，缺图、缺 sidecar、坏 default、坏描述等拒绝该归档，而不是略过错误变体。目录树和祖先路径不允许经过链接/重解析点。扫描刷新、取消和销毁具有代次检查；`Invalidate()` 可在已知作者修改后显式使目录索引失效。已经返回的编译字节属于该次快照；持有中的解码句柄仍遵循一般 AssetManager 缓存语义，不承诺原地热更新。

编译后的 IGameFile 保留图集自身 ID、逻辑路径、类型和 metadata，payload 为 `kind="image-variants"`、version=1 的定义，包含维度 key/label、default 和各变体 key/sprite ID，不含 folder。解码后重新校验并建立不可变 `ImageVariantsDefinition`，选项从变体事实推导。`ImageVariantsCodec` 严格拒绝未知/重复 JSON 属性；作者描述不能当作发行 payload 解码。

`PakBuilder` 直接打包本地归档的编译结果及正常 sprite，PAK 格式仍为 2。导出和包内验证沿用调用方的同一 resource catalog；从 PAK 读取定义无需原始图片文件夹。`project.validate`（项目范围，包括 structure-only）与 build 校验该资源，业务错误报告 `variants.invalid`；原始目录索引刷新不写作者文件或 dirty 状态。

解码注册仍与类型注册分离。使用 Assets 中的 `RegisterImageVariantsDecoder()` 扩展方法注册 `(image-variants, ImageVariantsDefinition)`，默认 Avalonia 游戏宿主与项目级 EditorAssetManager 已接入；自定义宿主覆写 decoder 配置时需自行注册。编辑器资源刷新重建内部 manager 时重新注册该 decoder。

项目资源文件事务在 Assets 改动发布前，使用投影后的文件/目录快照调用同一编译器预检。移动描述文件或图片目录会调整已知的相对 folder；复制完整目录时生成独立资源 ID 并将内部目录关系指向副本；这些改动与原操作一起撤销/重做。显式 metadata 的图集类型不因改扩展名而丢失。改名或删除若令 default 等定义失效，会在写盘前拒绝，不能靠文件路径变化静默迁移维度语义。显式导入包含图集的完整目录会在同一事务中为资源创建 sidecar。

## 后端媒体文件租约

`IAssetFileMaterializer` / `AssetFileLease` 位于 Core.Assets；Assets 的 `AssetFileMaterializer` 通过 AssetManager acquire 的字节实体化媒体。同一会话内资源 ID（忽略大小写）共享文件，最后一个租约释放才删除。关闭 materializer 取消待加载请求并拒绝新请求，已借用文件仍有效直到释放。Sample 将同一实例传给剧情音频、视频与 Gallery；视频先释放原生播放器再释放文件租约。Gallery 缩略图抽取和临时缓存由独立服务拥有。
