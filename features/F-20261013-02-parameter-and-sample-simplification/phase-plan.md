# Phase Plan

2026-10-14 Phase 4 已验证：PrimitiveImplementation.Immediate / Immediate<T> 简化同步工厂，轨道操作与 wait 使用唯一 DTO 参数声明，复杂校验/Composite/异步实例保留手写扩展。307 个不重复回归用例通过；五条指令迁移前后 CLI 描述相同，独立契约部署与实际项目编译通过。证据见 [phase-4.md](phase-4.md)。当前无阻断项，下一阶段为 Phase 4b 的异步机制与清理。

2026-10-14 最新收敛决定：DTO 成员特性生成有序 schema 与成员访问表，公共 DTO 适配器连接表单和宿主提交；新增 Phase 2d 位于 Phase 2c 最小验证之后。源码生成器已确认为实施目标，替代下文旧的候选/条件表述。范围、通知/默认值边界及退出条件以 [DTO 收敛计划](dto-convergence-plan.md) 为准；Phase 2c 最小宿主接入已验证（241 项回归），见 [阶段记录](phase-2c.md)；Phase 2d 单 Entry DTO 生成/绑定试点已验证（348 项回归、独立契约部署与实际 CLI 编译），见 [phase-2d.md](phase-2d.md)。Phase 4 已验证，下一步为 Phase 4b；全量设置 DTO 化仍暂缓，进一步作者工具化由 Phase 5 处理。

状态：in-progress。Phase 0/1/2a/2b/2c/2d/2e/3/4 已验证；中立值工具、共享表单、DTO 生成/强类型执行和 Sample 组合已落地，代表作者路径已验证，异步/清理与进一步工具化仍待后续。

顺序：Phase 0 → 1 → 2a → 2b → 2c → 2d → 2e → 3 → 4 → 4b → 5 → 6。Phase 4 的主线是降低开发新 Entry 的手写样板，并建立端到端作者路径；Phase 4b 单独处理异步生命周期和清理；Phase 5 根据实测继续优化作者 API、参数/工厂胶水与开发工具。DTO generator 已在 Phase 2d 落地，Phase 5 从现有工具链继续评估具体改进收益。4b 若收益不足可跳过，不阻塞 Phase 5；各阶段仍可按独立验证结果缩小范围。

每个实施 Phase 完成必要验证后单独提交；不提交其他任务的变更。记录状态 planned / in-progress / verified / skipped；跳过时写明原因及后续依赖如何调整。大规模重命名、格式化和功能行为变化不混入重构提交。

## Phase 0：工作量与代表性基线

状态：verified；相对工作量：小。证据与候选处理结论见 [baseline.md](baseline.md)：参数 3、CLI/契约 21、截图 14、注册 7，合计 45 项定向测试通过；不代表全解决方案回归。试点选择截图与 files 分页，已有工作区改动保留。

- 目标：确认优化针对的真实重复，给出可接受的迁移范围。
- 前置：用户授权实施；读取当前工作区和相关最新代码。
- 模块：Core 参数与变量、Contracts.Builtins、CLI 操作定义/解析、Sample 生命周期和截图。
- 任务：选截图及一个有数字/默认值的 CLI 命令；追踪现有描述、解析、校验和帮助链路；记录维护入口及重复规则。区分旧 Entry 编辑值、Raw JSON 和编译参数的表示，不把重构变成内容格式迁移。检查目标测试基线。
- 扩展调查：列出 Sample 各 service 的职责、状态源、所有权、转发链和共同消费者，覆盖启动/设置/本地化/显示/存档/调试而非仅生命周期五文件。绘制 Entry 从契约到执行的基础依赖，覆盖内置与 Sample，按立即完成、异步、交互/skip、持久场景等分类。每个候选给出合并/复用/保留/暂缓结论和成本，禁止假定所有相似类均冗余。
- 验证：运行代表性参数/CLI/截图/注册测试；记录已有失败和环境限制。验证不足不宣称基线通过。
- 文档：新增实施日志，记录基线、相对成本与计划调整；不创建无独立价值的微型框架。
- 风险：只数文件或行数，忽略业务差异；把已完成的变量优化再次列为任务。
- 退出：试点覆盖、暂缓清单及收益指标明确，现有行为有基线；发现成本明显过大时可直接缩小后续方案。

## Phase 1：公共参数基础的垂直试点

状态：verified；依赖 Phase 0；相对工作量：中。结果与成本见 [phase-1.md](phase-1.md)：Core 85、Shared 169、Builtins 69、截图 14、Architecture 31，共 368 项通过，改动文件格式/import 检查完成。EditorHints 与有序字段描述由 Phase 2a 继续实施，不宣称已具备 UI/生成器能力。

- 目标：抽出所有动态值 schema 可用的序列化/反序列化与校验工具，验证消费者能共用中立值描述；不统一完整业务模型。
- 模块：Core 参数基础、Entry 规范化、Editor.Shared 的 CLI 绑定、截图契约和一个代表性命令。
- 任务：演进现有参数描述/校验，建立 JSON 与命令行文本适配；试点接入真实执行和帮助输出。省略/null/default 分离；基础约束单一来源；业务校验仍留在业务层。优先复用现有 DTO/JSON schema 能力。
- 设计修订：拆开 ValueDescriptor、EditorHints 与 Entry/CLI/Setting 字段使用定义，移除试点中 CLI 直接持有 Entry DynamicParameterDescriptor 的耦合。类型和通用值处理独立于 Numeric/Slider 等编辑方式；EditorId 为开放字符串。不要求变量/设置继承新的公共基类。元数据采用组合，具体名称在实施时确定。
- 验证：截图可省略文件名且拒绝显式 null；CLI 必填、默认值、数字转换/范围、flag/重复选项和错误退出码；公共工具覆盖整数/浮点/布尔/枚举/对象/数组序列化、反序列化、校验和描述一致性。省略/null 分别验证；自定义 converter 和业务校验边界明确。Entry 自定义校验/Composite 路径不失效。
- 文档：记录实际选定模型、适配边界、迁移前后代码与规则声明次数。
- 风险：CLI 文本强行遵守 JSON 字符串语法；nullable 与 optional 混淆；为共用引入第二套值类型；help 与执行仍分离。
- 退出与收益检查：两个真实消费者使用同一事实源，行为和依赖边界验证通过。若适配量超过收益，保留有用的校验工具、撤销多余抽象并记录缩小方案；不强推 Phase 2a 全量迁移。

## Phase 2a：迁移已证明有收益的参数消费者

状态：verified；依赖 Phase 1；相对工作量：中。结果与保留范围见 [phase-2a.md](phase-2a.md)：512 项回归通过，独立契约部署与 GameTestCase 编译通过。新增字段/提示生产代码 48 行，总体生产代码净增 76 行，未扩大为全领域模型迁移。

- 目标：删除代表性重复并固定生成器可消费的最小 API。
- 模块：Contracts.Builtins、CLI 参数描述/解析与 help；效果描述只按实际共用收益接入基础工具。
- 设置元数据：抽取有实际共性的有序字段结构、值约束和 EditorHints；SettingItem 保留读写/通知/队列，Entry 保留作者语义，CLI 保留文本绑定。合法选项与展示标签分离，动态选项更新和 ActionSetting 留在宿主。
- 任务：按 Phase 1 结论迁移 Entry 和 CLI 的重复规则，移除失去消费者的旧实现；UI 提示保留独立元数据。视觉/音频效果只共用适用的参数校验，不改变 stage/scope/动画语义。变量只复用确定重复的小工具，不合并模型。
- Entry 基础：比较契约 DTO、Arguments、BuiltinInstructionFactory 和各 module 的读取/工厂胶水，统一相同职责并删除重复事实源。生成器未接入前也须有简洁的手写接入路径；不要求每个 primitive 同时维护参数表、读取默认值和新的适配 wrapper。
- 验证：受影响的契约规范化/编译、CLI help JSON 与真实命令解析、无契约基础查询、项目 DLL 独立加载和架构门禁。测试应校验规则执行，不能仅比较同一实现导出的两份结果。
- 文档：同步已验证的参数边界和开发示例；记录未迁移部分及理由。
- 风险：迁移整个项目但只减少少量声明；新旧事实源并存；工具内部 CLI 定义被混入用户项目 DLL。
- 退出：所选消费者不再重复维护同一基础规则；无未使用兼容层；未迁移消费者保留清晰边界。API 足够简洁时记录 Phase 5 可跳过的理由。

## Phase 2b：无样式 Avalonia SchemaForm 与工厂注册表

状态：verified；依赖 Phase 1/2a 的稳定字段描述；相对工作量：中。结果见 [phase-2b.md](phase-2b.md)：290 项回归，真实视觉树/输入/生命周期验证，契约部署与项目编译通过。表单/工厂生产净增 572 行，实际消费者复用收益由 Phase 2c 验证。

- 目标：按照 schema 的字段顺序生成子控件，控件不包含业务类型到控件的硬编码分支。
- 模块：Core 中立字段/编辑提示，优先使用现有 GalNet.Avalonia.Controls 承载平台组件，演进既有 editor lease 协议。
- 任务：最小 SchemaForm、EditorRegistry dictionary、FieldEditorFactory/Context/Lease 与数据绑定适配协议；EditorId 显式匹配及默认类型映射。默认 Numeric/Slider/Toggle/Choice/Text 工厂负责创建和绑定，支持自定义工厂及显式替换；保留草稿，复用公共值校验。结构模板由主题/宿主提供，不强制视觉样式，不添加通用表单插件框架。
- 验证：有序生成、显式/默认/自定义映射、重复和不支持类型诊断；每字段独立实例；中间输入/省略/null/校验失败不误提交；值/选项变化不重建表单；换 schema/binding、卸载/重挂及创建失败正确退订释放；过期异步结果不能写入新上下文。
- 文档：记录 registry 与 context 的最小协议、无样式含义、模块/绑定/控件所有权；正式事实在通过后同步。
- 风险：工厂持有全局项目服务、绑定泄漏、静默回退错误编辑器、把所有 JSON 结构都扩展成复杂表单、JSON 类型/浮点精度因 UI 转换丢失。
- 退出：默认和自定义工厂经真实 Avalonia 测试，控件只遍历 schema、解析工厂、挂载和清理；不依赖设置保存/Editor 撤销服务，Core 不引用 Avalonia。

## Phase 2c：Editor 与 Sample 设置接入共享字段编辑器

状态：verified；241 项回归通过，结果与收敛边界见 [phase-2c.md](phase-2c.md)；依赖 Phase 2b；相对工作量：中，按实际覆盖限制迁移。

- 目标：同一元数据和默认工厂可用于项目编辑与玩家设置，保留不同提交行为。
- 模块：Editor 的 EntryParameterSchema/CreateField/参数控件，Sample 设置组合，GameView 设置适配，受影响主题模板。
- 任务：Editor binding 适配作者值、命令/撤销/保存；设置 binding 适配 async apply、forced/availability、持久化队列；两宿主注册默认集合及自定义工厂。先迁移数值、滑块、布尔、文本和选项代表路径；资源选择/表达式补全通过 Editor 专属工厂，不把它们塞进默认控件。删除已迁移路径的重复选择/绑定逻辑，保留未迁移功能的明确边界。
- 验证：Entry 数值编辑/撤销/保存/无效草稿；设置范围/应用失败/保存失败/强制值/动态选项及真实绑定；代表主题结构可用且无默认皮肤侵入；切换项目/页面后无旧工厂、值和订阅残留。自定义工厂捕获正确作用域，不自动从项目 schema DLL 构造控件。
- 文档：新增字段和编辑器的接入步骤，列明已迁移与保留路径；记录代码量和适配成本。
- 风险：表面复用但每个工厂复制保存逻辑、项目按键撤销语义退化、设置原有错误/可用性消失、为了共用重做整个 UI。
- 退出与收益检查：两个宿主使用同一组默认工厂而提交机制独立，实质删除重复编辑分支；如果适配成本过高，缩小覆盖并记录原因，不保留两套维护同一路径的实现。

## Phase 3：优化 Sample service 并集中项目扩展入口

状态：verified；97 项定向回归、独立契约部署与实际 CLI 编译通过，结果、成本及 service 保留理由见 [phase-3.md](phase-3.md)。采用 Phase 2e 的最终契约/执行 API；字段工厂入口采用 Phase 2b/2c 协议；按候选局部收敛。

- 目标：开发者接入业务只修改契约和平台入口，生命周期文件不感知业务类型。
- 模块：Sample.Contracts、Sample.Avalonia 的 App/ResourceScope/Presentation/项目配置，Headless 的契约消费。
- 任务：保留契约选择入口；增加一个集中平台 customization 入口，接入主题/本地化/可选调试、执行注册、decoder 和预加载策略的实际修改点。截图迁移为业务实现文件，由平台入口注册。保留五个生命周期职责，不为每个 hook 新增文件/接口。
- 编辑器入口：Sample 集中默认和自定义字段工厂注册；Editor 在对应宿主/项目组合根注册扩展。字段工厂与执行 module 注册相邻但协议独立，不把创建控件塞进契约 DLL。
- Schema 来源接入：入口显式取得项目引用的 ProjectSchemas.Modules 并构建游戏 catalog，统一在此选择 schema 与执行 modules；Editor/CLI 继续走项目作用域 DLL 加载入口。相关资源/Gallery catalog 在现有组合边界集中选择，不扩大为全部类型 DLL 化；缺 schema/执行仍明确诊断。
- Service 整理：根据 Phase 0 清单合并职责重复、没有独立价值的转发包装；消除重复状态和重复创建/订阅；分离常被二次开发的启动/设置策略与稳定机制。检查 SaveSession 的 Gallery resolver、默认 layer 适配、Debug actions 与 Session 的边界以及 Headless 的重复组合，不强制迁移全部 service。小步骤分别验证，职责迁移与行为修正不混在一个提交。
- 验证：截图经契约编译和宿主注册执行；缺实现仍明确失败；不同项目选择不串用；主题/启动/设置与退出相关回归。游戏项目引用构建、契约独立构建/部署和 CLI 读取均可用，游戏无需动态加载契约。
- 来源验证：自定义/部分内置 schema 由集中入口取得；游戏与工具对同一契约的描述一致；没有隐式全内置 fallback，集中入口不强制游戏 DLL 加载或形成第二份 schema。
- Service 验证：实际变动对应的本地化 provider/事件释放、玩家设置持久化与失败处理、启动取消、存档/Gallery 所有权、窗口显示及调试路径；重复初始化/关闭不残留订阅或后台工作。未改动的 service 不为形式覆盖新增镜像测试。
- 文档：列明开发者常改入口与稳定宿主职责；给出新增 entry 操作步骤。
- 风险：customization 成为万能 ServiceProvider；错误捕获 scoped 对象；移动代码增加更多文件却不减少修改点。
- 退出：新增截图类业务无需修改 App/Session/Runner/释放流程；平台依赖不进入契约，借用与拥有对象明确。Gallery resolver/layer 适配等只在实证简化时迁移。
- 收益检查：给出 service 删除/合并、转发层和状态源变化以及开发者修改点对比；保留项有职责依据。若集中入口只是增加一次转发、没有减少耦合或修改点，则缩小设计。

## Phase 4：建立新 Entry 的低样板作者路径

状态：verified；307 个不重复回归用例、迁移前后五条指令 CLI 描述对照、独立契约部署和实际项目编译通过，结果及作者成本见 [phase-4.md](phase-4.md)。

- 目标：让开发者添加常见 Entry 时少重复声明 type ID、字段名/类型/default/constraints、参数读取和工厂胶水；schema、编译校验与运行时取值继续来自同一份契约事实。
- 模块：Core.Entry/Parameters、Contracts.Builtins、Presentation.Abstractions.Execution、Primitives.Builtins、Sample.Contracts 与 Sample.Avalonia；同步更新 `docs/development.md` 的新 Entry 路径。
- 基线：各选一个无参数同步 Entry、带默认值/约束的同步 Entry，以及一个有自定义校验或 Composite 的 Entry；记录实际改动文件、手写生产代码、重复事实数、接入步骤和需要理解的概念。异步 Entry 另作生命周期样本，不让简单样例被异步复杂度主导。
- 候选试点：对比现有手写 DynamicParameterTable + string 参数读取、轻量 typed arguments/definition API、复用 System.Text.Json 元数据的 DTO 描述，以及小型同步工厂辅助。选择能减少重复事实且调用端仍直观的最小组合；不得先验地引入 generator、通用 DSL、反射扫描或新 Helper 项目。
- 实施：保留 IEntryModule 契约和 IPrimitiveModule 执行职责分离，保留项目显式选择 modules。普通 Entry 可将参数定义与类型化读取绑定到同一声明；同步立即执行使用简洁工厂路径。复杂校验、跨字段规则、Composite 展开及自定义异步 PrimitiveInstance 继续显式实现。允许按领域复用一个 module，不要求每条 Entry 都单独新增模块或重复写 wrapper。
- 验证：契约 schema 展示、默认/必填/null/constraints、JSON converter/property naming、编译规范化、运行时类型化读取与真实执行使用同一字段事实；未知字段与错误类型仍明确诊断。分别从现有旧写法和新路径实现同一代表 Entry，比较手写步骤/重复声明/新增基础设施成本，并验证项目选择与独立 Contracts DLL 加载。
- 文档：给出无参数、典型参数和需要自定义语义的三种新增 Entry 示例；明确何时使用简化路径、何时保留手写实现。
- 风险：为了少几行引入隐藏反射/双份描述；类型化 DTO 丢失 JSON converter、null/default 语义或开放字段规则；同步 helper 越界承载 skip/replay；将契约与平台执行重新耦合。
- 退出：普通 Entry 的重复字段事实和手写取值胶水实质减少，维护来源唯一，开发者可从文档示例完整编译/执行；复杂行为边界依旧清晰。以手写维护点和总成本作证据，不设任意行数百分比。

## 全量 DTO 实施批次（2026-10-14 用户已授权）

顺序调整：Phase 4 → 5a → 5b → 5c → 5d → 4b → 后续净收益复核 → 6；不重新建立生成器或通用 Runner。每批完成验证后独立提交。

- 5a（verified，456 项回归与 CLI 格式对照通过，见 phase-5a.md）：JSON 默认/选项与字符串 bool/enum 编码、公共 DTO 读取、Composite 类型化入口；以 audio.stop 和测试 Composite 验证格式与行为。退出：省略/null、默认、codec、独立实例与编译信封验证通过。
- 5b（verified，235 项回归与 11 条 CLI schema 对照通过，见 phase-5b.md）：11 条简单 Primitive 的唯一 DTO 描述和执行读取；退出：编译/执行与原 schema、诊断回归通过。
- 5c（verified，338 项回归与 12 条 CLI schema 对照通过，见 phase-5c.md）：其余媒体/场景/动画/效果的外层 DTO；开放 payload 保留 JsonElement，普通执行/计划/重放只共享有收益的读取与 request 构造。退出：默认/转换/场景及媒体回归通过，不改变快照。
- 5d（verified，393 项回归、8 条 schema 与 16→20 编译对照通过，见 phase-5d.md）：8 条 Composite 的 typed options 与旧 primitive 类壳/取 ID 工厂收尾。退出：展开结果、batch/condition/reading boundary、独立契约及全部项目编译通过。
- 4b（planned）：简单异步展示与模块任务 scope，截图/清理失败策略保持原语义；对白/NVL 状态机另做净收益检查，不把视频/动画计划强行并入。

## Phase 4b：Entry 异步生命周期与 Sample 清理收敛

状态：planned；依赖 Phase 3；与 Phase 4 作者路径正交；相对工作量：中。

- 目标：删除截图异步样板，修正清理短路，同时保持业务行为和显式所有权；本阶段不以缩短普通新 Entry 的参数/schema 声明作为验收。
- 模块：Core 的实例完成协议（仅必要改动）、Presentation.Abstractions 的模块工具、Sample 截图/Session/Runner/Presentation，以及按证据选取的 Builtins PrimitiveInstance、module factory、RuntimeActions 与重放消费边界。
- 任务：选择最小公共异步机制，支持 scope/module 取消、完成与错误传播、运行中任务等待；观察并移除完成任务。保留业务 dispatch/replay 策略。统一“按顺序继续清理、最后聚合错误”，优先复用已有模式。禁止引入通用 Runner/资源插件框架。
- Entry 整理：删除同步立即完成、异步观察和工厂构造中已确认的重复；区分基础完成机制与业务状态/请求构建。先用截图和一个实际内置异步实例验证，再按相同语义扩展；等待/对白/视频/动画等差异保留独立实现，不能让基类成为类型分支集合。现有吞错/记录警告/传播失败策略逐项保留或另列行为修正，不自动统一为一种策略。
- 验证：真实 PNG 与 Normal/Quick/CatchUp；执行失败进入 Engine 错误路径；排队取消、模块释放等待、并发完成与重复释放；某项释放/flush 失败时其他资源仍释放且错误保留；已完成任务不长期积累。迁移的执行分类各有代表性回归，覆盖立即完成、玩家 skip/播放模式切换、展示后台失败、持久状态写入/重放、旧实例清理不破坏新对象。
- 文档：记录生命周期用法、顺序与错误边界。
- 风险：过早移除任务丢失异常；完成事件早于清理；抽象推断 CatchUp 行为；公共清理器隐藏依赖顺序。
- 退出：截图行为不变，异步跟踪或清理逻辑确有简化；故障注入证明清理不短路。若通用抽象未减少复杂度，保留局部实现，只提交有独立价值的清理修正。Core/契约/执行/平台依赖方向不改变，不强制所有实例都产生异步状态机。

## Phase 5：新 Entry 作者体验的系统性精简与工具化

状态：planned；依赖 Phase 4 的代表性路径与成本数据；可参考 Phase 4b 已验证的执行边界；相对工作量：中～大。

- 目标：围绕“新增常见 Entry 要修改什么、重复声明了什么、需要记住哪些规则”继续降低端到端成本。源码生成只是一个候选实现机制；即使不选生成器，也要完成作者 API/复用方案评估并落实有收益的改进。
- 候选方案逐项比较：改进现有声明 API；提供轻量 generic definition/typed reader 与同步 immediate factory；用显式 DTO/属性和 System.Text.Json 元数据派生 schema/读取；使用 IIncrementalGenerator 生成有序字段、规范化 typed arguments、描述/诊断；在生成代码之外增加针对错误声明的 analyzer/diagnostics 或 IDE quick fix；提供可复制的 Entry 模板/示例。按维护成本、运行时/构建依赖、IDE 支持、报错质量、生成物可读性和跨工具一致性选择组合，不预设单一路径。
- 优先消除的重复：参数名在 schema 和读取代码中重复；类型/default/null/constraints 与实际 JSON 消费分离；type ID/schema/工厂映射需要多处同步；常见同步 Entry 必须写无业务价值的实例/适配 wrapper。只有代码重复且语义稳定时才自动化；业务操作本身保持手写。
- 设计约束：手写与自动产物不得成为两份维护事实；项目显式选择生成/定义的 module，不扫描整个程序集自动启用；契约生成物只依赖 Core，平台执行保持在平台层，Roslyn（若采用）只进入构建工具依赖；保留 converter、JSON 名称、required/default/null、字段顺序、EditorHints、手写业务验证与 Composite 扩展点。UI 不生成具体控件/绑定。
- 实施顺序：先做一个低成本 API/DTO 原型并与 Phase 4 结果比较；若仍存在成批稳定重复，再实现最小工具链原型（可能是 generator、analyzer、模板或组合）。只将不同参数形态的少数 Entry 迁移，保持复杂 Entry 对照样本。是否保留独立 generator 项目由实测收益决定。
- 验证：用无参数同步、典型参数、枚举/嵌套对象或 converter、自定义校验/Composite，以及一个需要异步生命周期的 Entry 做端到端作者任务；校验契约展示、工具读取、规范化、执行一致。若采用生成器，验证 incremental 输入变化、可定位诊断、消费者干净构建和独立契约部署；若采用 analyzer/template/API，则验证 IDE/编译体验、错误诊断和新增项目步骤。统计手写维护点、重复事实、生成产物、测试/工具代码、依赖与调试成本，不只统计删掉的业务行数。
- 文档：给出推荐路径、例外路径、常见诊断和最小完整示例；同步实际支持的 DTO 元数据，不把未实现能力写入 spec。
- 风险：把样板移入不可调试生成物或更复杂 DSL；反射/生成对 System.Text.Json 语义处理不一致；分析器与生成器维护成本超过业务收益；模板过期；为了 API 简短隐藏模块生命周期或运行模式差异。
- 退出：Phase 4 的新建流程至少在一项端到端指标上进一步减少重复维护点，并且工具/抽象总成本可接受；所有生成或派生字段只有一个源头，复杂 Entry 仍可直接表达。若 generator 不划算，可以保留 API/analyzer/template 方案并记录比较；若没有任何附加方案产生净收益，明确列出已尝试候选与证据后结束本阶段，不为“有 Phase 5”强加基础设施。

## Phase 6：范围复核、回归与收尾

状态：planned；依赖所有实际实施阶段；相对工作量：小～中。

- 目标：确认代码更清晰，而不是新增一层外观一致的框架。
- 模块：实际改动的生产项目、测试与正式文档。
- 任务：审核剩余双份声明、无消费者类型、重复工具、项目引用和 developer 修改点；整理未实施项。只检查本 feature 必要的 import/格式，不扩展为全仓库清理。
- 验证：根据最终 diff 运行 Core/契约/CLI/Presentation/Runtime/集成与架构中受影响的测试；Sample 与工具相关构建；独立契约加载、截图运行和 CLI 真实命令 smoke。已通过的测试仅在新变更或失败需要时重跑。
- 文档：summary/review、当前 spec/development 示例；记录每阶段提交、证据、收益和 skipped 原因，不声称暂缓项已完成。
- 风险：用总行数下降替代可读性评估，或忽略生成器与测试成本。
- 退出：无阻断项，所有已实施阶段有验证和独立提交；契约/平台分层成立；开发者接入步骤明确；参数共用与生成器的收益结论可追溯。

## 暂缓项

- Core 领域去耦、Audio 迁层、Layer/SceneState 移出核心、通用模块快照和 animation/plan 调度体系重构。
- 游戏变量与参数完整模型合并；已有 Variable.Clone/PlayerVariableService 不重复改造。
- 资源/Gallery/视觉效果/音频效果全部通过一个新项目 DLL 入口注册。
- 统一所有动态对象、创建万能 DynamicType/Helper 框架。
- 全面合并 Sample 文件或强制搬迁到共享宿主项目。
- 为所有平台业务建立接口；自动推断执行、重放和资源所有权。

后续只有实际收益或新需求支持时再纳入，并记录独立范围与成本。

## 范围变更记录

- 2026-10-14：用户将共用方式收敛为值描述与序列化/校验工具，各业务模型组合使用；未提交 Phase 1 试点需修订，不认定通过。
- 2026-10-14：纳入 DTO + 特性生成中立有序 schema、开放 EditorId、无样式 SchemaForm 和 dictionary → 工厂的服务。拆分 2a/2b/2c，以控制 UI 工作量及回归范围；Core 领域/快照重构继续暂缓。
- 2026-10-14：用户明确 Entry 优化重点是减少开发新 Entry 的样板。将 Phase 4 调整为作者路径试点，将原异步/清理目标独立为 Phase 4b；Phase 5 扩展为端到端作者体验与工具化评估，源码生成器仅为候选之一。
