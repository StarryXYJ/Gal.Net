# 项目契约 DLL、共享工具与平台执行扩展

状态：设计完成，Phase 1、2 已验证。本文描述目标行为，验证状态见 phase-plan.md。

## 目标与范围

让每个内容项目显式提供自己的 entry 契约；CLI 和 Editor 使用该契约查询、校验及编译，游戏工程通过项目引用使用同一契约。AI 可以直接编辑作者文件，无需先经过命令写入。共享工具层承担校验、查询和必要操作，CLI 只做适配。

本 feature 包含内置契约程序集拆分、约定 DLL 加载、项目级 Editor catalog 接入、build/export 契约统一、项目内 ID 查询与无损校验、必要生命周期/Group 工具，以及单平台截图 entry 样例。不改 Editor 布局，不追赶所有 I18n/Gallery/设置 UI，不实现常驻 CLI server 或插件市场。

原始候选清单不是全套 CRUD 的强制交付表。已有编辑命令继续复用；本次重点是直接写文件后的可验证性、契约一致性和跨工具复用。

## 当前差距

- schema API 已独立，但内置 schema、Entry DTO、校验和执行仍位于同一 Builtins 程序集，带上 Runtime/Presentation.Abstractions 依赖。
- Headless 固定创建全量内置 catalog；Editor 的 catalog 及多个持有它的服务是 Singleton。
- build 可接收 catalog，但存在全内置默认值；export 内部再次创建全内置 catalog，无法确保自定义契约一致。
- 当前 Editor repository 先查 schema 再按已知参数筛选，不能充当无损磁盘校验入口。
- 官方组件执行入口已有，schema 缺少对应的组件入口；全量 AddBuiltins 会扩充项目所声明支持范围。
- GamePage 已提供 CaptureScenePng，截图对话框只适合人工交互，不能直接用作自动截图 entry。

## 契约项目与约定入口

以下具体名称作为本设计的默认实施约定；用户已确定固定目录/名称的方式，但尚未逐项指定字符串。

```text
ContentProject/
  Contracts/
    GalNet.Project.Contracts.dll
    <项目自有依赖 DLL 与可选 deps.json>
  settings.json
  Graph/graph.json
  Graph/groups/*.rawgalgroup
  Assets/
```

DLL 内固定入口为 `GalNet.Project.Contracts.ProjectSchemas`，暴露公共静态只读 `IReadOnlyList<IEntryModule> Modules` 属性。加载器只查这个入口，不扫描并合并全部模块。返回集合是该内容项目选择的完整契约，重复完整 typeId 失败，不按模块名字或前缀排他。

开发者独立契约项目设置 AssemblyName 为 GalNet.Project.Contracts；游戏工程 ProjectReference 到这个项目，普通构建/发布处理其程序集依赖。契约项目还可以独立构建并把产物部署到内容项目 Contracts/；不是另做一份手工 JSON schema。部署脚本应先构建到临时输出，成功后替换整个工具契约目录，避免依赖混用。工具与游戏使用同一构建版本。

Contracts/ 属于开发工具输入，不进入资源 GUID provider，也不作为游戏内容文件默认打入 .galpak；运行工程已通过正常程序集引用携带契约。CLI 可提供 JSON 描述输出给 agent，它是派生查询结果，不是第二个事实源。

缺少 DLL、入口不符、依赖缺失、框架版本不匹配或重复 ID 都输出独立诊断，不自动回退全内置目录。创建空内容项目时可以先生成目录与文档模板，但不声称已经可校验/编译；创建命令可接收已构建的契约部署目录。提供可独立构建的示例契约项目，解决首次建项目的引导问题。

## 新内置契约程序集

新建 `src/Shared/GalNet.Contracts.Builtins`，只引用 Core，命名空间使用 GalNet.Contracts.Builtins。迁移内置 schema、Entry 参数/ID 数据类型、纯校验和 Composite 展开；不能为保持旧命名空间而违反程序集所有权约定。

现有 GalNet.Primitives.Builtins 保留执行模块、PrimitiveInstance、RuntimeActions 和运行时场景重放，引用新契约程序集。所有消费者一次性更新命名空间和项目引用，不通过复制 schema 保留两个事实源。

BuiltinSchemas 提供对应执行领域的组件构造方法：Dialogue、Nvl、Layers、Animations、Audio、Video、Effects、Particles、Flow、Variables、Gallery。Transition 及 voice Composite 作为明确的作者组件，使用时必须同时包含其输出的 primitive。具体方法采用 Create<Domain>Module 与 CreateAllModules；根据用户补充，只返回独立模块列表，不提供合成大模块或内置专用 catalog 工厂。消费者使用通用 TargetProfileEntryCatalog 构造 catalog。

All 是组件合集；组件方法不隐式添加无关能力。当前内容实际展开时由编译器检查输出 primitive 是否在 catalog；本期不为任意 C# Composite 虚构可静态枚举的完整依赖图。

## 工具加载与项目上下文

动态加载位于 Editor.Shared 文件系统/工具层，Core 只消费 IEntryCatalog，不认识 DLL 路径。加载结果是一个可释放的 ProjectSchemaContext，拥有 catalog、来源标识及程序集加载上下文；catalog 构造不创建 Engine、执行模块或 presenter。

每个项目独立加载，避免两个同名 GalNet.Project.Contracts.dll 混用。Core 和内置契约类型与工具共享同一程序集身份；项目自有依赖在该项目上下文内解析。不因依赖目录存在另一份 Core 就让 IEntryModule 成为不兼容类型；版本不兼容应明确失败。

项目上下文采用可卸载加载上下文；关闭项目后先释放消费者，再释放上下文引用。卸载不承诺立刻触发 GC。首期不做文件监听、热更新或运行中替换契约，开发者重新打开项目/重新运行 CLI 使用新版本。

加载器处理约定根目录与依赖解析，消费者只拿 catalog 和结构化诊断。只读身份查询应能在契约缺失时读取原始磁盘数据；依赖类型语义的校验/编辑/编译明确报告缺失。

## Editor 接入与生命周期

Editor 打开项目时先加载契约、建立项目 scope，再解析依赖 catalog 的服务和页面；失败时清理未完成的 scope，不发布成功打开事件。无项目的启动页不需要 IEntryCatalog。

catalog、repository、save coordinator、持有 catalog 的编辑服务、文件命令 executor、项目内容 provider 和相关快捷键贡献按项目生命周期解析。全局 project service、导航、窗口、主题、设置服务可以继续 Singleton，但不得缓存上一个项目的 catalog。已有 EditorPageFactory 从 Current.Services 解析页面的路径可复用。

快捷键及类型选择基于当前 catalog 动态构造贡献集合，避免启动期对全内置定义逐项注册。只做数据来源与生命周期接入，不重新设计 UI。

预览仍由平台宿主明确注册运行实现；加载 schema DLL 不自动加载游戏平台代码。官方预览可为选定官方指令提供实现，自定义平台指令缺实现时明确诊断，不生成空成功实现。本期通过 Sample 截图联调验证单平台执行，而不增加通用 Editor 平台插件加载器。

## 查询、校验与共享操作

2026-10-13 用户要求复核 agent 命令面，命令主入口、help 机器描述、files/references、Graph/Group 定向校验、单 Group 编译和 Editor 单元操作边界详见 [CLI 与共享操作契约](cli-contract.md)。本节此前的扁平命令列表仅记录旧入口；按用户最新要求移除兼容别名，以新结构为准。

共享 ProjectTools 服务直接读取原始 JSON，保留未知类型和参数，身份查询不借用会生成 ID/筛选参数的 Editor repository。项目内 find 返回对象种类、文件、Group、稳定 ID 及内容，ID 可能跨种类/Group 出现，因此返回全部匹配并支持 kind/group 限定，不擅自猜测唯一结果。资源 GUID 根据当前 .meta 查询，节点/条目按稳定字符串 ID 查询，不新增项目 GUID。

校验按层返回诊断：JSON/格式版本、图/Group 结构及引用、选定 schema 与参数、Composite 编译结果，再到已有变量/资源等项目检查。未知 entry/字段报告原始位置，不删字段、不写回、不生成缺失 ID。结构合法不等于 schema 合法，schema 合法不等于平台实际可运行。

通用参数校验与编译复用 Core 契约逻辑，UI hint 只用于编辑提示。复杂 CLR 参数与自定义 C# validator 留在 DLL；查询 JSON 使用稳定 JSON 表达及描述，不将 CLR 类型名写回作者或编译内容。无法完整表达的类型应明确描述限制。

初期 CLI 表面保持现有 summary/nodes/entries/execute/build/export，新增 find、schema list/show、完整 validate 等直接工具入口。必要写操作复用共享实现：项目创建、显示名改名、Group 创建/改名/删除及引用影响检查。现有 Graph/Entry/变量/资源命令不为了 CLI 重新实现一份。

项目目录移动不等同显示名重命名。用户已确认项目删除将整个项目根目录送入系统回收站，与单独“移除最近记录”分开。共享操作通过文件系统外层的回收站适配执行，不让 Core 依赖系统 API；本地 Windows 提供对应实现。其他平台/位置不支持回收站或操作失败时明确报错，不回退永久删除，也不创建自有目录冒充系统回收站。

执行前解析并验证确切项目根路径；Editor 删除当前项目时先停止预览并释放项目 scope，避免打开的契约/资源阻止移动，关闭失败则中止。CLI 不假设自己能关闭另一进程中的项目。dry-run 只返回目标与影响，不关闭项目或移动目录；真正回收成功后才更新最近记录并报告成功。恢复由系统回收站提供，本期不新增独立恢复工具。

批次和 dry-run 准确表达支持边界，不能把每文件临时替换描述成跨文件原子事务。Group 文件写入与 Graph 引用变更需要失败恢复验收。不额外引入跨进程 history/server。

## 编译、导出与运行注册

CLI 一次加载项目契约，向 validate/build/export 传递同一 catalog。ProjectContentBuilder 的隐式全内置默认值应移除或改成明确的外层入口选择；GamePackageExporter 接收 catalog，不在内部重新建立全内置集合。Editor export 同样使用当前项目上下文。

游戏组合根调用 ProjectSchemas.Modules，独立注册执行模块并传入 presenter/后端。框架提供明确的注册覆盖校验：重复/无 schema 的实现失败，游戏启动检查实际编译内容需要的 primitive；宿主也可要求整个声明集合的执行覆盖。工具侧无执行模块不失败。Headless 因此可使用包含平台专属 schema 的共同契约，运行不涉及这些指令的内容；实际 Graph 仍保留 Engine 已有执行检查。

官方便利入口应按选定官方 schema 注册，不通过分类或前缀推断任意自定义实现。组件级手动注册是基线；若需要只选组件部分指令，路由筛选必须保留整个执行模块的共享状态和释放所有权。自定义实现或替换同 ID 的实现由宿主显式选择，重复不静默覆盖。

平台 presenter 的必需性由执行组件/宿主游戏模式校验，不能写入 schema 对具体视图类型的引用。Headless/state-only 是明确模式，不能把 null presenter 当作一般游戏能力已具备。

## 单平台截图 entry 样例

用户已确认自动保存场景 PNG 到宿主截图目录，不弹出截图对话框。

- 新增独立 Sample 契约项目，程序集产物符合上述约定；引用 Core/内置契约，组合内置集合和 `sample.screenshot` primitive。
- 截图不是全内置指令。其执行模块与实例位于 Sample.Avalonia，直接接收 GamePage 和宿主截图目录，不强制新增跨平台 IScreenshotPresenter。
- 首期参数为可选 `fileName`（单个 .png 文件名，缺省自动生成不冲突名称）。不接收任意输出根路径、不捕获全桌面、不包含玩家 UI；根目录由宿主决定，默认复用已有截图服务的 Pictures/<游戏名> 目录约定。
- 在 UI 线程等待有效布局后调用 GamePage.CaptureScenePng；使用已存在的 scene 渲染入口，不从运行状态另画一份。布局未就绪或尺寸无效应等待/明确失败，不能输出伪成功图片。
- PNG 捕获完成后异步写文件；blocking=true、skippable=false，成功、取消、失败均完成/清理。错误进入现有游戏诊断链，不能捕获后静默成功；PresentationCompletion 覆盖异步清理。
- CatchUp 不截图、不产生新文件；Quick 正常执行一次。截图本身不持久化运行对象，也不新增 SceneState 字段。普通读档重放不重复制造副作用。
- 文件名必须留在宿主截图根内，默认不覆盖已有文件，临时文件完成后再发布；失败/取消清理临时文件。重复执行显式文件名报冲突，而不意外覆盖。
- Sample.Avalonia 引用契约项目并注册截图执行模块。Sample.Headless 可校验/编译该 schema，但运行含截图指令的图若没有实现则明确报错；首期不提供伪截图实现。
- 在独立示例 Group/测试 fixture 中演示，避免把平台专属指令无条件加入双方共用的默认示例流程而破坏 Headless smoke。

## 方案取舍

| 候选 | 结论 |
| --- | --- |
| 常驻 CLI 保存游戏注册状态 | 不采用；状态来源仍需契约，且增加生命周期/过期问题 |
| 手工 JSON schema 作为唯一来源 | 不采用；无法完整复用 C# 校验和 Composite，易与宿主漂移 |
| 扫描所有 DLL 的模块并合并 | 不采用；会把未选择能力加入支持集合 |
| 固定项目契约 DLL + 静态集合 | 采用；契约源清楚，游戏项目引用，工具可独立加载 |
| 契约工厂同时创建执行/presenter | 不采用；工具将被平台和运行资源耦合 |
| 单平台执行必须包接口 | 不强制；直接依赖留在 Sample/View/客户端外层 |
| 缺契约回退内置、缺执行静默跳过 | 不采用；掩盖实际支持范围与不一致 |

## 迁移、风险与验证影响

- 新程序集与命名空间迁移影响执行、Sample、Editor 和测试引用；更新 solution、架构 allowlist、lock 图及开发文档。机械迁移与后续加载行为分 Phase 验证。
- 使用契约 DLL 后，既有内容项目需要构建/部署契约；Sample 脚本和测试 fixtures 同步提供它，不靠兼容 fallback 掩盖缺失。
- 加载隔离需要证明两个同名契约不同集合不串用；重复 Core 身份、依赖缺失及版本冲突要有负面测试。
- repository 的有损加载与真正磁盘校验分开；后续允许编辑未知内容前必须验证保存不丢字段，不能只测 validate。
- export 必须用自定义 schema fixture 验证 Raw→Compiled→包，而不是只跑全部内置示例。
- 截图使用真实 Avalonia 场景验收 PNG 签名、尺寸及可辨认像素；再覆盖取消、冲突、CatchUp 和布局时序。无 GUI/渲染环境时明确记录限制，不以 stub 替代真实截图成功。
- 校验、schema 加载和编译不引用 Avalonia；单平台运行可依赖它。用架构测试保护这两个方向。
- 本设计不保证任意旧契约 DLL 二进制兼容；契约项目和工具应针对同一框架版本构建。目录/入口命名与装载 API 应在实现前集中为常量，避免散落。

## 相关记录

- [需求](feature.md)
- [阶段计划](phase-plan.md)
- [ADR-0002](../../docs/adr/0002-entry-schema-execution-registration.md)
- [开发文档](../../docs/development.md)

### 项目回收的外层边界

项目删除由 Shared 的 ProjectDeleteService 识别当前 Graph/settings 项目根并协调回调；IProjectRecycleBin 位于 Editor.Abstraction。新增 Infrastructure/GalNet.ProjectTools.Windows，只引用该抽象，CLI 和 Editor 组合根注入适配。Core/Runtime/Shared 不引用 Windows Shell。当前仅支持 Windows 8+ 本地固定磁盘；Shell 使用 FOFX_RECYCLEONDELETE 和失败即停标志，PreDeleteItem 拒绝没有回收标志的删除，检查逐项结果和中止状态，不实现永久删除 fallback。CLI 无需契约 DLL 即可识别并回收骨架项目。

接口依据：[IFileOperation flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)、[PreDeleteItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-predeleteitem)。实物验证在通过后记录。
