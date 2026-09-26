---
id: F-20260926-01-gallery-meta-pages
title: Gallery 元数据聚合与可扩展页面注册
type: feature
status: design
created: 2026-09-26
updated: 2026-09-26
---

# Gallery 元数据聚合与可扩展页面注册

## 原始目标

从 `F-20260922-01-entry-instance-runtime` 中拆出独立 Gallery 重构 feature，简化当前 Avalonia Gallery 的展示分派，并为以后由插件增加 Gallery 类型和页面保留扩展点。

编辑阶段不再维护项目根目录下可编辑的 `gallery.json`；资源的 Gallery 标注只保存在对应资源 `.meta` 中。Editor Preview、目录运行准备或导出时聚合全部 `.meta`，生成只读的运行时 `gallery.json`（或等价内存 catalog），供加载性能、校验和打包使用。生成物不是编辑真源，不允许和 `.meta` 双向编辑。

## 与原 feature 的关系

- `F-20260922-01-entry-instance-runtime` 已完成 Gallery catalog、Player bool 解锁、Editor 标注和 Avalonia 默认页面的首轮垂直切片，本 feature 不回滚其已经验证的运行时能力。
- 本 feature 专门重构首轮实现中已经暴露的 Gallery authoring 与 Avalonia 扩展问题；Entry/`PrimitiveInstance`、Engine 推进和其他媒体 primitive 不属于本 feature。
- 当前正式文档仍描述集中式 `gallery.json` 是编辑真源、`GalleryRendererKind`/renderer resolver 负责页面分派；在本 feature 实现并验证前，这些文档仍是当前事实。

## 当前事实与问题

- 当前 Editor 在项目根目录单独读写 `gallery.json`，资源 `.meta` 只提供 asset ID、资源类型、路径、过滤和压缩等资源信息。
- 当前 Gallery item 在集中 catalog 中引用资源 ID；资源检查器通过 `.meta` 的资源类型反查可选 Gallery 类型。
- 当前 Avalonia 使用封闭的 `GalleryRendererKind` 和 `GalleryRendererResolver`，再由一个 `GalleryContentPageViewModel` 分支处理图片、视频、音频及未知类型。
- 当前前端支持零类型隐藏、单类型直达、多类型先进入选择页，也支持按 Gallery type ID 和资源类型 fallback 选择 renderer。
- 这种 renderer enum 加单一大型 ViewModel 的结构把不同媒体的状态和行为集中在一个类中；增加插件 Gallery 类型仍需要修改内置 enum/resolver 或复用不透明的 fallback，扩展边界不清楚。
- 当前通用页面注册已经支持 ViewModel 类型到 View 的映射，但导航入口主要是编译期泛型 API；按运行期 Gallery type ID 解析并导航到已注册 ViewModel 仍需要一个明确适配层。

## 已确认需求

- Gallery Core/数据层继续保持平台无关，只提供所有 Gallery 类型、每种类型对应的 Gallery item 以及当前解锁状态，不知道 Avalonia View、ViewModel、导航和媒体控件。
- Gallery 类型至少包含稳定 `typeId` 和对应的资源类型字符串；Gallery item 至少包含稳定 item/变量 ID、资源 ID 和所属类型。
- 每个 item 继续映射到固定格式的系统 Player bool；UI 从 Gallery 服务取得规范变量名或已计算的解锁状态，不自行拼接另一套名称规则。
- 编辑期 Gallery 标注的唯一真源是资源 `.meta`。同一标注不得同时作为可编辑内容保存在 `.meta` 和项目 `gallery.json` 中。
- `gallery.json` 是由 `.meta` 聚合出的派生运行时数据，只在预览、运行内容准备或导出/打包阶段生成；运行时不需要扫描全部 `.meta` 才能建立 Gallery。
- Gallery 类型与资源类型在游戏服务组合阶段使用流式/Builder API 注册；Gallery 本身只依赖稳定的字符串类型身份，不依赖封闭 renderer enum。
- Gallery 类型完全由代码、宿主或插件注册，Editor 不提供创建项目私有 Gallery 类型的 authoring 入口。
- 同一资源允许加入多个 Gallery 类型，也允许在同一 Gallery 类型中出现多次；资源 `.meta` 使用 Gallery 标注数组，每项拥有独立稳定 ID。
- 资源类型也改为组合期动态注册：现有 `ResourceType` enum、`.meta` 解析 switch、资源枚举过滤、Editor 扩展名推断和 `.pak` 中的封闭整数类型编码都必须迁移到稳定字符串 ID。注册 decoder 和平台专用预览仍是可独立组合的能力，不塞进 Core 类型描述。
- Avalonia 另有 Gallery 页面注册服务，只维护 `Gallery typeId -> ViewModel 类型` 的映射；ViewModel 到 View 继续复用现有页面注册/导航基础设施。
- 类型选择页和每种 Gallery 内容页都是独立 MVVM 页面。没有有内容类型时隐藏或禁用入口；只有一种时直接进入对应内容页，不把选择页压入导航历史；多种时先进入类型选择页。
- 默认提供 CG、视频和音频页面。自定义 Gallery 类型由宿主或插件显式注册页面；不同 type ID 可以显式复用同一个 ViewModel/View，而不需要 renderer fallback enum。
- 页面只按精确 Gallery `typeId` 显式注册和解析；移除按 `resourceTypeName` 自动选择图片、视频或音频页面的 fallback。
- 目标是减少分支和样板代码，同时允许以后通过组合根或插件增加 Gallery 类型和 Avalonia 页面；本阶段不要求运行期热插拔。

## 范围（待设计）

- 为资源 `.meta` 增加可版本化的 Gallery 标注结构，并定义规范化、重复、断链和迁移规则。
- 建立冻结的资源类型 registry，迁移资源元数据、运行时资源契约、Editor 类型推断/过滤和 pak 类型编码，使插件可增加新的字符串资源类型。
- 将 Editor 的 Gallery 增删改、撤销/重做、保存调度和资源检查器从集中 catalog 迁移到 `.meta` 标注。
- 在 Preview、目录内容准备和导出路径中聚合 `.meta`，生成验证后冻结的 `GalleryCatalog` 与可打包 `gallery.json`。
- 保留运行时按生成 catalog 快速加载、系统 Player bool 生成、`gallery.unlock` 和通用变量操作共享状态的行为。
- 用独立页面 ViewModel 替换 `GalleryRendererKind`、`GalleryRendererResolver` 和包含多种媒体分支的 `GalleryContentPageViewModel`。
- 建立 Gallery 页面注册与导航协调层，复用已有 ViewModel-to-View registry，覆盖零/单/多类型导航和返回历史。
- 为内置 CG、视频、音频注册默认页面，并验证宿主可新增类型、复用页面或提供新页面。
- 为旧项目根 `gallery.json` 提供一次性迁移或明确诊断，迁移完成后不再把它作为编辑真源。

## 非目标

- 不重构 Entry/`PrimitiveInstance`、`GameEngine` 推进或其他媒体 primitive。
- 不把 Avalonia 类型、CLR ViewModel 类型或页面路由写入 Core catalog、资源 `.meta` 的跨平台 Gallery 数据或运行时 `gallery.json`。
- 不在本 feature 中实现运行期插件热加载、卸载或注册表动态变化；注册可在应用组合完成后冻结。
- 不默认实现 galgroup 场景回放；场景 Gallery 仍需要隔离回放会话和存档策略。
- 不把 decoder、Avalonia/Skia 对象、Editor inspector 或缩略图控件塞进平台无关的资源类型定义；插件可分别注册这些平台能力。
- 不长期保留 `.meta` 与手写 `gallery.json` 双写、双向同步或冲突合并。

## 验收标准（discovery）

- 明确 Gallery 类型注册的唯一来源，以及项目自定义类型与插件提供类型的覆盖/冲突规则。
- 明确 `.meta` 中 Gallery 标注的基数、稳定 ID、显示字段和版本迁移规则。
- 明确 Editor Preview、目录运行和导出分别何时聚合、缓存和失效，并保证生成 `gallery.json` 可重复且不成为编辑真源。
- 明确旧集中式 `gallery.json` 到 `.meta` 的一次性迁移策略，以及资源缺失、类型缺失和重复 ID 的失败行为。
- 明确 Avalonia 页面 registry、ViewModel 激活参数和动态导航接口，且 Core/Runtime 不依赖 Avalonia。
- 明确缺少页面映射、重复页面注册和插件缺失时的可观察行为。
- 在上述问题确认后再进入设计与 Phase Plan，不在 discovery 阶段修改业务代码。

## 约束与假设

- Runtime/Core 不引用 Avalonia、具体文件系统、媒体播放器或插件宿主类型。
- `.meta` 是 Editor authoring 数据；导出包中的 `gallery.json` 是生成数据。相同输入必须产生稳定顺序和稳定内容，避免无意义 diff 与不可复现构建。
- Gallery item 的稳定身份不能由资源路径、标题或排序位置推导；资源移动、改名和重新排序不得改变解锁变量。
- Gallery 页面映射按精确 `typeId` 注册；是否保留资源类型 fallback 属于待确认兼容策略，不默认写入核心数据模型。
- 已完成的 Player variable 持久化是解锁状态真源；Gallery 标注和生成 catalog 只描述静态内容，不保存当前解锁值。
- 当前工作树可能包含原 feature 的未提交实现；本 feature 必须在后续实现时保护无关改动并先建立迁移测试。

## 开放问题

1. item/变量 ID 是否继续使用当前稳定字符串/GUID，并由它生成 `gallery_<id>_unlocked`；还是必须限制为纯数字 ID？纯数字需要额外的分配、跨项目合并和插件冲突规则。
2. `.meta` 中首轮除 `typeId`、item ID 外是否只保留当前已有的 `title`、`sortOrder`，把分组、封面帧和本地化留到后续？
3. 旧项目 `gallery.json` 应由显式迁移命令安全写入各资源 `.meta` 并保留备份，还是在 Editor 打开时自动迁移？
4. Gallery 标注迁移到资源文件命令后，是否必须保留当前 Gallery 编辑的 Undo/Redo？普通资源 metadata patch 当前不进入文档 Undo history，保留它需要新增可逆的 metadata edit 边界。
5. 目录开发运行是否把生成的 `gallery.json` 写入 `.galnet` 缓存目录，还是只在会话启动时建立等价内存 catalog？发行包和 Editor Preview 都会物化生成 JSON。
6. Avalonia 缺少某个 type ID 的页面注册时，应显示诊断页还是阻止 Avalonia 宿主启动？当前倾向显示诊断页，以免内容静默消失，同时允许未使用的插件类型存在。

## 相关链接

- [设计（Proposed）](design.md)
- [原 Entry/运行时 feature](../F-20260922-01-entry-instance-runtime/feature.md)
- [原 Gallery 设计章节](../F-20260922-01-entry-instance-runtime/design.md#10-gallery-内置能力)
- [当前文件格式规范](../../docs/spec/file-formats.md#galleryjson)
- [当前资源元数据规范](../../docs/spec/assets.md)
- [当前架构规范](../../docs/spec/architecture.md)
- [当前 renderer enum](../../src/GalNet.Avalonia.GameView/ViewModels/GalleryRendererKind.cs)
- [当前 renderer resolver](../../src/GalNet.Avalonia.GameView/ViewModels/GalleryRendererResolver.cs)
- [当前 Gallery 内容 ViewModel](../../src/GalNet.Avalonia.GameView/ViewModels/GalleryContentPageViewModel.cs)
- [当前页面注册与导航](../../src/GalNet.Avalonia.GameView/Navigation/PageNavigation.cs)
- [当前资源元数据模型](../../src/GalNet.Core/Assets/AssetMeta.cs)

