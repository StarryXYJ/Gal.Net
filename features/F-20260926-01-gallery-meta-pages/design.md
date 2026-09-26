---
feature: F-20260926-01-gallery-meta-pages
status: proposed
created: 2026-09-26
updated: 2026-09-26
---

# Gallery 元数据聚合与可扩展页面注册设计

## 1. 设计结论

本 feature 把当前 Gallery 实现拆成四个彼此独立的扩展面：

1. **资源类型 registry**：平台无关的字符串资源类型及扩展名映射，替代 `ResourceType` enum 和散落的推断 switch。
2. **Gallery 类型 registry**：代码/插件注册 `typeId -> resourceTypeId`，Editor 只能消费，不能在项目数据中创建类型。
3. **Gallery authoring 与聚合**：资源 `.meta` 中的标注是唯一编辑真源；预览、开发运行或导出时编译为冻结 catalog 和派生 `gallery.json`。
4. **Avalonia Gallery 页面 registry**：只按精确 `typeId` 把 Gallery 类型映射到独立页面 ViewModel；现有页面 registry 继续负责 ViewModel 到 View。

`GalleryRendererKind`、`GalleryRendererResolver` 和同时承担图片、视频、音频行为的 `GalleryContentPageViewModel` 不再是扩展边界。Core 不认识页面，资源类型不认识 Gallery 页面，Gallery 数据也不拥有媒体播放器。

## 2. 当前实现及重构原因

当前实现已经验证了 catalog、Player bool 解锁、零/单/多类型导航和默认媒体页面，但仍有三组封闭关系：

- `ResourceType` enum 进入 `IGameFile`、`IAssetManager.GetFilesAsync` 和 `.pak` 整数类型字段；`.meta` 到 enum、文件扩展名到类型的映射分别由多个 switch/集合维护。
- Gallery 类型和 item 由项目根 `gallery.json` 编辑；资源 `.meta` 只是被引用对象，复制资源时不会携带 Gallery 标注。
- Avalonia 先把 type ID/资源类型解析为 `GalleryRendererKind`，再让单个 ViewModel 根据 enum 分支。这使插件必须修改内置分派或依赖隐式 fallback。

目标不是创建一个包办解码、编辑器和 UI 的“资源模块”基类，而是把稳定身份和各平台能力拆开注册。插件只注册它实际提供的能力。

## 3. 边界与依赖方向

```text
Core
├─ ResourceTypeCatalog        string ID + extension metadata
├─ GalleryTypeCatalog         typeId -> resourceTypeId
├─ AssetMeta                  asset identity + Gallery annotations
└─ GalleryCatalogCompiler     registries + metas -> frozen catalog

Storage / Assets
├─ IGameFile.TypeId           string
├─ LocalFileProvider          reads .meta
├─ Pak v2                     stores UTF-8 type ID
└─ AssetManager               filters by string type ID

Editor
├─ consumes both registries
├─ edits AssetMeta.Gallery[]
├─ previews compiled catalog
└─ exports generated gallery.json

Avalonia GameView
├─ GalleryNavigationService
├─ GalleryPageRegistry        Gallery typeId -> ViewModel Type
├─ existing PageViewRegistry  ViewModel Type -> View Type
└─ independent CG / video / audio pages
```

依赖始终从平台实现指向 Core 契约。Core registry 不保存 Avalonia `Control`、ViewModel `Type`、Skia bitmap、decoder 实例或本地路径。

## 4. 动态资源类型 registry

### 4.1 身份与注册内容

资源类型使用规范化字符串 ID。首轮注册只包含所有层都能合理理解的数据：

```text
ResourceTypeRegistration
├─ Id                 例如 sprite、audio、video、effect-program
└─ Extensions[]       例如 .png、.jpg；可为空
```

推荐组合接口形态：

```csharp
services.AddResourceTypes(types =>
{
    types.Add("unknown");
    types.Add("sprite", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif");
    types.Add("audio", ".mp3", ".wav", ".ogg", ".flac", ".m4a");
    types.Add("video", ".mp4", ".webm", ".mkv", ".avi", ".mov");
    types.Add("effect-program", ".sksl");
});
```

注册完成后生成不可变 `IResourceTypeCatalog`。ID 忽略首尾空白并规范化为小写；只允许字母、数字、`_`、`-`、`.`。扩展名规范化为带前导点的小写形式。

重复 ID、同一扩展名被多个类型声明、空 ID 或非法扩展名在组合阶段失败。首轮不采用“后注册覆盖”，因为插件加载顺序不应静默改变资源解释。

### 4.2 不进入资源类型定义的能力

下列能力保持独立注册：

- `IAssetDecoder<T>` 仍按目标 CLR 类型注册。当前 AssetManager 的转换缓存也按 `(assetId, target CLR type)` 建键，强行按资源 type ID 合并会改变已有语义。
- Editor inspector、图标、缩略图和属性面板属于 Editor 扩展。
- Avalonia/Skia/LibVLC 的实际加载与播放属于展示层。
- 资源类型专用导入参数可在以后形成独立 metadata schema provider；本 feature 不把 `AssetMeta` 改成任意插件对象容器。

因此，“注册资源类型”保证该类型能被识别、保存、枚举和打包；插件若需要把它解码成特定对象，仍需同时注册对应 decoder。

### 4.3 现有资源契约迁移

- 删除公开 `ResourceType` enum。
- `AssetMeta.Type` 保持字符串，但加载后必须通过 catalog 规范化和验证。
- `IGameFile.Type` 改为 `string TypeId`，`GameFile` 同步修改。
- `IAssetManager.GetFilesAsync(ResourceType?)` 改为可选字符串 type ID；比较使用规范化后的 ordinal ID。
- Editor 的扩展名推断只读取 `IResourceTypeCatalog`，删除 `AssetCatalogService` 和 `AssetFileCommandExecutor` 中重复的硬编码表。
- `AssetPickerFilter` 不再持有 enum；内置选择器可持有允许的 type ID 集合或谓词。

### 4.4 Pak 兼容性

`.pak` v1 使用 int32 enum，无法表达插件类型。目标 `.pak` v2 把每项类型改为长度前缀 UTF-8 字符串：

```text
[TypeIdLength:int32][TypeId:utf8]
```

写入器只生成 v2。读取器在迁移期继续读取 v1，并把既有整数值映射为内置字符串；未知 v1 数值映射为 `unknown` 并产生诊断。v2 对未知字符串不丢弃，可在缺少插件时保留身份并延迟到实际消费点报告能力缺失。

这是发布格式变化，需要定向兼容测试；不能直接把 enum 的整数值替换成字符串而仍宣称版本 1。

## 5. Gallery 类型 registry

Gallery 类型完全由宿主和插件在组合期注册：

```csharp
services.AddGalleryTypes(types =>
{
    types.Add("cg", "sprite");
    types.Add("video", "video");
    types.Add("audio", "audio");
});
```

每项仍只有：

```text
GalleryTypeRegistration
├─ TypeId
└─ ResourceTypeId
```

registry 冻结时验证：

- `TypeId` 规范化后唯一；
- `ResourceTypeId` 必须已经存在于最终资源类型 catalog；
- 重复注册明确失败，不采用加载顺序覆盖；
- registry 冻结后不支持热添加或移除。

Editor 不再提供 `RegisterGalleryTypeCommand` 或删除 Gallery 类型的项目命令。生成的 `gallery.json` 可以携带 registry 的类型快照，使发布内容自描述，但该数组是代码注册的派生结果，不是第二个 authoring 来源。

## 6. `.meta` Gallery authoring 模型

### 6.1 格式

每个资源 `.meta` 增加可选数组：

```json
{
  "id": "asset-guid",
  "type": "sprite",
  "path": "cg/opening.png",
  "gallery": [
    {
      "id": "gallery-item-stable-id",
      "typeId": "cg",
      "title": "Opening",
      "sortOrder": 10
    }
  ]
}
```

父级 `AssetMeta.Id` 就是运行时 `resourceId`，标注中不重复保存。`gallery[].id` 是 Gallery item 和解锁变量的稳定中间 ID；完整变量名继续由统一规则生成：

```text
gallery_<gallery-item-id>_unlocked
```

数组允许：

- 同一资源属于多个 Gallery type；
- 同一资源在同一 type 中出现多次；
- 每次出现拥有不同 item ID、标题和排序。

因此唯一性只约束全项目 item ID，不能用 `(resourceId, typeId)` 去重。

### 6.2 首轮字段范围

为了保持最小模型，首轮复用当前 item 已有字段：`id`、`typeId`、`title`、`sortOrder`。分组、封面帧、标签和本地化标题不进入首轮 schema；以后可在标注对象中版本化增加，而不改变聚合边界。

推荐继续使用编辑器生成的 32 位小写 GUID 字符串作为 item ID。它无需中央计数器，复制、插件导入和多人分支合并时碰撞概率可忽略。若产品必须使用纯数字 ID，需要先补充项目级分配器、合并冲突和导入重编号语义；在确认前设计不假定纯数字。

### 6.3 校验

聚合时一次性验证：

- asset ID 与 Gallery item ID 全项目唯一；
- `typeId` 已注册；
- 资源 `.meta.type` 与 Gallery type 的 `resourceTypeId` 相同；
- item ID 满足变量名可逆规则；
- 标注数组和字段 JSON 合法。

任何失败都带资源 `.meta` 路径和 item ID。预览/导出不得静默跳过无效标注，否则 Editor 与发行包会观察到不同 Gallery。

## 7. Gallery 编译与生成物

新增平台无关的 catalog compiler 概念：

```text
GalleryCatalogCompiler
  input:
    frozen ResourceTypeCatalog
    frozen GalleryTypeCatalog
    all AssetMeta snapshots
  output:
    validated GalleryConfiguration
    frozen GalleryCatalog
    generated system Player variable definitions
```

同一组输入必须得到字节稳定的结果：类型按 `typeId` 排序，item 按 `typeId`、`sortOrder`、`id` 排序。时间戳、文件枚举顺序和绝对路径不得进入 `gallery.json`。

为降低迁移量，生成的 `gallery.json` 首轮继续使用当前 `version: 1` 的 `types[] + items[]` 结构；“按类型聚合”由冻结 `GalleryCatalog` 索引提供，不为派生文件的视觉嵌套额外升级格式。以后只有运行时契约确实变化时才升级版本。

不同宿主只决定输出位置：

- **Editor Preview**：从当前 `.meta` snapshot 编译，写入现有预览临时内容目录，再走和正式运行相同的 loader。
- **导出**：编译后直接把 `gallery.json` 写入 `content.pak` 的输入集合；不先写回项目根。
- **目录开发运行**：启动时编译一次并冻结为内存 catalog。若以后证明扫描成本显著，再把同一确定性 JSON 放入 `.galnet/generated` 并用 meta/registry fingerprint 失效；首轮不为了未经测量的性能引入持久缓存。
- **发行运行**：只读取包内生成的 `gallery.json`，不扫描 `.meta`。

项目根旧 `gallery.json` 不再被普通保存路径更新，也不覆盖 `.meta`。

## 8. Editor 行为与迁移

资源检查器从两个 registry 计算可选项：先取得当前资源的规范 `typeId`，再列出所有引用该资源类型的 Gallery 类型。勾选、取消或增加重复项只修改当前资源 `.meta.gallery[]`。

Gallery metadata 修改复用项目文件命令的沙箱路径校验和临时文件原子替换，但应使用结构化 `AssetMeta` patch，不能继续用大小写不稳定的自由 `Dictionary<string, JsonElement>` 拼字段。文件 watcher 在 `.meta` 更新后刷新资源投影和 Gallery 系统变量投影。

当前资源文件命令不进入 Graph Undo history，而现有 Gallery catalog 修改会进入 Graph Undo。迁移后不能假装仍有相同行为。推荐增加一个仅覆盖 metadata patch 的可逆编辑：记录目标 `.meta` 修改前后的字节或结构化 snapshot，Undo/Redo 都经过相同的受限文件命令写回。它不应把所有导入、移动和删除命令强行纳入 Graph history。

### 8.1 旧 `gallery.json` 迁移

推荐显式、一次性迁移，而不是打开项目即静默改写多份文件：

1. 读取并验证旧 `gallery.json`；
2. 由 `resourceId` 找到唯一 `.meta`；
3. 把每个 item 转成对应 `gallery[]` 标注并保留原 item ID、标题和排序；
4. 所有目标先写入 staging，全部成功后原子替换各 `.meta`；
5. 将旧文件移动到 `.galnet/migrations/gallery-v1.json` 作为恢复副本；
6. 重新聚合并比较 item/type 语义等价后才报告成功。

资源缺失、重复 asset ID、未知 type 或目标 item ID 冲突时整次迁移失败，不做部分写入。是否由用户显式触发仍需产品确认；无论入口为何，都必须遵守上述事务边界。

## 9. Avalonia 页面注册与导航

### 9.1 两层 registry

现有 `IPageViewRegistry` 继续只回答 `ViewModel Type -> View Type`。新增的 `IGalleryPageRegistry` 只回答 `Gallery typeId -> ViewModel Type`，两者不合并进 Core：

```text
Gallery typeId
    │ IGalleryPageRegistry
    ▼
PageViewModel Type
    │ IPageViewRegistry
    ▼
Avalonia Control Type
```

推荐提供一个组合期便捷 API，在一处同时登记两张表并把 VM/View 加入 DI：

```csharp
services.AddAvaloniaGalleryPages(pages =>
{
    pages.Add<CgGalleryPageViewModel, CgGalleryPage>("cg");
    pages.Add<VideoGalleryPageViewModel, VideoGalleryPage>("video");
    pages.Add<AudioGalleryPageViewModel, AudioGalleryPage>("audio");
});
```

多个 type ID 可以分别调用 `Add` 指向同一 VM/View 类型，从而显式复用页面。没有按 `resourceTypeId` fallback，也没有 renderer enum。重复 type ID 注册在组合阶段失败。

### 9.2 页面激活契约

Gallery 内容页实现一个窄的 Avalonia 契约：

```csharp
public interface IGalleryPageViewModel
{
    Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default);
}
```

实际 VM 仍继承 `PageViewModelBase`。Gallery 页面服务从 DI 解析 registry 指定的 VM，调用该契约，再把 VM 实例交给导航服务。为此给现有导航服务增加按实例导航的非泛型入口，比反射调用泛型 `NavigateAsync<TViewModel,TArgs>` 更小、更可测试：

```text
NavigateAsync(PageViewModelBase viewModel, transition, cancellationToken)
```

该入口只负责历史和呈现，不负责 DI 或 Gallery 激活。

### 9.3 分流

`GalleryNavigationService` 是唯一分流点：

```text
OpenGallery
├─ 0 个有内容类型 -> 返回不可用；标题页隐藏/禁用
├─ 1 个有内容类型 -> OpenType(typeId)
└─ 多个类型       -> Navigate GalleryIndexPage

OpenType(typeId)
├─ 从 Gallery data source 取得 GalleryTypeData
├─ 从 Gallery page registry 解析 VM type
├─ 激活 VM
└─ 按实例导航
```

选择页只显示数据并调用同一个 `OpenType`；它不直接依赖任何 CG/视频/音频 VM。单类型路径不实例化也不压入选择页，因此返回历史保持现有语义。

缺少页面映射时推荐导航到显式诊断页并记录 type ID，而不是按资源类型猜测页面或静默隐藏数据。若宿主希望启动即失败，可在组合根额外运行“已使用 Gallery 类型必须都有页面”的严格验证，但不把它设为 Core 默认。

### 9.4 默认页面

- `CgGalleryPageViewModel`：图片卡片和缩略图，解锁后进入独立图片查看页。
- `VideoGalleryPageViewModel`：视频卡片和首帧，解锁后进入独立视频播放页。
- `AudioGalleryPageViewModel`：音频列表、播放状态和进度。

三者可以复用内部 item VM、排序 helper 和媒体服务，但不通过一个 renderer enum 决定公开页面形态。共享代码只在出现真实重复后提取为私有 helper，不新增跨平台抽象。

## 10. 状态与变量

Gallery item ID 继续生成唯一系统 Player bool。`GalleryItemData` 建议同时暴露：

```text
Item
UnlockVariableName
IsUnlocked
```

变量名由 Core 的单一 helper 生成，Editor、primitive 和 UI 不各自拼接。`gallery.unlock(itemId)`、通用变量操作和 Gallery 查询继续观察同一 Player variable store；解锁值不写回 `.meta` 或生成的 `gallery.json`。

## 11. 插件生命周期和失败策略

- 所有 registry 在宿主组合完成后冻结；本 feature 不支持热加载。
- 插件缺失时，v2 pak 和 `.meta` 仍能保留未知字符串 type ID；需要实际导入、解码或打开 Gallery 页面时给出明确诊断。
- Gallery 聚合要求所引用的 Gallery type 和 resource type 已注册，因此缺少提供类型的插件会阻止预览/导出，不会生成内容不完整的包。
- Avalonia 页面插件可以和数据插件分离；缺少页面时使用诊断页策略，不影响无头运行时加载 catalog。
- 注册冲突包含来源信息是推荐能力，但 registry 正确性不能依赖插件加载顺序。

## 12. 候选方案与取舍

### 12.1 把 Gallery 类型、资源类型和页面放进一个模块对象

不采用。它会迫使 Core 引用 UI/decoder 能力，或产生大量可空属性。拆成三个 registry 后，Headless、Editor 和 Avalonia 宿主只组合自己需要的部分。

### 12.2 保留 `GalleryRendererKind`，允许插件增加 enum 外的字符串

不采用。无论增加 resolver delegate 还是 `Unknown` fallback，内置大 ViewModel 仍承担所有媒体状态，新增真正不同的页面仍要进入原类。

### 12.3 同时保存 `.meta` 和可编辑 `gallery.json`

不采用。两个可写真源必然需要冲突解决；资源复制、撤销和外部编辑都可能产生漂移。运行 JSON 只能由 registry 与 `.meta` 单向生成。

### 12.4 让资源类型 registration 包含 decoder、Editor inspector 和 Avalonia UI

不采用。平台依赖、生命周期和目标 CLR 类型不同，形成的“大注册对象”比现有 enum 更难组合。稳定 type ID 是连接点，各平台能力分别注册。

### 12.5 使用整数资源类型或整数 Gallery item ID

资源类型不采用整数，因为插件无法在不协调全局编号的情况下稳定扩展。Gallery item ID 若强制纯数字同样需要中央分配和合并规则；默认继续使用稳定字符串/GUID，仅把完整变量名前后缀视为派生信息。

## 13. 风险与缓解

| 风险 | 影响 | 缓解 |
|---|---|---|
| Pak 类型字段从 int 变字符串 | 旧包兼容和发布格式变化 | 明确 v2；保留 v1 reader；定向 round-trip 测试 |
| registry 在 Editor、Preview、导出宿主中组合不一致 | Editor 可见但导出失败，或生成结果不同 | 共用同一 Core registry builder/快照；导出器不再自行维护硬编码集合 |
| 多个 `.meta` 的旧数据迁移中途失败 | 部分资源已迁移、双真源 | staging + 全量校验 + 原子替换 + 备份 |
| `.meta` 外部编辑导致重复 item ID | 解锁变量碰撞 | 聚合全局校验并定位到两个文件 |
| metadata 命令与 Graph Undo 语义不同 | 用户感知功能回退 | 建立仅针对 metadata patch 的可逆 edit，或在实现前显式接受不支持 Undo |
| 插件只注册资源类型但没注册 decoder/UI | 类型可保存但不能消费 | 分层诊断；文档明确能力需分别注册 |
| 页面 registry 与通用 view registry 不一致 | 导航到 VM 后无法创建 View | 组合 API 原子登记两张表，并在 Build 时验证 |
| 目录运行每次扫描所有 `.meta` | 大项目启动开销 | 首轮测量；只在开发模式扫描一次；必要时再加入 fingerprint cache |

## 14. 验收影响

后续 Phase Plan 至少需要覆盖以下验证面：

- resource type registry 的规范化、重复 ID/扩展名和未知类型；
- `.pak` v2 字符串类型 round-trip 及 v1 兼容读取；
- 多 Gallery 标注、多次同类型标注、全局 item ID 冲突和资源类型不匹配；
- `.meta` 到生成 `gallery.json` 的确定性及 Preview/Export 等价；
- 旧 `gallery.json` 的全成功迁移和失败零部分写入；
- 系统 Player bool 名称与解锁状态保持兼容；
- 精确 type ID 页面注册、显式页面复用、缺失映射诊断；
- 零类型、单类型直达、多类型选择页和返回历史；
- CG、视频、音频 VM 之间不存在 renderer enum 分派，媒体生命周期互不泄漏。

## 15. 仍需确认

设计可以在以下推荐默认值下进入 Phase Plan，但在实现相应部分前仍应确认：

1. item ID 默认继续使用 32 位小写 GUID 字符串，而不是纯数字；`.meta` 只存中间 ID，不存完整变量名。
2. 首轮 annotation 字段只保留 `id/typeId/title/sortOrder`。
3. 旧 `gallery.json` 迁移使用显式命令并保留 `.galnet/migrations` 备份。
4. Gallery metadata 编辑保留 Undo/Redo，通过可逆 metadata patch 实现。
5. 开发目录运行首轮只建立内存 catalog，不做持久 cache；Preview 和导出物化 JSON。
6. Avalonia 缺页使用诊断页，严格启动校验作为宿主可选项。

