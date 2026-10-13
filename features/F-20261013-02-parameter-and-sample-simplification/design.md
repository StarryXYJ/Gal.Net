# 设计边界与收益判断

状态：设计与目标边界。Phase 1 中立值工具已验证，事实见 phase-1.md；有序字段/编辑提示、共享表单、Sample 整理及生成器仍为目标状态，不将未实现能力写入正式 spec。

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

2026-10-14 修订：公共基础是中立 ValueDescriptor、ValueCodec/ValueValidation 工具和可选 EditorHints。Entry 参数、SettingItem、CLI 参数保留各自的完整模型，通过组合或工具调用使用公共能力；不让 CLI 直接持有 Entry 的 DynamicParameterDescriptor。上一版“同一参数规则”指中立的值规则，不是统一完整参数模型。早期试点中的直接耦合已在 Phase 1 移除，EditorHints 尚待 Phase 2a。

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

## 异步与清理

Entry 基础审查不限于 Sample 截图：检查 Core 的 PrimitiveInstance/ImmediatePrimitiveInstance、契约 Entry DTO 与参数描述、PrimitiveModule/PrimitiveImplementation、Builtins 的 Arguments/BuiltinInstructionFactory、各领域实例和 RuntimeActions 的边界。已有基础优先复用或演进，不再平行新增一套 EntryBase/ModuleBase/参数工具。

当前部分效果/视频实例记录展示错误后继续完成，截图则通过完成任务传播失败；这不是可直接消除的语法重复。先按同步立即完成、异步展示、玩家交互/可跳过和持久场景执行分类，再抽取相同机制。阻塞、skip、CatchUp、状态写入、旧实例清理身份检查及错误策略保持显式；改变错误策略必须单独记录行为变化并验证，不能夹带在机械基类迁移中。

只抽取有明确共性的取消、完成任务、错误传播、运行中任务跟踪及异步释放。Normal/Quick/CatchUp、是否阻塞/可跳过、业务资源发布仍由 entry 明确选择。

截图保持 Normal/Quick 保存、CatchUp 无副作用、失败可观察、文件发布前清理完成。任务跟踪器在完成任务被观察后移除，避免长期保留全部历史任务；不得因移除任务丢失错误或破坏释放等待。

资源释放维持显式顺序，失败后继续清理独立资源并聚合错误。若重复量不足，可沿用局部函数；不为几行清理代码强建通用资源管理框架。

## 生成器

参数模型稳定后，独立 incremental generator 根据显式标记生成参数描述和类型化读取。模块内 schema/工厂注册仅在可重复模式已成立时扩展。契约生成物只依赖 Core，平台生成物位于平台项目；Roslyn 不进入运行期引用图。

具体声明方式：class/record DTO 以显式生成特性标记，成员类型提供结构信息，成员特性提供命名/约束/编辑提示及明确的省略/null/默认规则。生成中立的有序对象/字段描述、规范化读取和工具描述，各消费者组合使用；不分别生成三套互不共用的完整 Entry/设置/CLI 模型。优先复用 JSON 名称/忽略/converter 元数据并检查不支持声明。默认值使用明确元数据来源，不执行任意属性初始化代码；复杂校验、跨字段规则和 Composite 保留手写入口。生成器不生成具体 Avalonia 控件或控件绑定。

生成器不读取项目运行期 DLL、不创建平台对象、不自动启用所有被扫描模块、不推断业务执行语义。项目继续显式选择模块，复杂校验/Composite 展开留有手写接入点。

首个试点为截图和少量有不同参数形态的内置 entry。手写与生成式定义不能同时成为维护来源；评估包含生成器本身、测试、生成物规模和调试成本，不只统计删除的业务代码行数。

## 收益检查

本轮不实施 Core 的领域去耦、Audio 迁层、Layer/SceneState 移出、模块化快照或 animation/plan 调度重构。Entry 基础优化只针对现有架构内有明确收益的重复机制，不以改变领域所有权作为验收要求。

记录相同代表性业务迁移前后的规则声明次数、手写参数读取、需要修改的文件/入口、生产代码差异和新增依赖。代码行数是证据之一，不采用任意百分比作为硬指标。

若新抽象需要大量适配或仍双份声明规则，缩小共用范围；若公共 API 已消除大部分机械重复，跳过生成器；若某项样板没有第二个合理消费场景，优先保留局部清晰实现。
