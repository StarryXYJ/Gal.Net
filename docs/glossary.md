# GalNet 术语表

## 内容与运行时

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 图 | `Graph` | 剧情流程图，由节点和边组成。 |
| 入口节点 | `Entry` | 图的起始节点；Runtime 将其视为不含条目的 Group。 |
| 组 | `Group` | 线性执行一组 Entry；编辑源条目放在单独的 `.rawgalgroup`。 |
| 分支 | `Branch` | `Choice`（玩家选择）或 `Condition`（条件匹配）节点。 |
| 条目 | `Entry` | 剧情动作的数据载体；可为原语或非原语。 |
| 原语条目 | `PrimitiveEntry` | 有 Runtime Handler、可直接执行的条目；编译 `.galgroup` 只允许这一类。 |
| 非原语条目 | `NonPrimitiveEntry` | 没有 Handler，通过 `Compile(EntryCompileContext)` 展开为有序原语。 |
| 条目定义 | `EntryDefinition` | 条目类型、分类、参数 schema、默认值、可选值与原语分类的注册信息。 |
| 条目处理器 | `EntryHandler` | Runtime 中执行一个原语条目类型的实现。 |
| 原始组文件 | `.rawgalgroup` | 编辑器保存的 Group 源文件，可含原语和非原语，`kind` 为 `Raw`。 |
| 编译组文件 | `.galgroup` | 仅供 Runtime 加载的 Group 产物，只含原语，`kind` 为 `Compiled`。 |
| 运行时 | `IGameRuntime` / `GameRuntime` | 当前节点、条目位置、变量、场景实例和文本解析器的唯一状态源。 |
| 游戏引擎 | `GameEngine` | 驱动节点转移与条目执行，产生 checkpoint 和存档快照。 |
| 呈现接口 | `IGameView` | Runtime 向具体 UI/无界面宿主发送画面和交互请求的端口。 |
| 快照 | `GameSnapshot` | 可保存和恢复的运行时位置、变量与场景状态。 |

## 场景与动画

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 场景状态 | `SceneState` | 可存档的图层、激活控件/效果和转场标识。 |
| 场景实例 | `ISceneInstance` | 由稳定句柄定位的活跃场景对象。 |
| 图层 | `Layer` | 背景与立绘的统一资源图层，拥有 transform、z、显示模式和 opacity。 |
| 场景句柄 | `handleId` | 作者内容中用于定位场上实例的稳定字符串，不是显示名称。 |
| 动画请求 | `AnimationRequest` | 对单一可动画属性的一次 Replace 模式插值请求。 |
| 动画曲线 | `IAnimationCurve` | 归一化时间到进度值的函数；支持内置、三次贝塞尔与 LUT。 |

## 页面与 UI

| 术语 | 英文 / 类型 | 当前含义 |
| --- | --- | --- |
| 页面流 | `GameFlowFactory` | 创建固定的标题、游戏、设置、存读档、鉴赏和关于页面。 |
| 页面导航器 | `IGameScreenNavigator` | Control 默认页面的当前页和回退栈。 |
| UI 预设 | `IUiPagePreset` | 某个固定页面的设置 schema 和默认值，不含可实例化的模板 View。 |
| UI 项目 | `UiProject` | 宿主提供的页面预设选择与设置覆盖。 |
| 独立游戏页面宿主 | `GalNet.Avalonia.GameView` | 使用 ViewModel→View 注册表和 `GameShell` 的 Avalonia 页面实现。 |

`WidgetTemplate`、`WidgetInstance`、`ScreenTemplate`、`ScreenInstance` 与调色板模板体系是历史设计术语，不是当前 Control 的实现模型。

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
| 资源管理器 | `IAssetManager` | 注册提供者、加载、缓存和释放已解析资源。 |
| pak | `.pak` | `PakBuilder` 构建的资源归档，含资源索引和数据。 |
| 分发包 | `.galpak` | 当前为 ZIP，含 `Assets/content.pak`、`Assets/assets.pak` 与 JSON manifest。 |
| manifest | `<项目名>.galnet` | 当前 `.galpak` 内描述项目与两个 pak 哈希的 JSON 文件；不是独立逻辑二进制。 |
