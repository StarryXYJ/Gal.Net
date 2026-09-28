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
- `type` 是资源类型 ID；组合根通过冻结的 `IResourceTypeCatalog` 将其映射到具体 `AssetMeta` DTO。内置类型为 `sprite`、`audio`、`video`、`font`、`effect-program` 和 `data`。
- `path` 相对 `Assets/`；provider 会以实际逻辑路径校正缺失或过时的值。
- 可选字段缺失时使用 DTO 默认值；未知字段保存在 extension data，供 Editor 重写时保留。未知 `type`、无效 JSON、缺失 GUID 或缺失源文件是加载错误，不能静默跳过。
- `gallery` 是 Gallery 的唯一作者真源。每项使用全局唯一正整数 ID；没有 `sortOrder` 或项目根可编辑 `gallery.json`。

资源类型只负责识别、保存、枚举和打包；它不注册 decoder、Editor inspector 或 Avalonia 页面。decoder 以 `(typeId, target CLR type)` 另行注册。

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
