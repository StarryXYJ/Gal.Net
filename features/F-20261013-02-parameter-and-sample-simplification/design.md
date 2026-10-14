# 设计边界与收益判断

2026-10-14 用户确认开始全量 DTO 与执行/实例简化，评估见 [迁移评估](dto-migration-assessment.md)。实施顺序为 5a 公共能力 → 5b 简单 Primitive → 5c 复杂 Primitive → 5d Composite/旧类壳 → 4b 异步实例与任务 scope；对白/NVL 与请求构造按净收益处理。JSON 字段、默认/null、运行模式和错误策略保持显式；不改变快照与平台所有权。

2026-10-14 Phase 4 已验证：PrimitiveImplementation.Immediate / Immediate<T> 简化同步工厂，轨道操作与 wait 使用唯一 DTO 参数声明，复杂校验/Composite/异步实例保留手写扩展。307 个不重复回归用例通过；五条指令迁移前后 CLI 描述相同，独立契约部署与实际项目编译通过。证据见 [phase-4.md](phase-4.md)。当前 5a/5b 也已验证，下一批为 5c 复杂 Primitive；异步机制与清理由后续 Phase 4b 处理。

2026-10-14 最新收敛决定：DTO 成员特性生成有序 schema 与成员访问表，公共 DTO 适配器连接表单和宿主提交；新增 Phase 2d 位于 Phase 2c 最小验证之后。源码生成器已确认为实施目标，替代下文旧的候选/条件表述。范围、通知/默认值边界及退出条件以 [DTO 收敛计划](dto-convergence-plan.md) 为准；Phase 2c 最小宿主接入已验证（241 项回归），见 [阶段记录](phase-2c.md)；Phase 2d 单 Entry DTO 生成/绑定试点已验证（348 项回归、独立契约部署与实际 CLI 编译），见 [phase-2d.md](phase-2d.md)。Phase 4、5a/5b 已验证，下一步为 Phase 5c；全量设置 DTO 化仍暂缓，进一步作者工具化由 Phase 5 处理。

状态：设计与目标边界。Phase 1–4 的值工具、共享表单/宿主适配、DTO 生成/执行、Sample 组合和代表作者路径已验证；后续为异步/清理及进一步工具化。后续方案从已落地的 DTO generator 与 For<T>/Immediate<T> 继续评估收益，不重复建立工具链。

## 当前事实

- Core 的 DynamicParameterDescriptor/Table 已提供 CLR 值类型、required、JSON 默认值、constraints 与自定义校验入口；PrimitiveArgumentHelper 负责 Entry 参数规范化。
- CLI 有 CliParameter/ToolOperationCatalog，CliInvocation 已消费定义执行选项发现、重复选项、枚举和必填等检查，但数字转换和部分规则仍分散在调用方；不能把它描述成完全不消费定义的帮助表。
- CommandSchemaBuilder 和 ToolOperationCatalog 已使用 DTO/JSON schema 描述操作输入。共享参数方案应复用其能力，不再手写第二份嵌套对象 schema。
- Entry 编辑提示、视觉效果参数、音频效果参数各有描述类型。它们部分重叠，但 stage、动画和 scope 等领域约束不能简单删除。
- 游戏变量已使用 VariableValue，Variable.Clone 和 PlayerVariableService 的整理已提交；不重复实施已完成的优化。
- Sample 契约与平台实现分离。ProjectSchemas.Modules 在游戏通过项目引用消费，在 Editor/CLI 通过项目 DLL 加载。
- Sample 的 App、ResourceScope、Presentation 内有项目配置和功能注册；Session、Runner、ResourceScope、Presentation、SaveSession 拥有不同生命周期，文件数量不等于职责重复。
- 截图模块有独立的取消、任务跟踪、异步完成及释放机制。Session 的部分串行清理发生错误时可能阻止后续清理；Runner/Presentation 已采用继续清理并聚合异常的模式。

## 参数基础

公共能力归 Core 内明确的参数命名空间，优先演进/迁移现有类型。具体命名在试点中确定，不同时维护旧、新两套事实源。

2026-10-14 修订：公共基础是中立 ValueDescriptor、ValueCodec/ValueValidation 工具和可选 EditorHints。Entry 参数、SettingItem、CLI 参数保留各自的完整模型，通过组合或工具调用使用公共能力；不让 CLI 直接持有 Entry 的 DynamicParameterDescriptor。上一版“同一参数规则”指中立的值规则，不是统一完整参数模型。早期试点中的直接耦合已在 Phase 1 移除，FieldDescriptor/EditorHints 已在 Phase 2a 实施。

ValueDescriptor 描述 CLR/JSON 值类型、null 策略与通用约束；工具支持序列化/反序列化、类型检查及范围/长度/选项等校验。保留既有 CLR/JSON 能力，不新增标量值容器。必填/省略/默认值留在字段使用定义中，业务校验留在消费者；玩家/存档变量不必每个值都携带 schema。验证覆盖对象、数组、枚举及标量，不限定截图/分页。

EditorHints 与合法数据约束独立。使用 string EditorId，内置编辑器提供稳定常量，null 表示根据值类型选择默认工厂。Step/Format/Unit 为编辑提示；若要求数值必须为某个步长的倍数，需另有数据约束，不能从 UI Step 自动推断。合法选项值和展示标签分开。Core 不引用控件、绑定对象或运行期业务服务。

共享范围为值类型描述、默认值/必填/null 规则、通用约束、JSON 规范化和基础校验。继续用现有 JSON/CLR 类型能力表示嵌套对象，不建立新的通用值容器、DSL 或任意插件总线。

Entry 保留 primitive/composite、展开代码、阅读边界、资源引用和编辑提示；CLI 保留路由、选项名、位置参数、flag、重复输入和退出码。命令选项名与逻辑参数名的绑定由 CLI 适配器负责，不写进公共参数模型。

格式转换先进入规范参数值，再共用基础校验。省略与显式 null 必须分别处理；必填不自动代表非 null，允许省略也不自动代表允许 null。默认值和基础约束的描述与执行使用同一来源。文件/页面/项目是否存在等业务判断留在所属层。

试点包括截图参数和至少一个实际 CLI 命令；补充参数层验证覆盖整数、浮点、布尔、枚举及嵌套对象。不能仅验证两个 string 参数就宣布模型足够通用。

## Sample 接入

本范围包含 Sample 的 service 设计本身：会话、启动、设置、本地化、窗口显示、存档、资源、展示、调试与重启适配，以及 Headless 中对应的组合逻辑。检查业务策略是否混入基础设施、单纯转发是否有独立边界、重复状态是否可以由唯一事实源派生。允许合并职责实质重复的小 service，或把调试/项目策略移到已有业务区域；不因名字都是 service 就合并不同生命周期。优先调整现有文件，不额外建设共享宿主项目。

保留两个明确入口：契约项目选择 schema，平台项目选择实现及项目策略。建议以一个平台 Customization 文件和按业务组织的功能文件承载修改点，不为每个 hook/option/委托创建单独文件。

集中入口必须包括 schema 来源的读取/选择与 module 注册：游戏将项目引用的 ProjectSchemas.Modules 经明确入口取得并构造 catalog，平台入口接入执行 module 和已有领域 catalog/decoder 配置。Editor/CLI 使用已有 ProjectSchemaContext 读取项目 DLL；不要求游戏读取 DLL、不新增通用 schema-source 插件体系、不复制参数事实源。保留项目作用域、失败诊断、无内置 fallback 和实际内容的执行覆盖检查。

稳定宿主只消费显式、类型化的注册入口：执行模块、decoder、预加载策略、页面适配及主题/本地化配置。上下文按实际需要提供借用对象和生命周期，不暴露万能 IServiceProvider，不强制每个业务新增接口。

新增业务不需要手改顶层关闭流程。模块拥有的资源由模块释放，借用的页面/平台端口由宿主在模块完成清理后释放。不新增通用 Runner 框架或改变 Engine 的所有权。

Gallery 媒体 resolver 的位置、默认 layer 适配器是否可复用，以及设置/首次启动策略的拆分，先检查实际引用与替换需求；只有能简化所有权或减少修改点才调整，不作为强制搬迁清单。

## Schema 表单与编辑器工厂（2026-10-14 纳入）

方案与现有依赖兼容：GalNet.Avalonia.Controls 已引用 Core，可优先在该项目承载共享字段编辑基础；不新建 UI Helper 项目。现有 SettingEditorLease/CustomSettingEditor 已有控件与释放机制，实施时优先演进而非叠加两套生命周期工具。具体类型名是建议，不是已实现 API。

2026-10-14 实施细化：registry 由宿主创建并注册默认集合；主题可提供结构模板、样式和可选工厂扩展，由宿主显式组合，主题不拥有设置保存服务。默认 Numeric 使用文本编辑，以保留中间输入和大整数精度；主题可替换为数值控件。默认 Slider 仅接受可安全转换到 double 的数值类型和明确有限范围，decimal 或不安全整数范围明确诊断，不静默损失精度。Form 层不参与类型选择；binding 以 JSON presence 区分省略/null，未编辑的默认/空状态失焦不自动提交。宿主绑定的具体接入仍属 Phase 2c。

- Schema：提供稳定字段 ID 和有序字段列表，字段组合值描述、默认/省略规则、标签及编辑提示。手写和生成式描述使用同一结构；顺序由列表定义，生成器负责稳定产出，不依赖运行期反射枚举顺序。对象/数组可先提供 JSON 编辑器，不承诺首轮任意复杂嵌套表单。
- SchemaForm：一个无样式表单控件，绑定 Schema、EditorRegistry 和数据 Binding。按顺序创建/挂载子控件并释放；不含具体 EditorId/业务类型的 switch，不负责存档、撤销或设置应用。优先一个具体控件，无实际替换需要时不增加多层基类。
- EditorRegistry：内部 dictionary 保存 EditorId → FieldEditorFactory，另有默认值类型选择规则。Register/Resolve/显式 Replace 足够；不增加扫描插件、动态服务发现或 DI 容器解析。工厂验证自己支持的值类型。
- FieldEditorContext：字段 schema、编辑草稿/诊断及读写提交适配；允许宿主明确提供动态选项等必要能力。工厂通过宿主组合期闭包捕获资源选择等依赖，不让 SchemaForm 认识业务服务。
- FieldEditorFactory：创建一个新控件，绑定值/范围/步长/选项/可用状态，返回 FieldEditorLease（控件和幂等解绑/退订释放）。不共享控件实例，不在每个工厂重复公共校验、项目保存或撤销逻辑。
- 默认 Avalonia 工厂：Numeric、Slider、Toggle、Choice、Text；复杂对象/数组的基础 JSON 编辑可按成本加入。Editor 与 Sample 各自组合默认集合及自定义工厂。资源选择/表达式补全/自定义编辑器由宿主扩展。

Binding 提供读值、外部变更通知及提交候选值的能力。共享编辑模型持有草稿并处理类型转换/基础错误，不把 `-`、`1.` 等未完成输入写入业务值。Editor 适配作者值表示、撤销/保存与事务；设置适配 async apply、forced value、可用性及持久化队列。保留“未提供”与“显式 null”状态，不能因 DTO 或输入框空值混为一谈。ActionSetting 等无数据操作不强行当作值字段。

约束优先顺序为：显式 EditorId 精确查找；没有 ID 时使用默认类型映射。重复注册报错，替换必须显式；未注册 ID/不支持值类型显示诊断，必要时提供只读或不会静默写回的基础展示。未知业务控件不被普通文本控件无提示替代。

生命周期：结构/Binding/Registry 换代时取消旧编辑上下文并释放旧控件与订阅；普通值、选项、可用性更新不重建整张表单。挂载/卸载和失败构建须有完整清理路径；过期异步提交结果不得更新新字段上下文。宿主拥有数据服务，控件只释放自己的绑定/控件资源。捕获项目对象的工厂属于对应作用域，不放入进程级静态 registry；纯默认工厂可共享。

无样式表示控件保留行为与模板协议，布局、颜色和视觉资源由主题/宿主提供；不代表完全没有可挂载子控件的结构模板。Editor/玩家主题可使用不同视觉呈现，但共用字段行为。首轮不顺便重做全部主题和页面。

## 新 Entry 作者路径

主要问题是添加一条普通 Entry 时，开发者要在契约声明、参数规范化/读取、执行工厂及组合注册之间来回复制字段和类型信息。当前示例把 schema 与执行实现拆开，平台边界是必要的；schema 内同一参数名、类型、默认值或约束在多处重复维护则不是必要的。目标是降低作者在一条常见 Entry 上要维护的事实和步骤，而不是强迫所有执行类型长得相同。

期望的数据流是：一份 Entry/参数声明形成 editor/compiler/tool 共用 schema；运行时将 JSON 参数规范化为类型化参数，交给手写业务处理器；项目组合根仍显式选择 schema 与实现 module。普通同步 Entry 可以使用短的工厂路径；复杂校验、跨字段规则、Composite 展开、平台依赖和异步 PrimitiveInstance 保留明确的手写扩展点。

先对无参数同步、典型带参数同步、含自定义校验或 Composite 三种路径做基线，并挑一个异步 Entry 验证简单 API 不会误导生命周期。记录实际手写步骤、重复事实、改动文件，以及作者需要理解和调试的概念。用同一行为实现对照样例，确认减少的是维护点和同步事实，而非把代码移进隐蔽框架。

方案候选按成本由低到高评估，不预定必须使用 generator：

- 改进现有手写声明与 typed reader/factory API，减少字符串参数访问和无意义 wrapper；保留直接描述复杂语义的能力。
- 用一份显式 DTO/成员元数据从 System.Text.Json 类型信息派生 schema、编辑提示与参数读取；比较运行期 metadata/反射与构建期派生的复杂度和兼容性。
- 若属性/字段级声明仍有大批稳定重复，再评估 incremental generator 生成中立 schema、typed arguments、简单 module glue 和编译诊断。Analyzer、IDE quick fix、模板和可复制示例也可解决部分错误或步骤，不必与 generator 捆绑。
- 组合小型方案，例如 typed definition API 加少量代码生成；不因工具可实现就引入额外工具链。

不论采用何种方式，字段名/JSON 名称、CLR 类型、required/default/null、constraints、converter、字段顺序和 EditorHints 必须只有一个作者维护来源。自动派生结果供 Entry/Editor/CLI 使用共同模型；不分别维护完整而互不共用的 Entry、设置和 CLI 描述。允许手写业务校验、跨字段规则与 Composite。平台控件/绑定不进入契约生成物；Roslyn 若采用只进入构建工具；项目不扫描程序集自动注册或隐式启用 module。

对新增同步 Entry，重点检查 schema 与 typed arguments 是否仍需重复参数名/类型、是否必须手写基础工厂适配、一个 module 如何集中多个相关实现。可以让普通路径短，同时让调用者仍看得出 type ID、业务操作和所属 module。运行时自动扫描、全局服务定位、第二套 DTO/参数容器、关闭手写扩展的封闭生成模型均不作为简化目标。

## 异步与清理

Entry 生命周期优化与新 Entry 参数样板是正交问题，不能用异步抽象完成情况代替作者体验验收。检查 Core 的 PrimitiveInstance/ImmediatePrimitiveInstance、契约 Entry DTO、PrimitiveModule/PrimitiveImplementation、Builtins 的 Arguments/BuiltinInstructionFactory、各领域实例和 RuntimeActions。优先演进已有基础，不另建平行 EntryBase/ModuleBase/参数工具。

当前部分效果/视频实例记录展示错误后继续完成，截图则通过完成任务传播失败；这不是可直接消除的语法重复。先按同步立即完成、异步展示、玩家交互/可跳过和持久场景执行分类，再抽取相同机制。阻塞、skip、CatchUp、状态写入、旧实例清理身份检查及错误策略保持显式；改变错误策略必须单独记录行为变化并验证，不能夹带在机械基类迁移中。

只抽取有明确共性的取消、完成任务、错误传播、运行中任务跟踪及异步释放。Normal/Quick/CatchUp、是否阻塞/可跳过、业务资源发布仍由 entry 明确选择。截图保持 Normal/Quick 保存、CatchUp 无副作用、失败可观察、文件发布前清理完成。任务跟踪器在完成任务被观察后移除，避免长期保留全部历史任务；不得因移除任务丢失错误或破坏释放等待。

资源释放维持显式顺序，失败后继续清理独立资源并聚合错误。若重复量不足，可沿用局部函数；不为几行清理代码强建通用资源管理框架。

## 后续工具化与体验改进

Phase 5 是继续改善新增 Entry 的系统性工作，不等同于“实现源码生成器”。先基于 Phase 4 的手写成本与作者反馈比较 API、typed definition、JSON metadata、代码生成、analyzer/diagnostics、quick fix 和模板/示例；各自可以独立采用，也可以小规模组合。判断标准是新 Entry 的端到端维护成本，包括生产声明、工具链和测试，而非某一种技术是否被使用。

若采用 DTO/属性派生或代码生成，显式元数据应提供字段顺序、JSON 名称、converter、约束、EditorId、required/default/null 来源。不得执行任意属性初始化表达式推断运行时默认；C# nullable 不自动等于允许省略或允许 null。对不支持的声明提供定位准确的诊断。复杂验证、Composite、运行模式和资源所有权继续调用手写代码。

源码生成方案需验证生成输入变化、错误诊断、输出可读性、独立 Contracts 部署及 Roslyn 依赖不进入运行时；Analyzer/quick fix 需验证提示准确且无自动改写语义；手写 API/模板需验证一页文档和真实项目可以按其新建并执行 Entry。无论路线如何，按无参数、典型参数、复杂参数及异步样例做对照，并统计新增工具/测试/依赖和排错成本。

## 收益检查

本轮不实施 Core 的领域去耦、Audio 迁层、Layer/SceneState 移出、模块化快照或 animation/plan 调度重构。Entry 优化针对新建作者流程里的可验证重复，不以改变领域所有权作为验收要求。

相同代表性 Entry 改造前后记录：作者手写步骤、参数事实重复次数、所需改动文件/入口、手写生产代码、生成/派生代码、工具及测试/依赖成本、错误定位体验。至少证明一个端到端维护指标下降，同时没有把成本藏到另一个项目或运行期约定；不采用任意百分比或单纯总行数作为硬指标。

若轻量 API 足以消除重复，无须为了“自动化”引入 generator；若单靠 helper 仍需平行维护 schema 和读取表，则继续比较 DTO metadata、generator 或 analyzer。若没有第二个稳定消费场景，优先保留局部清晰实现；若所有候选都只转移复杂度，则记录证据并停止扩张。
