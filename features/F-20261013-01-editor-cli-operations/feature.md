---
id: F-20261013-01-editor-cli-operations
title: 编辑器无 UI 操作层与 CLI 跟进
type: feature
status: implementation
created: 2026-10-13
updated: 2026-10-13
---

# 编辑器无 UI 操作层与 CLI 跟进

## 用户原始目标

> 目前整个编辑器都是非常落后于项目的情况，接下来需要慢慢跟进项目。先实现命令部分，cli 对项目进行操作，不涉及 UI，操作层和 cli 分开，方便后续 ui 版本复用。操作包括但不限于新建项目、根据 guid 查找、删除项目、重命名项目、新建 galgroup 等等，你先列表。不过有个问题是没有引擎啥的好像不能得知目前有哪些 entry 支持和对应的类型，这部分打算怎么搞。

最初通过 create 流程建立需求并调查现状；设计与阶段计划已形成，用户已明确授权开始实现。Phase 1、2 已验证，继续 Phase 3；逐阶段验证并提交。

## 用户补充与已确认需求

- GUID/稳定 ID 查询只覆盖项目内资源、节点和条目，不增加项目 GUID 或跨项目发现。
- 用户接受 AI 直接修改项目文件；CLI 可以侧重校验逻辑和工具方法，不要求所有写入必须通过命令接口。后续 UI 仍需要可复用的共享能力。
- 当前核心问题是独立 CLI 进程如何获取项目所选的 entry 注册集合。用户提出单次调用/循环进程、schema 导出、项目指定目录的 schema DLL 等候选，尚未选定最终接入方式。
- 用户随后选择固定目录、固定名称的契约 DLL：开发者维护单独契约项目，以一个类的静态列表集中组合所有支持的 schema；工具和游戏工程共用同一契约。
- 内置 schema 需要按组件提供静态构造方法，并提供汇总全部内置 schema 的方法；开发者可选择部分组件、全部内置或自定义 schema。
- DLL 路径、程序集名称和入口类/成员的具体名称尚未确定；运行实现与表现层如何匹配是当前讨论重点。
- 用户进一步明确：游戏工程引用独立契约项目，按正常项目引用构建；契约项目也能独立构建并部署 DLL，供 CLI 和 Editor 使用。游戏侧不要求动态加载契约。
- Editor 默认读取当前内容项目的契约 DLL，因为各项目支持集合可以不同；catalog 属于项目上下文，不是全编辑器共享的固定内置全集。
- 用户要求梳理 schema 到执行层完整链路，保证低耦合；当前仍记录需求和讨论结论，不开始业务实现。
- 用户同意将内置契约抽到新的独立项目，隔离 Runtime 和执行实现依赖；程序集/项目具体名称待定。
- 用户提出单平台自定义 entry 直接操作视图层的需求；应保留该扩展路径，不强制开发者为每个指令增加 presenter 接口。
- 用户要求后续实现截图 entry 作为单平台扩展示例，继续推进 feature。
- 用户已确认截图行为：自动保存场景 PNG 到宿主截图目录，不弹对话框。设计采用 Sample 自定义 sample.screenshot，而不加入全内置集合；执行直接复用 GamePage.CaptureScenePng。
- 用户已确认项目删除行为：将整个项目目录放入系统回收站，不是永久删除或仅移除最近项目记录。
- 用户要求内置 schema 为多个独立模块，只有返回所有模块列表的汇总方法，不保留合成大模块或内置专用 catalog 工厂。
- 用户要求每完成并验证一个实施阶段提交一次，只提交本 feature 的改动。

## 当前推进范围

- 已形成 [设计](design.md) 与 [阶段计划](phase-plan.md)，从内置契约抽离开始推进。
- 首期覆盖独立契约与加载、查询/校验工具、项目级 Editor 数据接入、build/export 契约一致性、必要项目/Group 生命周期，以及截图 entry 联调；不把原始候选表中的全部 UI/I18n/Gallery 等追赶项强制纳入。
- 目录/名称采用设计默认值 Contracts/GalNet.Project.Contracts.dll 与静态 ProjectSchemas.Modules；可在实施前调整，不表示用户逐字指定。
- 项目删除采用系统回收站；不可用/失败时明确报错，不回退永久删除。删除语义已收敛，不再阻塞对应 Phase。

## 问题

- 编辑命令、项目设置和内容能力需要跟上当前项目格式及运行契约。
- CLI 与后续 UI 需要复用同一操作语义、校验及持久化行为。
- 用户需要在不启动游戏 Engine、播放器或 UI 的情况下查询可用 entry 及参数类型。
- “根据 GUID 查找”已限定项目内资源、节点和条目；需要正确区分资源 GUID 与其他稳定 ID。

## 已核实的仓库事实

1. 已有 `GalNet.Editor.Abstraction` 命令/文档协议、`GalNet.Editor.Shared` 实现及 `GalNet.Editor.Headless` CLI。共享项目不引用 Avalonia，应优先评估复用及补齐，不默认另建一套操作系统。
2. Headless 已支持 create、summary、nodes、node、entries、variables、validate、build、export 和 execute；execute 支持 JSON 文档命令批次，文件命令只允许单独执行。
3. 项目创建存在 `EditorProjectCreator` 与 UI 使用的 `ProjectService.CreateAsync` 两条路径。当前 `GalProject.Id` 由目录名称/创建名称传入，`ProjectSettings` 没有持久化项目 GUID；不能声称已经支持按项目 GUID 定位。
4. `EditorCommandCatalog` 已有图节点/边、entry、分支、变量、项目显示名和部分设置命令；`AssetFileCommandCatalog` 已有资源导入、目录创建、移动、重命名、删除和部分 metadata 编辑。
5. `project.rename` 当前只修改 `Graph.Name`，不移动项目目录；settings patch 当前只覆盖尺寸、存档槽数、SfxChannelCount 和 TargetLocale。
6. 当前作者源是 `Graph/groups/*.rawgalgroup`；`.galgroup` 是编译产物。新建可编辑 GalGroup 应创建 Raw Group，并维护 Graph 节点/文件引用。
7. 资源 GUID 来自 `.meta`；Graph 节点/边、Group 条目等有稳定字符串 ID。Graph 与 Group 不通过资源 GUID provider 查询；不同对象的 ID 不应混为同一个资源协议。
8. ADR-0002 已把 schema/composite 与 primitive 执行注册分离。`IEntryModule` 不含执行工厂；`TargetProfileEntryCatalog` 可只由 schema 模块组成。
9. `BuiltinSchemas.CreateCatalog()` 可以不创建 Engine、IGameView 或平台后端；当前 Headless 已使用它，但固定选择全部内置 schema，未按具体游戏宿主能力选择。
10. 参数契约已有 `DynamicParameterDescriptor` 的进程内 `ValueType`、required、JSON 默认值和 constraints；`EntryParameterType` 只是编辑提示，不能代替真实参数契约。内容不保存 CLR 类型名。
11. Composite 编译需要展开代码，只有参数描述不足以完成编译。声明 schema 也不代表目标宿主已提供执行实现。
12. 当前 `EditorDocumentRepository` 加载条目会调用 catalog.Get；未知类型直接失败，并且会按已知 schema 筛选参数。缺失或不匹配 schema 会影响读取项目，不能假设已经能够无损处理未知条目。
13. 当前 execute 的文档批次先克隆、执行和验证，再保存；多文件保存不能仅凭每文件临时替换就宣称整体事务原子性。文件操作也不能假设已有统一 dry-run/回滚语义。
14. `GameRegistration.BuildCatalog()` 由显式注册的 `_schemas` 构造 catalog；这些注册是进程内状态。仓库当前 Editor 路径未发现外部 schema DLL 加载入口，不能假设另一进程能自动读取游戏宿主的注册结果。
15. `DynamicParameterTable` 可附带 `Action<JsonElement>` 自定义校验代码；通用 JSON schema 导出不能自动等价替代这部分逻辑。
16. `CompositeGameView` 按完整 typeId 检查实现必须有 primitive schema，并拒绝重复实现；只注册 schema 的 view 仍允许创建，实际缺失执行由运行检查暴露。
17. 内置执行已有 Dialogue、Nvl、Layers、Animations、Audio、Video、Effects、Particles、Flow、Variables 和 Gallery 等领域入口，但 `BuiltinSchemas.CreateModule()` 当前集中构造全部内置 schema，没有对应的组件级公开构造入口。
18. 当前 `AddBuiltinImplementations()` 注册所有内置执行模块；部分 schema 集不能直接配全量执行注册，否则未声明的实现会触发无 schema 错误。部分模块允许无 presenter 的 state-only/headless 路径，执行注册存在不等于平台展示能力完整。
19. `GalNet.Primitives.Builtins.csproj` 当前同时引用 Core、Runtime 与 Presentation.Abstractions；schema/执行已在 API 上分离，但还没有独立的内置契约程序集。自定义契约项目若引用现有 BuiltinSchemas，会带上这些框架依赖，虽然不会因此初始化游戏后端。
20. `GalgroupCompiler` 消费 `IEntryCatalog`，展开 Composite 并校验生成的 primitive；`CompositeGameView.Dispatch` 再以相同 schema 规范化 arguments，将 definition、arguments、runtime、batchId 和取消 scope 传入 `PrimitiveCreateContext`。参数规则不应在执行模块中重新定义一份。
21. `docs/development.md` 已明确允许单平台执行模块直接捕获 Avalonia 页面服务、Skia renderer 或媒体后端，但该模块必须位于项目 View/客户端层；契约与框架内层不反向引用平台实现。

## 目标与边界

- 提供 UI 无关、可被 CLI 和后续 UI 共用的项目操作能力。
- CLI 负责命令行参数、输入输出和退出码；业务规则与项目数据操作留在共享操作层。
- 为命令发现、对象查询、诊断、编辑、校验和编译提供一致入口。
- 明确 entry 契约来源及其与实际运行能力的区别。
- 契约由约定目录中的约定名称 DLL 提供；开发者独立契约项目集中组合 schema，工具与游戏工程共用。补齐按组件及全部内置 schema 的静态构造能力。
- 游戏工程引用契约项目；契约项目可独立构建为工具使用的 DLL。CLI 与 Editor 都从当前内容项目加载契约，Editor 按项目隔离 catalog。
- 将内置 schema、参数数据类型、纯校验和 Composite 展开抽到独立项目，依赖 Core；内置执行项目引用该契约项目，不形成反向依赖。
- 支持开发者在单平台 View/客户端程序集直接实现自定义 primitive 并操作视图；公共工具仍可只加载其独立 schema，不强制构造或引用平台执行对象。
- 支持 AI 直接写入作者文件之后调用共享校验；CLI 写操作是工具能力，不是唯一编辑通道。校验应读取当前磁盘内容并报告诊断，不以加载/保存来静默修正或过滤内容。
- 首期以当前格式为准；旧编辑器 UI 的整体追赶属于后续工作。

## 候选命令能力清单

下表是需求讨论清单，命令名为示意，不是已确定的 CLI/API 设计，也不表示所有候选能力已纳入首期。

| 领域 | 候选操作 | 当前基础与缺口 |
| --- | --- | --- |
| 项目 | 新建、检查/读取、摘要、重命名、删除 | 已有创建与摘要；显示名重命名已有共享命令；删除缺失，目录改名需单独定义 |
| 身份与查询 | 项目内按资源 GUID、节点/条目稳定 ID 查对象，返回种类、所在文件、所属 Group 及内容 | 已有单节点、单 Group 查询；统一查询缺失；不增加项目 GUID |
| 引用 | 查询谁引用对象，删除/改名之前报告影响 | 部分资源/变量操作已有引用逻辑；跨领域覆盖程度需要实现阶段核实 |
| GalGroup | 列表、读取、新建、重命名、删除 | 现有 LinearGroup 节点可创建 Raw Group；需要更直接的入口及文件清理一致性 |
| Entry | 列表、读取、添加、删除、排序、修改类型/条件/参数 | 共享命令已有主要写操作；需跟进当前 JSON 参数、schema 校验与直接 CLI 可用性 |
| Graph | 节点/边查询、节点创建/删除/重命名/坐标、连接/断开、设入口 | 共享命令已有基础；应复用并检查图不变量 |
| 分支 | Choice 选项和 Condition 路由的增删改、排序 | 共享命令已有基础；应检查出口引用同步 |
| 变量 | Player/Save 定义查询、增删改、排序、默认值、引用查询 | 共享命令已有基础；改名更新引用语义需要清楚表达 |
| 资源 | GUID 查询、列表、导入、移动/重命名/删除、目录、metadata | 文件命令已有基础；需要当前资源 catalog、GUID 保持及引用诊断 |
| 项目设置 | 读取、修改、校验，包括语言、画面、存档及音轨配置 | settings patch 覆盖有限，需要与当前 ProjectSettings 对齐 |
| Entry 契约 | 列可用 typeId、查看 primitive/composite 类别及参数类型、默认值、约束 | 已有 schema 数据；CLI 查询和目标选择缺失 |
| 校验与输出 | validate、build、export | 已有入口；需要保持同一目标契约并输出对象/文件位置明确的诊断 |
| 自动化支撑 | 命令帮助/发现、JSON 输出、稳定退出码、批次、dry-run | 现有 CLI 有部分基础；文件操作与文档操作能力不一致 |
| 后续候选 | I18n 文本/人物表编辑、Gallery annotation 编辑、复制/迁移/修复项目、项目目录搬迁 | 与长期编辑器追赶相关，不默认塞入首期 |

建议优先收敛首期为：项目生命周期、项目内 ID 查询、GalGroup/Entry/Graph 操作、entry 契约查询及基础校验。现有变量、资源、build/export 能力保持可用；是否在本 feature 完整补齐设置、I18n/Gallery 等能力待用户确认。

用户补充后，CLI 校验、契约发现及查询工具的优先级提高；是否仍需要为每项写操作提供直接 CLI 命令待确认，不把此前候选清单当作必做的全套 CRUD。

## Entry 能力来源与已选方向

这部分记录需求发现和选择；具体接口、默认命名及扩展加载目标见 design.md。

- **内置契约**：直接使用 `BuiltinSchemas` 构造 catalog，无需启动 Engine。CLI 可查询 typeId、primitive/composite、参数的 JSON 表达类型、required、默认值、options/constraints，并补充编辑提示。
- **项目目标契约**：编辑器和 CLI 需要明确选择目标 profile。目标游戏宿主与工具侧应共享契约来源，避免 CLI 默认为全内置而错误宣称游戏可执行全部内置 entry。
- **自定义契约**：用户已选择独立契约项目和约定 DLL；它提供不依赖 UI/平台后端的 schema 集，CLI/UI 使用它，游戏宿主使用同一契约并另行注册执行模块。依赖部署和入口具体形式待后续设计明确。
- **纯数据描述**：若需要离线 JSON schema 快照，应从同一契约导出，并表达版本/来源；它适合查询与部分通用校验，不能完整承载自定义 CLR 校验器或 Composite 展开代码。是否支持写入/编译必须单独定义。
- **运行支持检查**：工具侧只拿到 schema 时只能保证目标声明的编辑/编译契约；实际运行能力仍需宿主注册检查或运行验收。不能通过创建一个空 Engine 来自动推断任意游戏的支持范围。
- **未知类型策略**：优先考虑保留原始数据、提供明确诊断，避免加载/保存时丢字段；未知 entry 的新增、修改、编译能力边界需确认。不把 schema 缺失当作所有项目基础查询都必须失败的理由。

### 注册信息如何到达工具进程：已选方向及待明确细节

- 单次命令和循环进程都需要显式契约来源；循环只改变生命周期与缓存，不能自动获知另一个游戏进程的注册集合。建议先保持单次调用，按项目配置重建契约。
- 用户选择固定目录、固定 DLL 名及集中静态 schema 集的方向，替代上一轮任意路径/profile 选择建议。确切命名待定。
- 集中静态集建议暴露不可变 `IEntryModule` 列表，复用当前 Primitive/Composite 两张表及校验器；这里的静态指进程内构造后稳定，不要求热更新。入口不创建运行时/平台资源。
- DLL 列表本身就是项目实际选用契约的集合，不扫描其他程序集并自动合并未选模块。内置方法可返回单个组件模块，汇总方法返回组件模块集合；重复完整 typeId 必须明确拒绝。
- CLI 每次从同一约定入口构造 catalog；Editor 打开项目时构造该项目的 catalog，切换项目不沿用上一个项目的集合。游戏工程通过项目引用使用同一契约入口，无需采用工具侧反射加载。
- 契约项目的独立“导出”就是构建并部署 DLL 及所需依赖，JSON schema 只作为可选查询产物。游戏发布按正常程序集依赖处理，不另设一份需要人工同步的 schema 数据源。
- 动态加载入口属于工具/文件系统外层，Core 编译器和 Runtime 消费 catalog/接口，不认识 DLL 路径或约定名称。动态加载不得让框架 Core 契约类型因重复装载成为不同类型身份。
- 从已选择的 catalog 导出带来源/版本标识的 JSON 描述，供 AI、帮助查询和 UI 描述消费；该描述是生成产物。带自定义校验或 Composite 的完整校验/编译仍以可执行工具契约为依据。
- 若只拿到 JSON 描述而没有对应代码，应区分结构/通用约束校验与完整语义校验；缺失代码不能静默降级后宣称完整成功。

### 表现层对接要求

- 契约只声明 typeId、参数、自定义校验和 Composite 展开；执行模块持有玩法/状态，宿主提供 presenter/平台后端。三者按 typeId 和明确端口连接，不把 presenter 工厂放入 schema DLL。
- 官方运行注册需要与选定 schema 对齐：可按对应组件显式注册，或提供按 catalog 注册所选官方指令的便利方法。执行模块共享状态和生命周期不能被逐指令注册方式破坏。
- 自定义 primitive 由宿主显式注册自定义执行模块。跨平台/可 Headless 执行的模块可依赖窄 presenter 接口；单平台模块也可在项目 View/客户端中直接引用具体页面、renderer 或后端。schema 名称或分类不应自动推断自定义表现代码。
- 建议游戏启动验证声明的 primitive 与最终执行注册一致；缺实现、无 schema 的实现、重复 ID 应有明确诊断。需要区分现有针对实际 Graph 的运行检查与拟新增的整个契约集完整性检查，工具侧不要求执行注册。
- 需要平台 presenter 的游戏能力应检查所需端口；显式 headless/state-only 模式可有不同要求，不能把空 presenter 默认为已实现视觉/媒体能力。
- Composite 只在工具编译时展开，表现层只接收展开后的 primitive。选择 Composite 时必须覆盖其所需 primitive；完整依赖表达方式待设计确定。

### 从契约到执行的完整链路与低耦合要求

1. 独立契约项目集中提供所选 schema、共享 typeId/参数数据类型、纯参数校验和 Composite 展开；不引用 Editor、游戏宿主或平台实现，不读取游戏运行状态。
2. 工具侧项目加载器读约定 DLL 并构造项目 catalog；游戏组合根通过项目引用调用同一契约入口。两者共享契约来源，构建/部署流程应避免工具 DLL 与游戏所引用契约发生版本漂移。
3. CLI/Editor 的项目读取、参数提示、校验和编译消费该 catalog；项目资源存在性、图引用、变量引用等校验由项目工具服务提供，不通过参数 schema 闭包捕获项目服务。
4. Raw Group 保存 typeId 与 JSON 参数；编译器对照契约校验并展开 Composite，生成只含 primitive 的 Compiled Group，输出不携带执行模块、presenter 或 CLR 类型名。
5. 游戏组合根注册契约和执行模块，注入实际 presenter/后端；执行模块可依赖契约中的共享 ID/数据类型，但契约不依赖执行模块。运行时注册覆盖检查与平台能力检查发生在游戏组合边界。
6. Engine 解释图和流程，经 IGameView 按 typeId 分发；CompositeGameView 复用契约规范化参数，执行工厂接收 PrimitiveCreateContext，创建对应 PrimitiveInstance。
7. PrimitiveInstance 执行逻辑、按既有不变量更新可恢复状态，再经 presenter 接口或单平台执行模块直接请求显示/播放；平台实现持有具体 UI/媒体资源。Composite 展开和 schema 查询不需要这些资源。
8. 游戏 scope 管理执行模块状态；宿主负责 Engine/view/presenter/后端释放顺序，契约的静态集合不持有运行会话对象。

用户已同意将内置 schema、参数 DTO、纯校验和 Composite 展开抽出为独立契约项目，依赖 Core；内置执行继续留在依赖 Runtime/Presentation.Abstractions 的现有边界，并引用内置契约。该物理拆分已纳入范围，尚未实施。

### 单平台执行模块的边界

- 独立 Contracts 只声明自定义 entry 的参数和 ID；具体 PrimitiveImplementation/PrimitiveInstance 可放在项目 View/客户端，捕获页面或平台服务，由宿主实例化并注册。
- 页面直接调用不要求框架先定义统一 presenter；开发者选择单平台耦合时，只让该执行实现依赖具体平台，不让契约 DLL、Core、Runtime 或 Editor.Shared 依赖它。
- UI 调用由平台执行实现自行调度到正确线程；动画/等待应遵守 completion、blocking、skip、取消与 PresentationCompletion 等已有实例协议，不以 fire-and-forget 代替完成语义。
- 会影响存档后持续画面的状态必须可恢复，并提供读档/快进重放路径；短暂效果应明确 CatchUp 是否跳过或应用末态。静态 schema 不能持有页面引用。
- Headless 工具仍能读取契约、校验和编译这些 entry；Headless 执行若没有对应实现，应明确失败，或由开发者显式提供替代实现，不静默跳过。

## 非目标

- 不修改 UI、Dock、ViewModel 交互或播放器预览。
- 不实现游戏 Engine 或媒体执行后端。
- 不默认引入长期 CLI server、跨进程 Undo/Redo 或后台项目服务。
- 不默认支持旧 Group 格式、旧存档或全盘项目搜索。
- 不默认重做资源协议、运行时 entry 注册或另建全局类型目录。

## 候选验收标准

待范围确认后收敛为实现阶段的正式验收标准。

1. 一个操作可由 CLI 和非 UI 测试调用同一共享实现，命令行适配不承载业务校验和持久化规则。
2. 可在无 Engine、无 Avalonia 初始化条件下创建项目、查询对象、编辑 Raw Group 与 entry、验证和编译内容。
3. 显示名称、目录路径、稳定 ID 的语义分开；重命名不意外改变身份，GUID 查询不会误把 Group 当成资源。
4. 新建/删除 Group 同时正确处理 Graph 引用和源文件；删除操作报告引用影响，并有清楚的失败行为。
5. Entry 类型查询和写入/编译使用同一选定 catalog；参数 JSON 类型、默认值与约束不会被仅 UI hint 替代。
6. 缺失扩展/不匹配目标有可定位诊断；读取、保存不得静默丢弃未知内容。
7. CLI 提供可自动化的结果与退出码；dry-run 和批次支持范围准确说明，失败写入保证通过对应测试验证。
8. 若纳入外部自定义 schema 支持，有至少一个无需平台后端的自定义 primitive 示例；若包含 Composite 编译，也有实际展开验收。
9. AI 直接修改文件后，CLI 可读取磁盘原始数据并校验；校验不写回、不删除未知字段，诊断可定位到文件与对象。
10. 部分内置组件、自定义 primitive 和 Composite 的契约组合可从约定 DLL 加载，CLI 与游戏侧使用一致的 typeId/参数集合；部分组件不会被全量默认注册意外扩充。
11. 同一个契约项目既可由游戏项目引用，也可独立部署给 CLI/Editor；Editor 打开两个支持集合不同的项目时不会串用 catalog。
12. 工具加载/校验/编译不创建执行模块或 presenter；执行注册复用同一参数契约，未把游戏宿主服务或 DLL 文件路径泄漏到 Core。
13. 一个单平台自定义 entry 的契约可由无 UI 工具加载，而执行实现可直接操作平台视图；跨层依赖与运行实例生命周期仍符合上述边界。
14. sample.screenshot 契约可由工具加载并编译；Avalonia Sample 实际捕获场景 PNG，宿主决定目录，取消/失败/文件冲突有诊断和清理，CatchUp 不重复写文件。
15. 删除项目将经过准确定位的项目根目录送入系统回收站；dry-run 不移动文件。回收站不可用/失败不会永久删除，也不误报成功；最近记录只在成功后按相应生命周期更新。

## 约束与假设

- 当前已完成设计与阶段计划；用户后续加入截图样例并要求继续 feature，不把最初候选列表当作全部已实现或已确认交付。
- 优先复用 Editor.Abstraction/Shared/Headless，并遵守 Core/Runtime 与 UI/文件系统分层。
- 表中“已有”来自静态代码调查，本次没有运行 CLI、构建或测试；不代表这些能力已完成针对新需求的验收。
- 对开放问题保留阻塞范围和设计默认假设，不把默认值写成用户亲自确认的决定；有问题的单项不得伪报完成。

## 开放问题

1. 契约目录/入口和新项目命名采用 design.md 默认值；用户如有现有命名约定可调整。
2. 当前优先共享校验/查询及必要生命周期工具，保留已有 execute；原始候选中的全面 CLI CRUD、I18n/Gallery/设置追赶留在后续。
3. 项目删除已确认采用系统回收站；与单独移除最近项目记录保持不同操作。
4. 项目重命名是否只改显示名？目录改名/移动是否进入首期？
5. 是否有必须纳入首期的额外工具能力？默认以 design.md 范围为准。
6. 未知 entry/字段在查询中保留并输出，语义校验/编译明确失败；涉及保存的工具必须先验证无损边界，不能按 schema 静默丢字段。
7. 表现层最小联调包含明确执行覆盖诊断与 Sample 截图；不增加通用 Editor 平台插件加载器。

## 相关链接

- [设计](design.md)
- [实施阶段](phase-plan.md)
- [Proposed ADR：项目 entry 契约程序集](../../docs/adr/0004-project-entry-contract-assembly.md)
- [文件格式](../../docs/spec/file-formats.md)
- [Entry 类型与 target profile](../../docs/spec/entry-types.md)
- [架构](../../docs/spec/architecture.md)
- [ADR-0002：Entry 契约与执行注册分离](../../docs/adr/0002-entry-schema-execution-registration.md)
- [扩展开发](../../docs/development.md)
- [已有 Editor 职责拆分](../F-20260929-10-editor-responsibility-split/feature.md)
- [已有编译管线 feature](../F-20260929-01-compiled-content-pipeline/feature.md)
- [Headless CLI](../../src/Editor/GalNet.Editor.Headless/Program.cs)
- [共享命令目录](../../src/Editor/GalNet.Editor.Shared/Commands/EditorCommandCatalog.cs)
- [内置 schema](../../src/Shared/GalNet.Primitives.Builtins/BuiltinSchemas.cs)
- [目标 entry catalog](../../src/Shared/GalNet.Core/Entry/TargetProfileEntryCatalog.cs)
