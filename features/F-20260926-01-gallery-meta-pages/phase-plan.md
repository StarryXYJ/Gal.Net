---
feature: F-20260926-01-gallery-meta-pages
status: planned
created: 2026-09-26
updated: 2026-09-27
---

# Gallery 元数据聚合与可扩展页面注册实施计划

## 总体顺序

```text
Phase 1 资源类型 registry 与 Meta DTO
   ↓
Phase 2 资源加载、decoder 与 pak 新格式
   ↓
Phase 3 数字 Gallery catalog 与解锁状态
   ↓
Phase 4 目录运行、Preview 与导出聚合
   ↓
Phase 5 Avalonia 页面 registry 与独立页面
   ↓
Phase 6 清理、全量验证与正式文档
```

资源类型迁移先于 Gallery，是因为 Gallery compiler 必须依赖已经冻结的资源 registry 和类型化 Meta DTO。Avalonia 页面最后接入，避免 UI 重构期间同时改变底层资源身份。

每个 Phase 完成后都应记录实际验证结果；状态只在证据满足退出条件后从 `planned` 改为 `verified`。

## Phase 1：资源类型 registry 与类型化 Meta DTO

**状态：** `planned`

**目标：** 在不先改动资源加载管线的前提下，建立可冻结、可验证的资源类型 registry，以及按 type ID 解析具体 `.meta` DTO 的公共模型和 codec。

**前置条件：** feature 设计审核为 `pass`；不要求兼容既有 `.meta`。

**涉及模块：** `GalNet.Core`、必要的组合扩展项目、`GeneralTest`。

**任务：**

1. 把公共 `AssetMeta` 收敛为可继承的基础 DTO，保留 `Id`、`TypeId`、`Path`、通用 `Compress` 和 `Gallery[]`；Gallery annotation 先建立数据结构，行为在 Phase 3 接入。
2. 为 sprite、audio、video、font、effect-program 和必要的通用类型建立内置 Meta DTO；仅把 `Filter` 等类型专属字段放入子类。
3. 实现 `ResourceTypeRegistration`、Builder 和冻结 `IResourceTypeCatalog`。
4. 支持 `Add<TMeta>(typeId, extensions...)`、`AddExtensions(typeId, extensions...)` 和显式 `Replace<TMeta>`。
5. 规范化 type ID 与扩展名；在冻结时验证重复完整注册、未知扩展目标、扩展名冲突、非法 DTO 类型和显式替换后的完整一致性。
6. 实现两段式 Meta codec：先读 `type` discriminator，再按 registry 的 DTO 类型反序列化并校验，不在 JSON 中写 CLR 类型名。
7. 提供所有宿主可复用的内置资源类型注册入口，避免 Sample、Editor、测试各自复制后缀表。

**测试：**

- 完整注册、追加扩展名、先追加后完整注册、显式替换。
- 重复 type ID、未知追加目标、扩展名冲突、非法字符和非法 DTO 类型。
- 每个内置 Meta DTO 的 JSON round-trip。
- 自定义测试 DTO 的 discriminator 选择和专属字段保留。
- JSON type ID 与反序列化 DTO 不一致时失败。

**文档：** 只更新本 phase plan 的验证记录；正式 `docs/spec` 等到 Phase 6，避免把尚未贯穿资源管线的中间状态写成稳定事实。

**风险：** `System.Type` 与 JSON serializer 配置若散落到宿主，会让同一 type ID 在不同入口得到不同 DTO。必须由冻结 catalog 和统一 codec 承担选择。

**退出条件：** 独立测试能通过相同 registry 正确解析内置与自定义 Meta DTO；所有注册冲突在 Build 时失败；现有业务项目仍可编译。

## Phase 2：资源运行时契约、decoder 与 pak 切换

**状态：** `planned`

**目标：** 将资源系统从封闭 `ResourceType` enum 完整切换到字符串 type ID 和类型化 metadata，并让开发目录与 pak 对插件 DTO 观察一致。

**前置条件：** Phase 1 registry、Meta codec 和内置注册已验证。

**涉及模块：** `GalNet.Storage.Abstractions`、`GalNet.Assets`、`GalNet.Core`、Editor 必要编译适配、Samples、`GeneralTest`。

**任务：**

1. 把 `IGameFile.Type` 改为字符串 `TypeId`，增加 `AssetMeta Metadata`；同步 `GameFile` 和所有测试替身。
2. 把 `IAssetManager.GetFilesAsync` 的过滤条件改为可选 type ID，并统一通过资源 catalog 规范化。
3. 把 decoder 注册与解析键改为 `(resourceTypeId, target CLR type)`；缓存仍按 `(assetId, target CLR type)`，缺少 decoder 时报告完整二元能力信息。
4. 让 `LocalFileProvider` 使用统一 Meta codec，不再调用 `AssetMeta.ParseResourceType()`。
5. 改写 pak entry：保存 UTF-8 type ID 和 canonical typed metadata JSON；reader 经 registry 恢复具体 DTO，并校验表中 Id/Path/TypeId 与 metadata 一致。
6. 直接切换 pak 格式版本；删除旧 int32 enum reader 和所有兼容分支。
7. 删除 `ResourceType` enum、`ParseResourceType()` 以及所有硬编码扩展名 switch/集合；Editor 的文件类型推断和 AssetPicker 只使用 registry/type ID。
8. 更新 Sample、Editor adapter 和 decoder 注册点，确保同一目标 CLR 类型可由不同资源类型分别注册 decoder。
9. 定义 `.galpak` 的安装边界：ZIP 只负责分发，安装器校验 manifest 后解压到同级受控目录；运行时扫描规定的 `Assets/Paks/**/*.pak` 作为全部资源来源，按相对路径倒序确定覆盖优先级。`graph.json`、`.galgroup`、`I18n/`、`settings.json`、生成的 `gallery.json` 和 manifest 作为解压后的特殊文件由对应 loader 单独读取，不进入资源 provider。安装器与内容 provider 不进入 `AssetManager`。

**测试：**

- Local provider 能读取自定义 Meta DTO 和专属字段。
- Pak build/deserialize 保留 type ID、DTO 运行时类型和专属字段。
- Pak table 与 metadata 的 Id、Path 或 TypeId 不一致时失败。
- 两种资源类型到同一目标 CLR 类型的 decoder 分派互不覆盖。
- 字符串 type filter、缓存、并发 single-flight 和释放行为保持现有语义。
- registry 未注册类型、缺少 decoder 和扩展名冲突均给出可定位诊断。
- `.galpak` 解压前验证 manifest 的条目大小和 SHA-256；同一包重复安装可复用，损坏包或不同包覆盖既有安装目录必须明确失败。
- 解压目录中的补丁 PAK 可按文档化优先级加入资源 provider 列表，且无需修改 `AssetManager`。

**文档：** 在 plan 中记录新 pak 验证证据；正式格式文档留到 Phase 6 一次性替换，不保留 v1 兼容说明。

**风险：** 这是传播范围最大的编译切换。禁止在同一 Phase 顺手重构缓存、provider 优先级或压缩算法，以便失败能归因到类型/metadata 迁移。

**退出条件：** 仓库中不再存在业务 `ResourceType` enum 使用；LocalFileProvider 和新 pak 对同一 asset 返回等价 `TypeId` 与具体 Metadata；Core、Assets、Editor、Samples 和 GeneralTest 串行构建通过。

**资源包输入验证（2026-09-27）：** `.galpak` 已作为仅分发 ZIP：`GalpakInstaller` 先校验 manifest 的安全相对路径、文件大小和 SHA-256，再原子解压到同级安装目录。开发项目由 `ProjectGameContentProvider` 按 `Graph/` 和 `Assets/` 加载，并以 `LocalFileProvider` 扫描 `.meta`；安装目录仅扫描 `Assets/Paks/**/*.pak`，按相对路径倒序覆盖，`Graph/`、`I18n/`、`settings.json` 与生成的 `gallery.json` 由 `InstalledGameContentProvider` 直接读取。安装入口与导出器可接收同一对冻结资源/Gallery catalog，避免扩展类型被内部 built-in 表覆盖。`GameTestCase` 已迁移至上述工程布局，旧根目录内容 provider 已删除。`GalNet.Assets.Tests` 的工程→导出→安装→Graph/Gallery→资源查询及补丁优先级测试共 34/34 通过，迁移后的 GameTestCase 冒烟测试通过；Headless、Editor.Shared、Editor 与 Avalonia Sample 均构建通过。`GeneralTest` 全量仍为 228/229：`EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 的换行/缩进快照失败，未触及相关代码。

## Phase 3：数字 Gallery catalog、类型 registry 与解锁状态

**状态：** `planned`

**目标：** 建立由代码注册 Gallery 类型、由 `.meta.gallery[]` 提供 item 的平台无关 catalog，并把数字 ID 贯穿排序、系统变量和 `gallery.unlock`。

**前置条件：** Phase 2 已能可靠枚举带具体 Metadata 的资源。

**涉及模块：** `GalNet.Core.Gallery`、`GalNet.Primitives.Builtins`、变量服务契约与实现、`GeneralTest`。

**任务：**

1. 建立 `GalleryTypeRegistryBuilder` 与冻结 `IGalleryTypeCatalog`；注册 `(typeId, resourceTypeId)`，冻结时验证资源类型存在、重复 Add 失败、显式 Replace 行为明确。
2. 用正 `Int32` 改写 `GalleryItem.Id`、catalog 索引和 JSON DTO；删除 `SortOrder` 和字符串 item ID 规范化逻辑。
3. 实现 `GalleryCatalogCompiler`，输入冻结的资源/Gallery registries 和全部 typed `AssetMeta`。
4. 从父 Meta 的 asset ID 生成 item resource ID；允许同一资源多类型、同类型多项，但要求数字 ID 全局唯一且大于 0。
5. 验证 annotation 的 Gallery type 存在、资源 type ID 匹配、Meta DTO 类型匹配，并在冲突诊断中同时报告两个来源。
6. 每个 Gallery 类型内按数字 ID 升序冻结；生成 JSON 使用确定性类型/item 顺序。
7. 将 `GalleryUnlockVariable`、系统 Player bool 定义、`GalleryDataSource` 和 `gallery.unlock` 改为数字 ID；data source 同时返回规范变量名和 `IsUnlocked`。
8. 把 `gallery.unlock` entry 参数 schema 改为正整数，并删除依赖旧字符串 ID/`gallery.json` 提示的错误信息。

**测试：**

- 数字 ID 为 0、负数、重复时失败；重复诊断包含两个 Meta 来源。
- 同资源跨多个类型、同资源同类型多项均成功。
- 资源 type 与 Gallery type 不匹配、未知 Gallery type、错误 DTO 类型均失败。
- catalog 与生成 JSON 按 type ID、数字 ID 确定性排序。
- `gallery_<id>_unlocked` 生成、保留名称检查、变量写入和 data source 查询一致。
- `gallery.unlock(number)` 解锁存在 item，拒绝未知数字和非正数。

**文档：** 更新 plan 验证记录；暂不改正式 entry/file-format spec。

**风险：** ID 同时承担身份和顺序。实现不得额外保留旧 `sortOrder` fallback，否则会再次形成两套排序规则。

**退出条件：** 仅凭 registries 和内存 Meta fixtures 就能生成冻结 Gallery catalog；数字 ID 的排序、变量和 primitive 全部通过测试；Core/Runtime 无 UI 类型。

## Phase 4：目录运行、Preview 与导出聚合

**状态：** `planned`

**目标：** 让实际宿主从 `.meta` 建立同一 Gallery 内容：目录开发运行启动时聚合一次，Preview/导出生成 JSON，发行运行只读生成内容。

**前置条件：** Phase 2 typed resource pipeline 与 Phase 3 Gallery compiler 已验证。

**涉及模块：** `GalNet.Storage.FileSystem`、`GalNet.Assets`、`GalNet.Editor.Shared` 的内容基础设施、Sample hosts、Headless Sample、`GeneralTest`。

**任务：**

1. 给目录内容加载入口注入冻结资源/Gallery registries 和统一 Meta codec；启动时枚举 `.meta` 一次并建立 `GameContent.Gallery`。
2. 会话建立后不再监听或扫描 `.meta`；目录开发运行首轮不实现持久 cache。
3. 把 `GalleryFileLoader` 定位为生成内容 loader：加载 JSON 后与当前代码 Gallery registry 快照逐项比对，不让 JSON 反向注册类型。
4. 调整导出链，从 typed Meta 编译 Gallery JSON并作为根目录特殊内容直接加入 `.galpak`；资源 PAK 写入 `Assets/Paks/000-base.pak`。
5. 让 Editor Preview 基础设施使用同一个 compiler 和当前 Assets metadata；不实现新的 Gallery Inspector。
6. 移除/禁用当前集中式 `gallery.json` authoring 状态、Gallery type/item 编辑命令和旧 Inspector 入口，避免继续产生第二真源；不提供迁移工具。
7. 更新 Sample 的 `.meta`，用数字 ID 提供 CG、视频和音频内容；所有组合根使用同一内置资源/Gallery 注册入口。
8. 保证 Headless、Avalonia Sample、Editor Preview 和导出不会各自维护 Gallery 类型或资源类型硬编码集合。

**测试：**

- 目录内容从嵌套 Assets `.meta` 聚合 Gallery，且一次会话只构建一次 catalog。
- 相同 Meta/registry 输入生成字节稳定的 Gallery JSON。
- 导出包的根 `gallery.json` 含生成 Gallery JSON，`Assets/Paks/000-base.pak` 含 typed metadata；重新读取后语义等价。
- 包内 Gallery type 快照与代码 registry 不一致时加载失败。
- 缺少资源、重复 asset ID、缺少 Gallery/资源插件时 Preview 与导出给出一致错误。
- 项目根手写 `gallery.json` 不再参与 authoring 或覆盖 Meta 结果。

**文档：** 在 plan 中记录目录、Preview、导出三条路径的验证证据；正式 spec 留到 Phase 6。

**风险：** 当前 `GamePackageExporter` 和部分 provider 是静态入口，容易偷偷保留硬编码集合。它们必须接收共享 registry/catalog context，而不是在方法内重建“已知类型”。

**退出条件：** Headless 与 Avalonia Sample 可只依赖 `.meta` 启动并取得相同 Gallery；导出/重载保持等价；旧集中 authoring 不再可写；未实现 Editor UI 不阻碍 solution 编译。

## Phase 5：Avalonia 页面 registry 与独立默认页面

**状态：** `planned`

**目标：** 删除 renderer enum 和单一多媒体 ViewModel，以精确 type ID 注册独立 MVVM 页面并完成零/单/多类型导航。

**前置条件：** Phase 4 的实际游戏会话已能提供带数字 ID、变量名和解锁状态的 `GalleryTypeData`。

**涉及模块：** `GalNet.Avalonia.GameView`、Avalonia Sample、Editor Preview 必要组合适配、媒体服务、相关测试。

**任务：**

1. 建立冻结 `IGalleryPageRegistry` 和 Builder；支持 `Add<TViewModel,TView>(typeId)`、显式 `Replace`、多个 type ID 复用同一 VM/View。
2. 组合 API 同时写入 Gallery page mapping、现有 VM-to-View registry 和 DI registrations；Build 时验证三者一致。
3. 给 `IGameNavigationService` 增加按 `PageViewModelBase` 实例异步导航入口，复用现有历史和 transition，不使用反射调用泛型导航。
4. 建立 `GalleryNavigationService`：零个有内容类型返回不可用，一个类型直达，多类型进入独立 `GalleryIndexPage`。
5. 选择页只消费类型数据并调用统一 `OpenType`；单类型路径不实例化或压入选择页。
6. 实现缺页诊断页；不按 resource type fallback，不静默隐藏已存在内容。
7. 拆出独立 CG、视频、音频 ViewModel/View，以及必要的独立图片详情和视频播放页。
8. 复用合理的私有 item/media helper，但删除 `GalleryRendererKind`、`GalleryRendererResolver`、`GalleryContentPageViewModel` 和通过 renderer enum 分派的详情逻辑。
9. 注册内置 `cg`、`video`、`audio` 页面；增加一个测试自定义 type，验证新页面和显式复用都无需修改内置 resolver。

**测试：**

- 页面 registry 的重复 Add、显式 Replace、缺失 DI/view mapping 和多个 type 复用。
- 零类型隐藏/禁用、单类型直达不污染历史、多类型选择和返回历史。
- 缺页进入诊断页；resource type 相同但 type ID 未注册页面时不得自动 fallback。
- CG/视频锁定与解锁卡片、详情页；音频播放/暂停、时间和进度。
- 页面离开/重复激活时缩略图取消、播放器事件解绑和资源释放。
- 自定义 Gallery 页面能够通过注册接入，不修改内置 switch/enum。

**文档：** 记录页面 registry 与导航验证；正式 Avalonia 扩展说明在 Phase 6 同步。

**风险：** 页面 VM 为 game scope service，导航历史可能再次引用同一实例。每次 `ActivateAsync` 必须取消旧异步工作、清理旧媒体订阅并重建页面状态。

**退出条件：** 默认 CG、视频、音频功能均由独立页面完成；零/单/多类型行为与返回历史通过测试；仓库中不存在 Gallery renderer enum/resolver 或资源类型 fallback。

## Phase 6：清理、验证与正式文档

**状态：** `planned`

**目标：** 删除所有被新模型替代的路径，完成跨项目验证，并只把已实现事实同步到正式文档。

**前置条件：** Phase 1-5 均已验证。

**涉及模块：** 全部受影响项目、Samples、`GeneralTest`、`docs/spec`、`docs/glossary.md`、feature 文档。

**任务：**

1. 全仓搜索并删除残留 `ResourceType` enum/switch、`ParseResourceType`、旧 decoder 注册、集中 `gallery.json` authoring、字符串 Gallery ID、`SortOrder` 和 renderer fallback。
2. 检查所有组合根都使用共享资源/Gallery registries；没有宿主私有的重复 built-in 类型表。
3. 运行格式/静态检查、GeneralTest、相关项目串行构建和可运行 Sample smoke validation。
4. 对导出包做端到端验证：Meta DTO → assets.pak → IGameFile.Metadata，以及 `.meta.gallery[]` → generated gallery.json → Player bool/UI。
5. 更新 `docs/spec/assets.md`、`file-formats.md`、`architecture.md`、`runtime.md`、`entry-types.md`、`control.md` 和 `docs/glossary.md`；删除旧集中 authoring、enum 和 renderer fallback 描述。
6. 回写每个 Phase 的实际偏差、验证命令和结果；如实现与设计不同，先修订 feature design，再进入 closeout。

**测试与验证：**

- `dotnet test test/GeneralTest/GeneralTest.csproj --no-restore -m:1 -p:UseSharedCompilation=false`
- 串行构建 `GalNet.Sample.Avalonia`、Headless Sample 和 `GalNet.Editor`；包含 Avalonia 的命令设置 `AVALONIA_TELEMETRY_OPTOUT=1`。
- `git diff --check`。
- `rg` 确认旧公共类型、旧 authoring 文件路径和 renderer 分派没有业务残留；测试名称或历史 feature 文档不作为误报。

**风险：** 全解决方案构建可能因未安装 Android/Browser/iOS SDK 而失败。必须区分平台 SDK 探测限制和本 feature 引入的业务编译错误，并以相关桌面/测试项目的串行构建作为主要证据。

**退出条件：** 所有验收标准有测试或可重复验证证据；相关项目构建通过；正式 spec 与实现一致；feature 可进入独立代码 review 和 closeout。

