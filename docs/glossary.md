# GalNet 术语表

## 内容与运行时

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 图 | `Graph` | 剧情流程图，由节点和边组成。 |
| 入口节点 | `Entry` | 图的起始节点；Runtime 将其视为不含条目的 Group。 |
| 组 | `Group` | 线性执行一组 Entry；编辑源条目放在单独的 `.rawgalgroup`。 |
| 分支 | `Branch` | `Choice`（玩家选择）或 `Condition`（条件匹配）节点。 |
| 条目 | `Entry` | 剧情动作的数据载体；可为 primitive 或 composite。 |
| Primitive 条目 | `PrimitiveEntry` | 编译后 Runtime 可直接分发的通用 primitive envelope。 |
| Composite 条目 | `CompositeEntry` | 只存在于 authoring/编译期，通过 `Compile(EntryCompileContext)` 展开为有序 primitive。 |
| 条目定义 | `EntryDefinition` | 条目类型、分类、参数 schema、默认值、可选值与 primitive/composite 分类的注册信息。 |
| Entry 模块 | `IEntryModule` | 持有冻结 `PrimitiveEntries` 与 `CompositeEntries` 的模块。 |
| Primitive 定义 | `PrimitiveEntryBase` | 一个 primitive 的参数 schema 与 instance 工厂。 |
| Primitive 实例 | `PrimitiveInstance` | 一次 primitive 调用产生的运行状态，暴露 blocking、skippable、completed、BatchId、dispatch 和 skip。 |
| 原始组文件 | `.rawgalgroup` | 编辑器保存的 Group 源文件，可含 primitive 和 composite，`kind` 为 `Raw`。 |
| 编译组文件 | `.galgroup` | 仅供 Runtime 加载的 Group 产物，只含 primitive，`kind` 为 `Compiled`。 |
| 运行时 | `IGameRuntime` / `GameRuntime` | 当前节点、条目位置、变量、场景实例和文本解析器的唯一状态源。 |
| 游戏引擎 | `GameEngine` | 驱动节点转移、活动 primitive 队列、batch skip、Choice 和稳定快照。 |
| 游戏视图 | `IGameView` | 单条 primitive 的定义解析、参数规范化、实例创建和 dispatch 入口。 |
| 呈现端口 | presenter interfaces | 宿主提供的窄端口，例如 dialogue、choice、layer、animation、effect。 |
| 快照 | `GameSnapshot` | 可保存和恢复的运行时位置、变量与场景状态。 |
| Gallery 目录 | `GalleryCatalog` | 由资源 `.meta.gallery[]` 与冻结类型注册聚合、在发行包中生成快照的不可变类型与资源条目索引，通过 `GameContent.Gallery` 交给宿主。 |
| Gallery 类型 | `GalleryTypeRegistration` | 一个稳定 Gallery type ID 与资源类型字符串的关联；不等同于 Entry 模块或资源 decoder。 |
| Gallery 条目 | `GalleryItem` | 使用全局唯一正整数 ID、Gallery type ID 和资源 GUID 描述的一项静态 Gallery 内容。 |
| Gallery 解锁变量 | `gallery_<item-id>_unlocked` | 每个 Gallery item 自动生成、默认 `false`、跨存档槽持久化的系统 Player bool；是 Gallery 解锁的唯一真源。 |

## 场景与动画

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 场景状态 | `SceneState` | 可存档的图层、效果、粒子和长期动画状态。 |
| 场景实例 | `ISceneInstance` | 由稳定句柄定位的活跃场景对象。 |
| 图层 | `Layer` | 背景与立绘的统一资源图层，拥有 transform、z、显示模式和 opacity。 |
| 场景句柄 | `handleId` | 作者内容中用于定位场上实例的稳定字符串，不是显示名称。 |
| 动画请求 | `AnimationRequest` | 对单一可动画属性的一次 Replace（绝对值）或 Additive（相对增量）插值请求。 |
| 动画计划 | `AnimationPlanDefinition` | 多 track 时间线；当前作为单个 primitive instance 执行。 |
| 效果状态 | `ActiveEffectState` | 可存档的活跃效果描述，包含 effect id、program、instance id、目标、顺序和参数。 |

## 页面与 UI

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 游戏页面宿主 | `GalNet.Avalonia.GameView` | 使用 `GameShell`、页面导航服务和 ViewModel→View 注册表的共享 Avalonia 页面实现。 |
| 游戏导航服务 | `IGameNavigationService` | 管理游戏 Scope 内的当前页面、回退历史和导航转场。 |
| 页面注册表 | `IPageViewRegistry` / `IPageViewFactory` | 在组合期建立并解析不可变的 ViewModel→View 映射。 |
| 游戏根页面 | `GameShell` | 承载页面切换、页面内容和游戏截图入口的 Avalonia 根控件。 |
| 游戏页面呈现 | `AvaloniaGamePageView` | 将 presenter 端口接到 Avalonia 游戏页面和场景渲染。 |

`WidgetTemplate`、`WidgetInstance`、`ScreenTemplate`、`ScreenInstance`、`UiProject` 与调色板模板体系是历史设计术语，不属于当前游戏页面宿主。

## 编辑器与项目

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 编辑器文档 | `EditorProjectDocument` | 命令处理器编辑的 UI 无关聚合：图、组条目和项目设置。 |
| 图作者文档 | `EditorGraphDocument` | `Graph/graph.json` 的 DTO，包含编辑器坐标、稳定 ID 和变量定义。 |
| 项目服务 | `IProjectService` | 新建、打开、关闭与保存项目，并管理每个项目的 DI scope。 |
| Dock 贡献 | `IDockPanelContribution` | 描述一个编辑器面板的创建、位置、生命周期能力和可选检查器。 |
| 检查器贡献 | `IInspectorControlContribution` | 为当前活动面板创建检查器 ViewModel 与 View。 |

## 资源与发布

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 资源提供者 | `IAssetProvider` | 打开命名资源归档的来源，例如本地目录或 pak。 |
| 归档 | `IArchive` | 按资源 ID 或路径寻址的一组 `IGameFile`。 |
| 资源管理器 | `IAssetManager` | 从 provider 按 GUID 定位资源，按 `(GUID, CLR 类型)` 解码/缓存，并以 `AssetHandle<T>` 引用计数释放已解析资源。 |
| 资源句柄 | `AssetHandle<T>` | 一次成功 acquire 的独立资源引用；幂等 Dispose 只释放该次引用。 |
| pak | `.pak` | `PakBuilder` 构建的资源归档，含资源索引和数据。 |
| 分发包 | `.galpak` | ZIP 分发容器，含根特殊内容、生成的 Gallery JSON、`Assets/Paks/000-base.pak` 与 JSON manifest；可验证后解压安装。 |
| manifest | `<项目名>.galnet` | 当前 `.galpak` ZIP 根目录中唯一的 JSON manifest，描述项目与所有包的 SHA-256/大小；不是独立逻辑二进制。 |
| 游戏安装 | `GameInstallation` | 将开发目录或验证并安装后的 `.galpak` 统一为内容 provider 与有序资源 provider 的宿主输入。 |
