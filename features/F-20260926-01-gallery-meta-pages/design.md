---
feature: F-20260926-01-gallery-meta-pages
status: proposed
created: 2026-09-26
updated: 2026-09-26
---

# Gallery 元数据聚合与可扩展页面注册设计

## 1. 设计结论

本 feature 建立四个彼此独立、在组合期冻结的注册面：

1. **资源类型 registry**：注册资源 type ID、对应 `.meta` DTO 类型和文件扩展名。
2. **Gallery 类型 registry**：注册 Gallery type ID 及其允许的资源 type ID。
3. **Gallery catalog compiler**：从注册表和资源 `.meta` 聚合运行时 Gallery 数据。
4. **Avalonia Gallery 页面 registry**：把精确 Gallery type ID 映射到独立页面 ViewModel；现有页面 registry 继续负责 ViewModel 到 View。

资源 `.meta` 是 Gallery authoring 的唯一真源。目录运行启动、Preview 或导出时聚合 `.meta`；运行会话只使用冻结 catalog，发行包只使用生成的 `gallery.json`，不在游戏过程中反复扫描 metadata。

`GalleryRendererKind`、`GalleryRendererResolver` 和包含所有媒体分支的 `GalleryContentPageViewModel` 被移除。CG、视频和音频各自拥有页面；其他 Gallery type 必须显式注册页面，允许多个 type ID 显式复用同一页面。

当前是快速迭代阶段，本设计直接替换旧 API 和文件结构，不提供旧 `.meta`、`gallery.json`、`.pak` 或旧项目迁移兼容。

## 2. 目标与边界

### 2.1 目标

- 插件能增加新的资源类型，而无需修改 `ResourceType` enum 或多个扩展名 switch。
- 每种资源类型能声明自己的强类型 `.meta` DTO。
- 插件能增加 Gallery 类型及对应页面，而无需修改内置 renderer enum。
- Gallery item 用易于手工配置的正整数 ID，同时承担解锁变量身份和页面排序。
- Core/Runtime 保持平台无关，Avalonia 页面与媒体播放器只存在于前端层。
- 首轮优先让目录运行和默认游戏前端跑通，Editor Gallery UI、Undo/Redo 和旧数据迁移后移。

### 2.2 非目标

- 不支持运行时热添加、移除或替换注册项；所有 registry 在宿主组合完成后冻结。
- 不在一个“资源模块”对象中同时塞入 Core DTO、decoder、Editor inspector 和 Avalonia View。
- 不实现 Editor 的 Gallery 标注 UI、metadata Undo/Redo 或旧 `gallery.json` 迁移。
- 不兼容旧发布包或旧 authoring 文件；测试和示例数据直接更新到新格式。
- 不实现 galgroup 场景回放 Gallery。

## 3. 分层

```text
GalNet.Core
├─ AssetMeta base DTO + GalleryAnnotation
├─ ResourceTypeRegistration / ResourceTypeCatalog
├─ GalleryTypeRegistration / GalleryTypeCatalog
├─ GalleryCatalogCompiler
└─ GalleryCatalog / GalleryDataSource

GalNet.Storage.Abstractions
├─ IGameFile.TypeId
├─ IGameFile.Metadata
└─ IAssetManager string type filtering + typed decoder registration

GalNet.Assets / Storage.FileSystem
├─ registered Meta DTO deserialization
├─ LocalFileProvider
├─ Pak v2 metadata payload
└─ directory/generated Gallery loading

GalNet.Avalonia.GameView
├─ GalleryNavigationService
├─ GalleryPageRegistry
├─ GalleryIndexPage
└─ CG / video / audio independent pages
```

Core registry 可以保存 `System.Type` 作为组合期 DTO 映射，但不会把 CLR 类型名写入 `.meta`、`gallery.json` 或 `.pak`。文件只保存稳定 type ID，宿主代码决定该 ID 对应哪个 DTO。

## 4. 资源类型动态注册

### 4.1 注册模型

完整注册包含：

```text
ResourceTypeRegistration
├─ TypeId
├─ MetaDtoType
└─ Extensions[]
```

推荐 Builder API：

```csharp
services.AddResourceTypes(types =>
{
    types.Add<SpriteAssetMeta>("sprite", ".png", ".jpg", ".jpeg", ".webp");
    types.Add<AudioAssetMeta>("audio", ".mp3", ".wav", ".ogg", ".flac");
    types.Add<VideoAssetMeta>("video", ".mp4", ".webm", ".mkv");
    types.Add<EffectProgramAssetMeta>("effect-program", ".sksl");

    // 只给已经完整注册的 sprite 增加后缀，不重复声明 DTO。
    types.AddExtensions("sprite", ".bmp", ".gif", ".avif");
});
```

语义：

- `Add<TMeta>(typeId, extensions...)` 是完整注册。同一规范 type ID 只能完整注册一次。
- `AddExtensions(typeId, extensions...)` 可以出现多次，并在 Build 时合并到目标类型；调用顺序不要求目标已经先注册，但冻结时目标必须存在。
- 同一扩展名只能归属于一个资源 type ID；冲突在冻结时失败。
- type ID 规范化为小写，只允许字母、数字、`_`、`-`、`.`。
- 扩展名规范化为带前导点的小写形式。
- `TMeta` 必须继承公共 `AssetMeta`、非抽象，并能由配置的 JSON serializer 创建。
- 宿主要替换内置注册时使用显式 `Replace<TMeta>`；普通重复 `Add` 不依赖加载顺序覆盖。

### 4.2 Meta DTO

公共基类只放所有资源都需要的字段：

```text
AssetMeta
├─ Id
├─ TypeId
├─ Path
├─ Compress?
└─ Gallery[]
```

内置类型通过 DTO 增加专属字段，例如：

```text
SpriteAssetMeta : AssetMeta
└─ Filter
```

`Compress` 属于归档存储策略，对所有资源类型都有意义，因此保留在公共基类；`Filter` 等解释/渲染专属字段才进入具体 DTO。插件 DTO 可以继续增加自己的 JSON 字段。

读取 `.meta` 时采用两段式过程：

1. 只读取最小 discriminator `type`；
2. 从 `IResourceTypeCatalog` 查到 `MetaDtoType`；
3. 使用该具体类型反序列化完整 JSON；
4. 校验 DTO 的 type ID 与 registry key 一致，并执行公共字段校验。

未知 type ID、DTO 类型不合法或反序列化失败都直接报错。JSON 中不保存程序集限定名或 CLR 类型名。

示例：

```json
{
  "id": "asset-guid",
  "type": "sprite",
  "path": "cg/opening.png",
  "filter": "bilinear",
  "compress": "none",
  "gallery": [
    { "id": 100, "typeId": "cg", "title": "Opening" },
    { "id": 900, "typeId": "wallpaper", "title": "Opening Wallpaper" }
  ]
}
```

### 4.3 运行时资源契约

- 删除 `ResourceType` enum。
- `IGameFile.Type` 改为规范字符串 `TypeId`。
- `IGameFile` 增加类型化后的公共 `AssetMeta Metadata`，具体插件可检查其已注册 DTO 类型。
- `IAssetManager.GetFilesAsync` 使用可选字符串 type ID 过滤。
- AssetManager decoder 注册键改为 `(resourceTypeId, target CLR type)`：

```csharp
assetManager.RegisterDecoder<SceneTexture>("sprite", spriteDecoder);
assetManager.RegisterDecoder<SceneTexture>("svg", svgDecoder);
```

这样多个资源类型可以解码成同一目标 CLR 类型而不互相覆盖。缓存仍按 `(assetId, target CLR type)` 建键，因为一个 asset 在冻结 catalog 内只有一个规范资源类型。

Decoder、Editor inspector、缩略图 provider 和 Avalonia/Skia 类型仍分别注册，不进入 `ResourceTypeRegistration`。注册资源类型意味着“可识别、可保存、可枚举和可打包”，不自动承诺所有宿主都拥有 decoder 或预览 UI。

### 4.4 Pak 格式

新的 `.pak` entry 保存：

```text
Id
Path
TypeId (length-prefixed UTF-8)
MetadataJson (length-prefixed UTF-8, canonical registered DTO JSON)
Offset / lengths / compression / hash
```

Pak reader 使用 `TypeId` 查 registry，再把 `MetadataJson` 反序列化成注册 DTO。表中的 Id/Path/TypeId 与 metadata 中对应字段必须一致，否则包无效。类型专属 metadata 因此能进入 decoder，而不是只在开发目录中存在。

格式直接升级并只读写新版本；不保留旧 enum pak reader。

## 5. Gallery 类型注册

Gallery 类型只由代码、宿主或插件注册：

```csharp
services.AddGalleryTypes(types =>
{
    types.Add("cg", "sprite");
    types.Add("video", "video");
    types.Add("audio", "audio");
});
```

每项只有：

```text
GalleryTypeRegistration
├─ TypeId
└─ ResourceTypeId
```

冻结时验证 type ID 唯一且引用已注册的资源类型。Editor 不创建项目私有 Gallery 类型，也不保存 Gallery 类型定义；生成的 `gallery.json` 可以携带 registry 快照供包内容自描述，但运行时仍要与当前代码 registry 校验一致。

## 6. Gallery annotation 与数字 ID

### 6.1 数据模型

```text
GalleryAnnotation
├─ Id          positive Int32
├─ TypeId
└─ Title?
```

规则：

- ID 必须大于 0，并在整个游戏内容中唯一，不只是某个 Gallery 类型内唯一。
- 同一资源可以加入多个 Gallery 类型，也可以在同一类型中出现多次；每个 annotation 使用不同数字 ID。
- `resourceId` 不重复保存在 annotation 中，聚合时使用父级 `AssetMeta.Id`。
- 页面在同一 Gallery 类型内按数字 ID 升序展示；数字越大越靠后。
- 不保存 `sortOrder`。ID 就是导出后静态内容的最终顺序，同时也是稳定解锁身份。
- `title` 可省略；分组、封面帧、标签和本地化后移。

### 6.2 解锁变量

数字 ID 是系统变量名的中间部分：

```text
annotation id: 100
variable name: gallery_100_unlocked
runtime name:  player.gallery_100_unlocked
```

`gallery.unlock` 参数改为正整数 ID。Gallery catalog、primitive、Editor 条件提示和 UI 都调用同一个 Core helper 生成变量名；`.meta` 和 `gallery.json` 不保存完整变量名或当前解锁值。

全局唯一约束避免变量名加入 type ID，也使 item 在不同 Gallery 类型间移动时保持解锁状态。数字由作者配置；由于 Editor authoring 延后，本轮由测试、Sample 或手工 `.meta` 保证分配。

## 7. Gallery 聚合与生成物

平台无关 compiler 输入：

```text
GalleryCatalogCompiler
  ResourceTypeCatalog
  GalleryTypeCatalog
  IEnumerable<AssetMeta>
```

校验：

- asset ID 全局唯一；
- Gallery annotation ID 为正整数且全局唯一；
- annotation type ID 已注册；
- 资源 DTO 的 type ID 与 Gallery type 要求的 resource type ID 一致；
- resource registry 中登记的 DTO 类型与实际反序列化对象一致。

输出：

- `GalleryConfiguration`：注册类型快照和全部 item；item 包含数字 ID、type ID、父资源 ID和标题。
- 冻结 `GalleryCatalog`：按 type ID 索引，并在每个类型内按数字 ID 升序。
- 系统 Player bool 定义：每个数字 ID 对应一个 `gallery_<id>_unlocked`。

相同输入必须得到确定性输出：类型按 type ID，item 按 type ID 后数字 ID 排序；绝对路径、文件枚举顺序和时间戳不进入生成 JSON。

### 7.1 宿主路径

- **目录开发运行**：启动时扫描 `.meta` 一次，完成类型化反序列化和 Gallery 聚合，直接把冻结 catalog 交给 `GameContent`。首轮不写持久缓存。
- **Editor Preview**：在 Editor 基础设施完成必要适配后使用同一个 compiler；本轮不实现 Gallery inspector。
- **导出**：从 `.meta` 编译后把生成 `gallery.json` 写入 `content.pak`，把带 typed metadata 的资源写入 `assets.pak`。
- **发行运行**：读取包内生成 JSON 和 typed asset metadata，不扫描 sidecar。

包内 Gallery 类型快照不是注册来源。加载时逐项与冻结 `IGalleryTypeCatalog` 比对；缺少 Gallery/资源类型插件或映射改变时内容加载失败。

## 8. Avalonia Gallery 页面注册

### 8.1 两层页面映射

```text
Gallery typeId
  └─ IGalleryPageRegistry ──> PageViewModel Type
                                  └─ IPageViewRegistry ──> Avalonia View Type
```

便捷注册 API 在一处更新 Gallery page registry、现有 VM-to-View registry 和 DI：

```csharp
services.AddAvaloniaGalleryPages(pages =>
{
    pages.Add<CgGalleryPageViewModel, CgGalleryPage>("cg");
    pages.Add<VideoGalleryPageViewModel, VideoGalleryPage>("video");
    pages.Add<AudioGalleryPageViewModel, AudioGalleryPage>("audio");
});
```

- 只按精确 Gallery type ID 查找，不按资源类型 fallback。
- 多个 type ID 可以显式注册到同一 VM/View 以复用页面。
- 重复 `Add` 失败；覆盖内置页面使用显式 `Replace`。
- Build 时验证 registry 中每个 VM 都存在 View mapping 和 DI registration。

### 8.2 激活与动态导航

每个 Gallery 内容 VM 实现：

```csharp
public interface IGalleryPageViewModel
{
    Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default);
}
```

Gallery 页面服务按 type ID 从 DI 解析 VM、激活，然后调用导航服务新增的按实例入口：

```text
NavigateAsync(PageViewModelBase viewModel, transition, cancellationToken)
```

这样不需要反射调用现有泛型导航，也不让 Gallery service 负责 View 创建。

### 8.3 零、单、多类型分流

```text
OpenGallery
├─ 0 个有内容类型 -> 入口隐藏/禁用
├─ 1 个有内容类型 -> 直接 OpenType，不压入选择页
└─ 多个类型       -> GalleryIndexPage -> OpenType
```

选择页是独立 MVVM 页面，只列出 `GalleryTypeData` 并调用统一 Gallery 页面服务。缺少页面 mapping 时进入诊断页并显示 type ID；不按资源类型猜测页面，也不静默隐藏内容。

### 8.4 默认页面

- CG：网格卡片、锁定占位、解锁缩略图、独立完整图片页。
- 视频：网格卡片、锁定占位、首帧、独立视频播放页。
- 音频：列表、标题、播放/暂停、时间和进度。

三种页面可以复用私有 item VM、媒体 service 和 helper，但不再通过 `GalleryRendererKind` 决定同一 ViewModel 的分支。图片和视频详情页也不需要以 renderer enum 决定行为。

## 9. Editor 延后范围

本轮只做因公共资源契约变化而必须完成的 Editor 编译适配，例如把 enum 过滤迁移为字符串 type ID。下列产品能力全部后移：

- Gallery annotation 的 Inspector UI；
- 数字 ID 分配/冲突提示；
- metadata Undo/Redo；
- 旧集中式 `gallery.json` 迁移；
- 项目打开时的旧格式诊断和修复工具。

因此本轮验收通过 Sample、测试 fixture 或手工 `.meta` 提供 Gallery annotation，不为旧项目保留双读或兼容路径。

## 10. 候选方案与取舍

### 10.1 一个大模块同时注册 DTO、decoder 和页面

不采用。DTO 是跨平台数据模型，decoder 依赖目标 CLR 类型，页面依赖 Avalonia；捆绑后 Headless 和不同 UI 宿主都会被迫处理无关能力。

### 10.2 只把 enum 换成字符串，不注册 Meta DTO

不采用。插件虽然能写新 type ID，但 LocalFileProvider 无法把类型专属 metadata 解析成可靠模型，pak 也无法把这些数据交给 decoder。

### 10.3 保留 renderer enum 和资源类型 fallback

不采用。它仍要求所有展示形态进入内置分支，且同一资源类型不能可靠表达不同 Gallery 交互。

### 10.4 GUID item ID 加独立 sortOrder

不采用。当前产品选择数字 ID 便于配置并直接表达导出后的静态顺序；作者负责维持全局唯一数字。

### 10.5 兼容旧格式

不采用。项目处于快速迭代期，兼容 reader、迁移 journal 和双模型测试会显著扩大代码；示例、测试和开发项目直接更新。

## 11. 风险与缓解

| 风险 | 影响 | 缓解 |
|---|---|---|
| 数字 ID 手工冲突 | 两个 item 映射同一解锁变量 | 聚合时全局失败并报告两个 `.meta` 路径 |
| 数字 ID 被误改 | 解锁身份和最终顺序同时变化 | 把 ID 视为导出内容的稳定身份；聚合时严格检查正数和全局唯一性 |
| DTO 注册与 JSON discriminator 不一致 | 错误类型被错误解析 | 两段式读取并校验 registry key、DTO TypeId 与 pak table 三者一致 |
| 两个插件声明同一扩展名 | 导入类型随加载顺序变化 | registry 冻结时失败，不使用后注册覆盖 |
| 自定义 metadata 未进入 pak | 目录运行正常、发行失败 | pak entry 保存 canonical typed metadata JSON；导出/读取 round-trip 测试 |
| 同一目标 CLR 类型有多个 decoder | 后注册 decoder 覆盖前者 | decoder key 使用 `(resourceTypeId, target CLR type)` |
| Gallery page registry 与 View registry 不一致 | VM 可解析但 View 创建失败 | 组合 API 同时注册并在 Build 时验证 |
| 动态资源类型扩大首轮范围 | Gallery 重构被底层迁移拖慢 | Phase Plan 先完成资源身份/DTO/pak，再做 Gallery 聚合和页面 |

## 12. 验收影响

后续 Phase Plan 至少覆盖：

- 完整资源类型注册、追加扩展名、显式替换及所有冲突路径；
- 根据 discriminator 选择 Meta DTO，并让自定义字段通过 LocalFileProvider 与 pak round-trip；
- 字符串 `IGameFile.TypeId`、字符串枚举过滤和 `(typeId, target type)` decoder 分派；
- 正整数 Gallery ID 的全局唯一性、变量名生成和数字升序；
- 同一资源多类型、同类型多项及类型不匹配失败；
- `.meta` 到 Gallery catalog/生成 JSON 的确定性；
- `gallery.unlock(number)` 与通用 Player bool 操作观察同一状态；
- 精确页面注册、显式复用、诊断页以及零/单/多类型导航；
- CG、视频和音频独立 MVVM 页面完成现有功能；
- 当前 solution 在删除 `ResourceType` enum 后全部编译，Editor 只做必要适配而不扩张 authoring UI。

## 13. 待确认：统一资源会话与发布包安装模型

> 2026-09-27 提案；尚未接受，不改变当前实现事实。

当前 `IAssetManager` 向调用方暴露了 `IGameFile`、Provider 注册以及按资源 ID 的全局 `Release`。这允许调用方绕过加载会话并可能释放其他调用方正在使用的同一资源。建议把目录与发行包统一为一个由资源管理器拥有的只读 mount，并把资源使用权表达为可释放的 handle。

```text
GameLocation (项目目录 | .galpak)
  -> GamePackageInstaller（仅 .galpak：校验、原子解压、复用安装目录）
  -> GameContentMount（manifest、content.pak、assets.pak、冻结 registries）
  -> AssetManager（唯一资源索引、GUID/path 查询、decoder、缓存）
  -> AssetHandle<T>（单次 acquire 的引用；Dispose/DisposeAsync 后释放）
```

`AssetHandle<T>` 应只在成功加载后持有一次引用，暴露稳定 GUID、类型和 `Value`；其 `Dispose` 必须幂等。缓存键仍是 `(assetGuid, target CLR type)`，但引用计数只能由 handle 递增/递减，删除 `Release(id)`、`Release<T>(id)`、`LoadAsync(IGameFile)` 和外部 `IGameFile` 枚举。编辑器 picker 或 Gallery compiler 改为查询只读 `AssetDescriptor`（GUID、逻辑路径、type ID、可安全公开的 metadata 摘要），不取得原始字节或 stream。

`AssetManager.OpenAsync(GameLocation, composedRegistries)` 应完成一次索引建立。目录 mount 扫描并严格校验 `.meta`；包 mount 读取 manifest 与两个 PAK。两者向 manager 提供同一种内部 `AssetRecord`，故调用者只用 GUID 获取 handle，路径只用于 authoring/debug 的 GUID 解析。任何无效/未知 metadata、重复 GUID、manifest hash 或 registry snapshot 不匹配都使 open 失败，不允许静默跳过。

导出保持 `.galpak` ZIP 容器；内部 `assets.pak` 和 `content.pak` 仍适合随机访问和完整性校验。若产品要求“传入压缩包即解压为文件夹”，该职责应属于 `GamePackageInstaller`，而非 `AssetManager`：先校验 zip 路径、manifest 和 hash，解压到相邻的受控安装目录（临时目录完成后原子 rename），记录版本/hash 和 lock，防 Zip Slip、半解压与多进程竞争。安装后才以目录 mount 打开。直接从 ZIP/PAK mount 可作为后续优化，但不是首轮必要路径。

这会让 Gallery compiler 从 manager 的 descriptor snapshot 编译 catalog；发行运行从安装后的 `content.pak` 读取 generated `gallery.json`，并用同一 registry 验证 snapshot。这样资源加载、引用计数、目录/包来源和 Gallery 的资源可达性都在同一个会话闭环内。

