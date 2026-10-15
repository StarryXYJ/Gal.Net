# Editor 创作工作区实施计划

日期：2026-10-15；进度更新：2026-10-16。依赖：[需求](feature.md)、[设计](design.md)。用户已授权实施，P1a 已验证，见 [phase-1a.md](phase-1a.md)；P1b 已完成当前工作树集成验证，构建成功、311 项测试通过，见 [phase-1b.md](phase-1b.md)。P2 已验证，当前工作树构建成功、323 项回归通过，见 [phase-2.md](phase-2.md)。下一阶段 P3，其余 Phase 为 planned。状态只使用 planned / in-progress / verified / skipped；状态更新必须附实际证据。

## 顺序与范围

```text
P1a 基本页面/项目文档 → P1b 项目历史/保存骨架
  → P2 Graph 基础 → P3 Graph 完整交互
  → P4a 文件事务/文档协调 → P4b 项目文件界面
  → P5 galgroup 核心与最小参数表单
  → P6 自动生成 UI 完善
  → P7 快照编译/整体验收/收尾

后续独立 feature：游戏预览（本计划没有隐含 P8 实施）
```

| Phase | 状态 | 主要可交付结果 | 需求验收对应 |
| --- | --- | --- | --- |
| P1a | verified | 新建/打开项目和独立文档/页面生命周期；默认契约自动部署；隔离基线累计 273 项测试通过 | 1、3 |
| P1b | verified | 项目统一历史、保存、失败补偿及恢复骨架；当前工作树构建成功，311 项回归通过 | 2、3、6、7 |
| P2 | verified | 基本节点与连线编辑，Group 最小生命周期；323 项回归通过 | 1、4 |
| P3 | planned | 分支、批量编辑、复制粘贴、快捷键 | 2、4 |
| P4a | planned | 项目文件事务和未保存文档协调 | 5、6、7 |
| P4b | planned | 项目文件树与实际文件操作体验 | 1、2、5 |
| P5 | planned | 多 Group 编辑、变量和基本参数 UI | 1、2、3、8 |
| P6 | planned | 完整 schema/复杂值/专属编辑器接入 | 8、9 |
| P7 | planned | 当前快照校验编译与跨功能真实验收 | 1–10 |

阶段边界可因最小闭环调整，但不能跳过行为验证或把未完项藏到“后续优化”。每阶段在本目录新增 `phase-<id>.md` 记录改动、执行命令、证据和偏差；目录不是自动生成空模板的要求。

## 通用实施规则

- 用户已在 Phase Plan 之后明确授权开始实施；一次推进一个可验证 Phase，保护同工作区其他任务改动。
- 每次接入一个领域，同步移除该领域旧写入口及重复历史；不等最后阶段才尝试收敛两套作者状态。
- 可有阶段性未接通的后续页面，禁用并标明；不得通过旧模型 fallback 假装完成。基本壳不自动启动游戏预览。
- 大规模纯移动/改名与行为变更分开；不机械追求拆类/拆项目数，不默认改 Runtime 公共协议。
- 变更仅操作 feature 自有内容；Core/Runtime 的 modular-game-state 重构属于独立任务。基线异常、环境限制与本次回归分开记录。
- 文件发布、撤销重做与恢复失败必须有故障注入；测试不能只证明正常路径。
- UI 测试必须挂载主题与真实视觉树、实际提交事件。使用显式 `Dispatch<bool>` 等待异步结果，UI 回调直接 await/try-catch，不阻塞 UI pump。
- 各阶段只运行受影响测试；最终才做跨边界回归。多个依赖同一 obj 的 dotnet 进程不并行。

## P1a：基本页面、作者文档与项目会话

状态：verified。隔离提交 a674feef 加本次 Editor 文件，构建成功、累计 273 项测试通过；后续 P1b/P2 已在当前工作树完成集成验证。证据：[phase-1a.md](phase-1a.md)。design 6.2 的文件立即写盘仍为后续 P4 的设计假设，本阶段未开放文件移动/重命名。

**目标**：建立能实际打开/关闭的工作区，并让内容与页面生命期分离。

**模块**：Editor.Abstraction 的 Project/Documents；Editor.Shared 的 ProjectService、repository/schema；Editor 的组合根、Startup/NewProject/EditorPage、Dock、Inspector。

**任务**：

1. 记录现状构建/测试基线与作者消费者清单；形成旧服务保留/改造/删除表，检查 singleton/scoped 的当前文档所有权。
2. 建立项目会话、文档身份/路径索引和 Graph/Group/settings 作者模型。Group 文档与 GraphNode ViewModel 解耦；Graph 变量定义为权威，settings 中兼容当前消费者所需字段由序列化派生，不再独立编辑。
3. 明确 Raw JSON 参数的中立表示与 codec；先完成加载/保存数据模型适配，不要求完整 Group 页面。保护 AudioTracks、Speakers 和其他配置字段完整性。
4. 接通契约目录选择、创建、打开、最近项目、失败信息与关闭取消；项目打开失败保持错误，不生成样例图覆盖。
5. 页面参数改用稳定文档/对象身份；关闭/重开、多视图、Inspector 锁定目标和项目切换有明确释放路径。
6. 工作区只注册当前阶段可用入口，停止默认构造游戏预览。保留当前主题/本地化方式，不并行重做视觉设计系统。

**验证**：新建临时项目→部署契约→打开；缺失/损坏契约；完整配置与 Raw JSON round-trip；相同文档多视图一致；关闭最后页面不卸载作者内容；两项目切换无订阅泄漏。挂载基本页面验证输入和错误信息。

**文档**：阶段记录及模块迁移表；已验证后更新 architecture/file-formats 中实际变化，不提前描述后续能力。

**风险**：旧全局文档服务和 GroupNode 持有内容造成双真源；类型切换时 JSON 生命周期。**退出**：基本入口可用、会话唯一拥有作者数据、页与文档身份分离，当前可读写格式及消费者明确，相关构建和测试通过。

## P1b：项目历史与保存的最小纵向闭环

状态：verified。前置：P1a。当前工作树构建 0 错误，Editor 75、Shared 186、架构 38、内容集成 12 项测试全部通过。原生 IME 人工验收与快照还原界面尚未提供，旧 Graph 记录按 P2/P3 迁移，完整边界与证据见 [phase-1b.md](phase-1b.md)。

**目标**：用“项目显示名/设置修改→撤销→重做→保存→重新打开”证明基础可靠。

**模块**：Shared 编辑/历史/持久化；Editor HistoryUiCommands、Shortcut、字段 binding、关闭流程、ProjectSaveScheduler。

**任务**：

1. 实现项目单栈、局部值/集合与复合编辑、异步串行执行、成功后更新游标、重入防护和故障补偿；记录只引用项目数据/ID。
2. 以显示名和基本项目设置作为实际用户操作切片，替换该领域 Settings/UI 分栈及修改后监听记账。领域命令如继续保留，走相同编辑规则。
3. 实现输入手势、IME/取消和文本控件本地撤销路由，保存切断合并；项目菜单显示编辑描述与目标。
4. 保存全部的版本捕获、外部基线、所有设置与作者内容一次计划发布；dirty 使用实际保存内容，保存失败不标记成功。
5. 统一自动保存与关闭排队/取消，建立 `.galnet/editor/`、恢复 manifest、30 秒/3 份快照、200 步/1 GiB 软预算的可配置内部策略及引用清理机制。提供 Group 创建/删除所需的最小作者文件计划和工作版本/基线双状态结构变换，P4a 再扩展任意受支持文件，不在 P2 临时写第二套文件操作。
6. 发生补偿失败进入 RepairRequired，保留材料；初期通过故障注入验证，不依赖真实磁盘损坏。

**验证**：修改→保存→修改→撤销回保存内容；撤销后新编辑截断 redo；保存中继续编辑；保存后不跨边界合并；复合中间失败/Undo 失败/Redo 失败游标保持；补偿失败；关闭取消与关闭等待；恢复快照完整性及清理引用。

**文档**：阶段记录与用户可见保存/撤销语义；架构和恢复目录事实按实际实现同步。

**风险**：以栈位置代替磁盘基线、异步 deadlock、历史持有 ViewModel。**退出**：真实 UI 切片和故障用例通过，关闭页面仍可撤销，保存/重开一致；后续 Graph 与文件可以复用同一协调者。

## P2：Graph 基础操作与 Group 最小生命周期

状态：verified。前置：P1a、P1b。当前工作树构建 0 错误，Editor 85、Shared 188、架构 38、内容集成 12 项通过。实施与边界见 [phase-2.md](phase-2.md)。

**目标**：完成不依赖 Group 编辑页面的剧情结构编辑。

**模块**：Graph 文档/序列化、GraphEditingService、画布控制、Workspace 投影、文档库、项目历史。

**任务**：节点增删改、入口、连线/断开、选择/框选、平移缩放和拖拽预览；多个前驱汇合；Group 空源创建/复合删除；页面对象删除状态。显示名不推导路径，加载不自动修复或静默丢边。替换旧 Graph mutation/history/property tracker 对应路径。

**验证**：创建三节点并保存重开；替换输出边的完整撤销；两个前驱共用目标；取消拖拽无 dirty/历史；多节点移动记一步；删除 Group 一次恢复节点、边、未保存内容及已存在源文件；删除对象后关闭/重开页面并撤销。

**文档**：阶段记录、Graph 不变量和支持列表；修改正式图规则时同步 validator/CLI/fixture。

**风险**：沿用现有输入唯一性造成汇合边丢失，节点 ID/路径混用。**退出**：Graph 基本创作可用且完整 round-trip，主要操作均进项目历史，视图状态不进内容栈；Group 有独立文档身份。

## P3：分支、批量操作与 Graph 完整交互

状态：planned。前置：P2。

**目标**：支持有分支的真实剧情结构和批量编辑。

**模块**：Graph 选项/条件、连接器投影、Clipboard、快捷键/上下文菜单、Inspector。

**任务**：选项/条件增删改和重排，连接器由稳定选项身份定位并在保存时映射 outlet；节点多选批量删除、Graph 深复制粘贴、ID/内部引用重映射；定位/居中、键盘操作和跨页历史目标展示。

**验证**：选项移动后各路边仍指向原目标；删除中间选项/条件再撤销；复制 Group 包含作者条目且新 ID 无碰撞；只复制内部边；批量操作中途失败全体补偿；焦点在 Inspector、画布和文本框时 Ctrl+Z 路由正确。

**文档**：阶段记录及 Graph 操作/复制/引用规则，更新支持矩阵。

**风险**：根据位置或显示名重建身份；未知自定义引用被全局替换。**退出**：完整分支图可编辑、保存、重开，批量操作一次撤销；未知引用边界可见，旧分支直接改 VM 路径移除。

## P4a：文件事务、文档路径与引用协调

状态：planned。前置：P3；按 design 6.2 的立即写盘设计假设推进，若用户调整先修订。P2 的最小 Group 文件能力在此统一，不能再形成另一套事务实现。

**目标**：先证明文件操作和 dirty 文档一致，再开放完整文件界面。

**模块**：Shared 项目文件计划/补偿/引用索引/持久化；现有 AssetFileCommandExecutor、ProjectAuthorFileTransaction、metadata codec；文档库。

**任务**：

1. 按设计操作矩阵实现文件分类、项目相对路径和原/目标预检、大小写改名、目录批量及父子项折叠。
2. 统一创建、导入/复制、改名、移动、恢复区删除的正向/反向计划，协调 metadata 与项目索引。普通文件字节保留；资源复制新 GUID，移动保留 GUID。
3. 工作版本引用索引覆盖已打开/未打开文档；明确结构化引用与候选字符串。Group 文件操作更新 Graph source path，I18n 目录规则在预检中执行。
4. 立即发布文件结构操作：对工作版本 W 与磁盘基线 B 独立应用已知结构变换，仅将 B 的变化落盘；处理未保存内容、文档路径/标题和文件监听。Undo/Redo 对当时基线应用局部逆/正变换，不覆盖期间保存的无关字段；文件操作不借机保存用户无关草稿，不以读取磁盘覆盖内存。
5. 增加提交、撤销、重做各阶段的故障注入；恢复 manifest 与备份引用接入 P1b 清理策略。
6. CLI/文件命令与 GUI 共用必要规则，删除旧 Assets 专用执行中的重复事务；CLI 不获得 GUI 会话历史承诺。

**验证**：dirty Group 改名→保存→撤销→重做；撤销改名不能丢掉期间已保存文本；改名不保存其他字段；放弃未保存内容不撤销已落盘文件操作；目录移动并恢复全部 metadata；仅大小写改名；导入后删除外部源仍可重做；目标被外部文件占用时拒绝覆盖；提交及 Undo 中段 I/O 失败；metadata 扩展/Gallery 保留；引用存在于未打开文件；未完成 manifest 重开核对；补偿失败保留材料。

**文档**：阶段记录、实际文件事务/恢复语义与目录支持矩阵；file-formats/assets/CLI 帮助按受影响范围更新。

**风险**：文件已经变动而作者引用/基线未变，文件备份过早淘汰。**退出**：故障矩阵通过；磁盘、工作内容、持久化基线和历史对同一次结构操作一致；大文件不要求内存整份复制。

## P4b：项目文件树与文件编辑入口

状态：planned。前置：P4a。

**目标**：让用户在 GUI 实际完成项目内部文件操作。

**模块**：ProjectFiles/现有 Assets 面板、路径导航/拖放、文件页、Inspector、文件对话框和定位服务。

**任务**：项目根文件树、过滤/导航、多选、右键/快捷键/拖放；改名、移动、复制/粘贴、导入、创建和删除反馈；文件与 meta 组合显示；固定路径和内部目录禁用原因；通用文本页编辑/保存/撤销及二进制信息/外部打开。外部修改提示不覆盖 dirty 内容。

**验证**：挂载文件树触发真实改名/拖放/删除；在打开页面中移动文件后标题与保存路径更新；关闭页面再撤销；UTF 编码/BOM/换行 round-trip，无法无损识别时只读；跨类别非法移动、Contracts/内部目录禁用及目录批量冲突。原生系统文件对话框/外部打开单列人工证据。

**文档**：阶段记录、文件操作支持清单和外部修改行为。

**风险**：树使用磁盘路径作为永久页面身份；通用文本页被扩大成完整 IDE。**退出**：用户可以实际操作项目内资源及普通文件，支持范围清楚，所有内容文件操作都使用 P4a 的同一事务/历史。

## P5：galgroup 多文档编辑与最小参数 UI

状态：planned。前置：P4b。

**目标**：完成可保存、可编译检查的剧情条目创作。

**模块**：GroupEditorPanel、Entry 作者模型/投影、文档库、Inspector、变量定义、类型列表、现有 SchemaForm/binding。

**任务**：多 Group 打开/定位；Entry 增删、插入、重排、多选、复制粘贴；type/condition 编辑；变量定义和补全；基本标量/枚举参数表单。切换类型完整保留撤销数据，不默认用 catalog 第一项创建条目；复制建立新稳定 ID。删除旧 GraphNode.Entries 真源和 VM 参数字符串同步路径。

**验证**：两个页面交替编辑再按项目顺序撤销；条目换类型→保存→撤销完整恢复；关闭页面重开内容保留；变量和条件补全刷新；条目重排不改作者 ID；必填未完成内容可保存重开，完整编译仍拒绝；已知和未知 Entry 数据往返无丢失。

**文档**：阶段记录、Group 文档与 Entry 值/身份规则，更新 entry-types/file-formats 的已验证行为。

**风险**：把完整 Normalize 当字段提交校验；条目序号混同稳定 ID；Group 页订阅未释放。**退出**：可完成真实 Group 条目编辑，标量参数无需额外页面，历史/保存无第二真源。

## P6：自动生成 UI 与复杂值编辑

状态：planned。前置：P5；复用现有参数/表单机制，不重做 DTO generator。

**目标**：让项目契约能够直接获得可用的基础编辑 UI。

**模块**：Avalonia.Controls Forms 的必要扩展、Editor 字段 registry/binding、专属编辑器、诊断与 lease。

**任务**：嵌套对象、列表元素/顺序、nullable/optional、默认值提示及显式重置；资源/表达式/多行/补全专属 EditorId；不支持节点的明确 JSON 回退；partial/full 校验分离；动态选项与 schema 失效拒绝过期提交；按展开创建字段并释放控件订阅。

**验证**：真实视觉树中编辑 string/enum/object/array/缺省/null/default；复杂值编辑后保存重开及撤销；粒子参数按不同字段顺序均可创作，完整编译仍严格；资源 GUID/轨道等动态选项刷新；换类型或删除 Entry 后晚提交被拒绝；未知字段原样保存；长列表与多次开关面板不持续增加控件/订阅。

**文档**：阶段记录、开发指南中的 EditorId/绑定扩展方式，区分通用控件与 Editor 业务行为。

**风险**：单纯创建工厂对象便认为表单可用；自动填默认值改变作者内容；递归 schema 无限展开。**退出**：代表性 built-in 和自定义契约在可见 UI 内完成字段编辑，缺省语义和撤销正确，复杂或不支持内容有可用反馈。

## P7：快照编译、前期整体验收与收尾

状态：planned。前置：P1a–P6 verified；若有 skipped 必须有需求范围依据，不以跳过失败测试替代。

**目标**：证明前期编辑器独立可用，留下后续游戏预览的清晰接点。

**模块**：Shared 快照/校验/GroupCompile/ProjectContentBuilder、CLI、Problems/定位、恢复与工作区清理；相关测试和正式文档。

**任务**：

1. 编译入口消费一致作者快照，包括 dirty Graph/Group/settings/I18n 与资源清单；CLI 捕获磁盘快照再复用。结果带 revision、作者 ID 与 source map，旧结果标过期。
2. Problems 支持字段/Entry/节点/文件定位；路径变动后依然可达。编译失败不写作者文件、不替换成功产物。
3. 验证保存、自动保存、外部冲突、恢复快照、历史预算/备份清理及关闭流程。保留既有导出消费者的构建接通，不扩展发布产品功能。
4. 清除剩余旧作者真源、分栈和失效服务；核对 DI、线程、项目引用及 CLI/sample fixture 的实际兼容范围。
5. 代表性性能 fixture：约 500 节点、100 个 Group/合计 20,000 Entry、单 Group 2,000 Entry、10,000 项目文件，另含超预算单文件；可分组合成，不把全部测试都跑大样本。记录打开、文件枚举、滚动、撤销与保存的耗时/峰值内存，按 P1 实测设阈值并记录环境，不虚构当前性能承诺。
6. 独立审核可用同一 agent 的独立审查轮次；本计划不默认授权/要求子 agent。问题修正后补必要验证，形成 review.md 和 summary.md，正式文档只同步已验证事实。

**真实工作流验收**：新建含契约项目→导入资源→创建汇合/分支 Graph→编辑两个 Group 与复杂参数→改名/移动其中一个 dirty Group 及资源目录→跨页面撤销/重做→保存→关闭/重开→检查/编译→检查产物和定位。另走一次故障/恢复流程，证明错误反馈与材料可用。此流程不启动游戏预览。

**测试与构建**：Shared/Editor/Architecture/Integration 必跑；表单变化跑 Presentation/Core/SchemaGenerator 的受影响用例；Assets/Storage/Runtime/Builtins 按实际消费者变化追加。最终覆盖所有变更边界，执行 solution build；平台 SDK/原生媒体限制如实单列。检查 whitespace/IDE0005。CLI 使用新建临时项目验证查询、编辑、校验、compile/build 与现有 export，不依赖旧数据迁移。

**文档**：architecture、file-formats、assets、entry-types、dock-design、development 与 CLI README 按实际影响同步；summary 记录偏差、删除路径和预览后续边界；agent lesson 经验证后另存，不将私人经验当 spec。

**风险**：单元测试全绿但跨页面文件保存不一致，或用旧快照编译掩盖错误。**退出**：feature 十项验收逐项有证据；真实 GUI/文件产物检查完成或明确列出未完成项且 feature 不宣告完成；游戏预览未混入本次范围。

## 验证命令参考（P1a 实际结果见阶段记录）

PowerShell 环境先设置 `AVALONIA_TELEMETRY_OPTOUT=1`。按实际需要 locked restore，以下命令顺序运行，构建期间保持源码快照稳定：

```powershell
dotnet restore test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --locked-mode
dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test test/GalNet.Editor.Tests/GalNet.Editor.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test test/GalNet.Architecture.Tests/GalNet.Architecture.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false
```

其他测试项目也需先恢复；运行中 Editor 锁输出时使用 feature 专用输出目录，不能关闭用户应用或把环境锁误记为业务失败。参考 [构建环境](../../.agents/lessons/L-20260922-01-dotnet-build-sandbox.md)、[异步 UI 断言](../../.agents/lessons/L-20261015-02-ui-tests-await-failures.md)、[实际挂载表单](../../.agents/lessons/L-20261014-03-form-tests-must-mount-editors.md)。

## 计划变更记录

- 2026-10-15：按用户顺序形成初始计划；基本页面拆为项目文档与历史保存两个纵向切片，文件操作拆为协调事务和界面两个阶段。游戏预览移出本 feature；未开始实施。
- 2026-10-15：用户授权实施，开始 P1a。Core/Runtime 的独立并发重构使工作树暂时不能整体构建，采用提交 a674feef 加本次 Editor 文件的隔离验证快照；不通过修改游戏代码消除他项中间态错误。
- 2026-10-15：用户纠正创建入口：不能要求用户准备契约 DLL。P1a 补充 Editor 自带默认契约及自动部署，自定义目录可选；打开已有项目仍严格加载其自身契约。用户要求每次完成后提交 Git。
