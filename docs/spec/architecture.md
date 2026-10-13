# GalNet 架构

## 分层

`Core` 不依赖 UI、文件系统或 DI。Runtime 只依赖 Core 与呈现抽象；宿主组装文件系统、资源、媒体和页面实现。[ADR-0001](../adr/0001-runtime-storage-contract-ownership.md) 已完成落地，旧 `GalNet.Storage.Abstractions` 项目已删除。

```text
Core ───────────────────── 领域模型、资源/Gallery registry、服务契约
Contracts.Builtins ──────── 仅依赖 Core 的内置 schema、参数和 Composite
Presentation.Abstractions ─ IGameView 与细分 presenter 端口
Runtime ────────────────── Graph、Compiled Group、Engine、存档状态与宿主端口
Assets ─────────────────── Local/PAK provider、Archive、AssetManager
Storage.FileSystem ─────── 项目/安装内容、保存、变量与进度实现
ProjectTools.Windows ───── Editor 抽象的 Windows 系统回收站适配
Avalonia.GameView ──────── 共享页面、导航、Gallery page registry、呈现
Editor.Shared ──────────── 项目读写、命令、导出
Editor / Samples ───────── 组合根与平台宿主
```

## 物理布局

`src` 按职责分组，项目名默认与程序集名一致；Sample.Contracts 为工具固定契约约定输出 GalNet.Project.Contracts：

```text
src/
  Shared/          Core、Contracts.Builtins、Runtime、Assets、Presentation.Abstractions、Primitives.Builtins
  Presentation/    Avalonia.Controls、Avalonia.Rendering、Avalonia.GameView、Avalonia.Themes
  Infrastructure/  Storage.FileSystem、ProjectTools.Windows
  Editor/          Editor.Abstraction、Editor.Shared、Editor、Editor.Headless
  Samples/         Sample.Contracts、Sample.Avalonia、Sample.Headless
  Launcher/        Launcher 共享项目与 Android、Browser、Desktop、iOS 宿主
```

物理分组用于表达查找和所有权，不改变程序集名称、根命名空间或依赖方向。新增生产项目应放入与其职责一致的分组，并同步 `GalNet.slnx`；测试项目统一位于 `test/<Assembly>.Tests`。

程序集拥有自己的根命名空间：展示与执行注册端口位于 `GalNet.Presentation.Abstractions.*`，内置 schema、Entry DTO 与 Composite 位于 `GalNet.Contracts.Builtins`，primitive 和执行 module 位于 `GalNet.Primitives.Builtins`。契约项目只依赖 Core，执行模块按领域共享状态，可依赖 Runtime 的平台无关逻辑（如音频轨道）；Runtime 不反向依赖 Builtins。扩展程序集不得在 `GalNet.Core.*` 下声明类型；Core 只保留领域基础类型和扩展协议。注册与程序集拆分决定见 [ADR-0002](../adr/0002-entry-schema-execution-registration.md)、[ADR-0004](../adr/0004-project-entry-contract-assembly.md)。

资源层只向上暴露 `IAssetProvider`、`IArchive`、`IGameFile` 和 `IAssetManager`。provider 定位源文件；manager 负责 GUID 查询、decoder 分派、缓存和 `AssetHandle<T>` 生命周期。Core、Runtime 与 Editor.Shared 不引用 Avalonia。

## 内容、资源与宿主组合

宿主在组合期建立并冻结一对 `IResourceTypeCatalog`、`IGalleryTypeCatalog`。同一实例必须传入开发/安装内容 provider、资源 provider、Preview 和 exporter；catalog 不是从内容文件反向创建的。这样资源 DTO、PAK、Gallery 编译和页面选择能对插件类型使用同一身份表。

```text
开发项目 ── ProjectGameContentProvider ── .meta -> GalleryCatalog
           LocalFileProvider(Assets/) ───── GUID -> raw bytes

安装目录 ── InstalledGameContentProvider ─ generated gallery.json + Graph/
           PakFileProvider(Assets/Paks/**/*.pak) ─ GUID -> PAK bytes
```

`.galpak` 只是 ZIP 运输容器。`GameInstallation` 验证并解压后，内容 provider 读取 Graph/settings/I18n/gallery 等特殊文件；只有 PAK 会成为资源 provider。详见 [资源](assets.md) 与 [文件格式](file-formats.md)。

## 游戏运行

`GameEngine` 持有 `IGameRuntime`，解释图、条目、Choice、skip batch 和快照；它不认识具体 UI。Engine 统一报告运行状态，负责停止、取消执行 scope 和等待内部 continuation 退出，不拥有执行 view 或平台后端。`IGameView` 解析 entry schema、创建 primitive instance 并调用 presenter 端口。影响场景的 primitive 先写入 `SceneState`，再通知呈现端；读取快照后由宿主调用 Builtins 的场景重放入口。

Avalonia 展示按领域拆为 dialogue/choice、layer、animation 和 particle presenter；`AvaloniaGamePresenters` 只创建并暴露这些 presenter，`CompositeGameView` 组合独立的 schema catalog 与执行模块，提供 Runtime 使用的 `IGameView`。官方 Avalonia Sample 的页面会话只串行化公开命令和顶层 UI 状态：`SampleGameResourceScope` 管理安装内容与资源，`SampleSaveSession` 管理玩家持久化与 Gallery 数据，`SampleGameRunner` 绑定 Engine 与页面、准备读档及首次推进，`SampleGamePresentation` 管理执行 view 和平台展示资源，`PersistentSceneRestorer` 复用 Builtins 重放 renderer-owned 场景对象。没有通用 Runner 抽象或必须继承的宿主基类。runner 必须先于资源 scope 异步释放。

Gallery catalog 是平台无关的静态内容。宿主把其自动生成的 `gallery_<item-id>_unlocked` Player bool 注册到变量服务；`GalleryDataSource` 合并 catalog 与当前 Player 值。Gallery 解锁不存于独立 UI 状态或存档槽。

Avalonia GameView 通过精确 Gallery `typeId` 的 `IGalleryPageRegistry` 选择页面。零个有内容类型时入口不可用，一个类型直达，多类型显示选择页；没有页面映射时进入诊断页，绝不按资源类型 fallback。Gallery 媒体的路径适配由宿主 resolver 完成，并且必须以 `AssetManager` acquire 的内容为来源。

## 编辑器工作流

`EditorProjectDocument` 是 UI 无关的图、Group、设置编辑聚合。资源 metadata 属于 `Assets/`；Gallery annotation 不进入 Editor document，也不存在集中式 Gallery authoring 文件。预览编译 Raw Group，并用当前项目的 metadata 和共享 catalog 构建 `GameContent.Gallery`。

CLI 只做参数、结果协议和退出码适配；Shared 提供原始查询、无写回校验、定向编译、严格编辑/保存、项目/Group 生命周期服务。原始访问保留未知内容，不依赖 entry DLL；有语义的操作使用项目 Contracts DLL，不回退内置。Editor 的 ProjectSchemaSession、catalog 及文档消费者由项目 scope 持有。Windows 回收通过 Editor.Abstraction.IProjectRecycleBin 注入，Shared 不引用 Shell；Editor 先关闭匹配会话并等待清理，再回收，最后更新最近记录。当前接口及范围见 [CLI README](../../src/Editor/GalNet.Editor.Headless/README.md)。

`EditorAssetManager`、`EditorGameDataProvider`、`AssetCatalogService` 与 `GameExportService` 共享 Editor 组合根的 catalog。导出把资源写入基础 PAK，把 Graph/settings/I18n 和派生 Gallery JSON 写入 `.galpak`。

`EditorWorkspaceViewModel` 保留可观察 UI 状态、命令编排、history checkpoint 和自动保存触发。`GraphSelectionState` 维护节点/边选择与 `IsSelected` 一致性；`EditorWorkspacePersistence` 组合 repository、document service、mapper、save coordinator 与 project service 完成图的加载、保存和 Preview 数据构建。内置 Editor 命令由单一 handler 入口分派，Graph、Entry、Variable 和 Project 实现按领域分文件维护。

Editor 扩展 registry 通过非泛型 `IDockPanelContribution` 和 `IInspectorControlContribution` 保存异构贡献，`object` 只存在于该类型擦除边界。新 contribution 应优先继承 `DockPanelContributionBase<TViewModel>`、`DockPanelContributionBase<TViewModel, TParameter>` 或 `InspectorControlContributionBase<TDockViewModel, TInspectorViewModel>`，由 bridge 集中校验 parameter、dock ViewModel 和 inspector ViewModel 类型；错误类型会在贡献边界报告预期与实际类型。`IServiceProvider` 继续表示全局或当前项目 scope，view 继续返回 `object` 以避免 Editor.Abstraction 引用 Avalonia。内置 panel 和 inspector contribution 均使用这些强类型入口。

## 测试边界

| 项目 | 职责 |
| --- | --- |
| `GalNet.Architecture.Tests` | 项目引用、命名空间与仓库结构门禁 |
| `GalNet.Core.Tests` | Core 领域模型与纯逻辑 |
| `GalNet.Runtime.Tests` | Runtime 状态、加载、表达式与存档序列化 |
| `GalNet.Primitives.Builtins.Tests` | 内置 entry module 与 primitive 行为 |
| `GalNet.Assets.Tests` | 资源 provider、压缩与加密 |
| `GalNet.Storage.FileSystem.Tests` | 文件系统存档、变量与进度实现 |
| `GalNet.Editor.Shared.Tests` | Editor 协议、命令、文档与持久化协作者 |
| `GalNet.Editor.Tests` | Editor UI、ViewModel 与交互逻辑 |
| `GalNet.Presentation.Tests` | 展示抽象、Avalonia 展示与渲染逻辑 |
| `GalNet.IntegrationTests` | 跨 Assets、Storage、Editor、Runtime 和 Sample 的端到端场景 |

Integration 测试只承载必须联合多个外层实现的场景；单一生产边界的测试留在对应项目，不通过引用其他测试项目共享实现。CI 分别执行纯逻辑与架构、Editor/Presentation 以及 Integration 三组测试。

## 维护规则

- 不让 Core、Runtime 或 Editor.Shared 反向引用具体 Avalonia 实现。
- `GalNet.Architecture.Tests/ProjectDependencyTests` 检查内层与共享项目、10 个测试项目的直接依赖 allowlist，禁止测试项目互相引用，并要求 `GalNet.Storage.Abstractions` 项目及引用保持不存在；依赖边界变化必须同步更新 ADR 或设计依据。
- 内容、保存、玩家变量、变量桥接和进度协议由 Runtime 拥有；资源访问协议由 Core.Assets 拥有；Editor 设置和退出协议由 Editor.Abstraction 拥有。
- `ISaveService` 只提供异步、可取消的 I/O API，保存请求统一使用 `SaveRequest` 携带 snapshot 与可选展示 metadata。
- Provider 不管理解码对象或引用计数；页面/媒体实现不理解目录与 PAK 布局。
- 新资源类型、Gallery 类型、decoder 与页面各自注册在所属层；不要合并为一个跨层“资源模块”。
- 新持久化字段要同时检查作者 JSON、加载器、导出包、安装加载和验证测试。

变量桥接实现 `PlayerVariableService` 归 Runtime.Variables，仅通过 `IPlayerVariableStore` 持久化；文件实现为 `FilePlayerVariableStore`。变量值复制使用 Core 的 `Variable.Clone`，保留 Uid 并可显式替换名称；不可变 VariableValue 可共享。

Runtime 的 GraphLoader/GalgroupLoader/SaveManager 仅解析和序列化内容；文件路径和 I/O 由外层内容 provider、宿主和 ISaveService 实现处理。
