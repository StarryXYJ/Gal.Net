# 递归属性访问设计

状态：implementation / verification；2026-10-27 根据用户“继续”指令形成正式设计，随后获授权实施。本文保留设计基准；实际接口、验证、偏差和未完成项以 phase-plan、review、summary 为准。

## 目标与本次交付边界

借鉴 Unity SerializedObject / SerializedProperty 的属性访问方式，在现有 FieldDescriptor、ValueDescriptor 和 JSON codec 上提供可迭代、可定位、可局部修改的结构视图。共享结构元数据驱动通用表单、专用编辑器、嵌套值检查与本地化引用索引，保留各自的展示与领域业务职责。

本次交付覆盖：基础结构/路径/有界遍历；DTO、数组和现有 List 类集合、字符串键映射；Transform/Flipbook 的固定结构声明及引用分析；这两种专用编辑器、通用结构表单和项目子字段草稿的属性访问统一。Transform 的编辑、检查、定位闭环是首个可见成果，不等全部迁移结束才验证。

动态音效/视觉参数、Timeline 轨道与事件列为后续迁移场景。本次保留可注入的结构解析入口，并用测试结构证明其上下文失效行为；不重写这些领域的编辑器、执行或存储。未迁移的非空未知结构继续报告分析未覆盖，不能宣传所有内置 opaque 警告都已消除。

## 当前事实与重复来源

| 当前代码 | 已有能力与限制 |
| --- | --- |
| Core.Parameters.FieldDescriptor / ValueDescriptor | 字段身份、presence 规则、默认、约束、JSON options、引用语义；尚无共享的子结构访问入口 |
| ValueCodec / ValueValidation | 使用 System.Text.Json 转换和检查值；不负责项目历史，不保证 DTO 的每个嵌套字段规则都被递归执行 |
| SchemaGenerator / IFieldDto | 生成静态 Schema 和成员访问；仅显式 Field 成员进入 schema，不应扩大为扫描任意 getter |
| StructuredFieldEditor | 自行判断 DTO/集合、反射 schema、用 JsonTypeInfo 映射 wire 名、建立子绑定；字典与未知 converter 等回退 JSON |
| LocalizationReferenceIndex | 另行判断 DTO/集合并映射成员；无法识别 Entry 中 JsonElement 的 Transform 等结构 |
| TransformFieldEditor / FlipbookFieldEditor | 手写子字段和局部修改；Transform 兼容对象及字符串包裹的 JSON，保留实际成员大小写 |
| EntryEditSession | 已有根字段加子路径、项目级草稿和作者 incarnation；另有 JSON 寻址/回写逻辑 |

因此不是缺少 JSON 读写器，而是结构与属性访问在消费者之间分叉。采用公共结构入口收敛重复实现，同时保留现有序列化与作者操作机制。

## 结构模型

### 元数据单一来源

继续使用 FieldDescriptor 表达 ID、标签、默认值、约束、编辑提示和语义。ValueDescriptor 增加可选的不可变结构关联，工作名 ValueStructure；它仅描述如何理解当前 wire 值，不创建第二种存储值容器。

优先提供 `WithStructure(...)` 一类返回新描述的组合入口，并保留现有公开构造函数签名。不能只在旧构造函数尾部追加可选参数并假定二进制兼容；插件契约、手写 schema 和生成代码都需编译/加载回归。

结构种类：

- **Scalar**：由现有 codec 完整处理的叶值，包括 string-encoded 数字/枚举。
- **Object**：有序成员描述，保留字段 ID、CLR 成员身份与实际 wire 名的映射。
- **Sequence**：元素字段描述，支持一维数组、List/IList/IReadOnlyList/IEnumerable 的现有范围。
- **Map**：字符串键和值字段描述；键是数据，不能当对象 schema 字段或数组索引。
- **Opaque**：没有可证明的结构，保留原值和未解析原因。

动态不是第六种存储类型，而是解析器根据值与只读上下文得到上述结构之一。初期不自动枚举 arbitrary IEnumerable 实例；遍历对象为 JSON 数组，CLR 集合类型只用于取得元素契约。

序列元素/映射值使用可携带 FieldValueSemantic 的字段描述，以便表达“列表中的每个字符串是文本 key”。不把父字段语义自动传播给所有字符串，也不放宽现有 LocalizedText / CharacterReference 只标记字符串字段的规则。自动推导得到的普通字符串元素保持无引用语义，显式集合语义由结构声明提供。

### DTO 与兼容 JSON 结构

原生 IFieldDto 使用现有 Schema；schema 解析集中到公共结构解析器，过渡期可对静态 Schema 反射一次并按作用域缓存。只读取 schema 元数据与 JsonTypeInfo，不构造作者 DTO 来遍历，不调用作者属性 getter/setter。自动生成器提供更直接入口可以后续演进，不要求首期大改 IFieldDto 接口。

固定 JsonElement 字段通过声明关联结构类型。例如 FieldAttribute 的拟增 `StructureType` 指向 LayerTransform 或 FlipbookDefinition，生成器生成 ValueDescriptor 的结构关联；手写描述调用同一组合入口。此关联保持外层 ValueType=JsonElement，不改变 wire 格式。

LayerTransform / FlipbookDefinition 可复用现有类并为可编辑成员增加生成 schema；Clone、Capacity、IsValid 等成员保留，未标记的计算属性不进入属性树。字段默认、标签等元数据从共享 schema 取得，专用控件不再复制一套字段定义。新增特性不应改变现有 JsonSerializer 对实例属性的输出。

### 命名与 codec

对象成员区分稳定字段 ID 和实际 wire 名：前者供注册/元数据查找，后者供读取、局部 patch 与 JSON Pointer。JsonPropertyName、命名策略、字段 converter 优先级和父级 JSON options 在公共解析入口统一处理。

对象/集合级自定义 converter 改变了结构时，不能凭 CLR 成员继续推断；没有显式结构适配则为 Opaque。成员级已声明 codec 与父级 options 的合并规则必须在索引和表单中相同。JSON Schema 导出继续表达实际 wire 契约，不因为可编辑结构声明就丢弃兼容表示。

Transform 等既有兼容字段采用 Contracts.Builtins 拥有的显式 wire 适配器：声明对象及该字段已支持的 JSON 字符串编码，按现有规则匹配成员大小写并保留原键。普通 DTO 的精确命名规则不因此变成全局大小写不敏感。重复 JSON 属性、大小写别名冲突和多义成员必须报告，不能选取第一个后继续写入。

普通 string 不尝试 JSON 解析。字符串包裹结构的逻辑子路径不是外层 JSON Pointer 能直接寻址的真实属性；诊断同时保留实际 wire 位置及结构内逻辑路径，编辑和导航经适配器恢复该边界，不能声称对外 JSON Pointer 可以直接穿过字符串。

## 属性快照、路径与迭代

Core 提供平台无关的 ValueDocument / ValueProperty 只读快照视图，工作命名以区别 Editor 项目对象。引用图描述递归类型，值属性按需展开，不预先构造整棵 schema 或值树。

属性视图至少包含：字段元数据、结构种类/解析状态、当前原始值、Missing/Null/Provided 状态、实际 wire 路径、逻辑路径及直接子项入口。有效默认值是独立的展示查询；引用索引只扫描实际作者值，UI 展开缺省对象不写回默认值。

PropertyPath 的片段具有 Member、Index、MapKey 及显式编码边界信息，键 `"0"`、空键、`"a/b~c"` 不得被误解为数组位置或多级成员。JSON Pointer 的转义/反转义由单一工具完成；原字符串路径和已归档草稿在外层适配，不能静默改变历史路径含义。

提供 FindProperty、EnumerateChildren、GetIterator/MoveNext 风格入口。迭代使用显式栈，允许访问者进入或跳过子项；每次访问受节点预算、深度与取消控制。命中未知值、坏结构、缺失映射或预算时产生有路径的结构结果；调用方将其转为对应 UI/工具诊断。

默认引用分析预算先保持现有深度 16、总值 100,000。UI 的八层/200 元素边界继续作为控件实例化策略，不等价于分析完整性限制。FindProperty 也需有界，不能用直接查找绕开对字符串解码和递归结构的保护。

快照根负责 JSON 生命周期；捕获时一次持有独立且不可变的值，不对每个子节点递归 Clone。未知成员可以读取/保留，但没有描述的非空结构不能被当成已完整检查。空值无引用的现有约定继续有效；结构校验与引用覆盖是独立结果。

## 局部修改与项目作者桥接

Core 的属性 patch 是纯操作：输入快照、类型化路径及 Set/Remove/集合结构修改，输出新候选值或带路径的问题。保持未知成员、兄弟值、原成员拼写和显式编码方式；不保证空白字节不变，不通过根 DTO 重建替换 JSON。集合元素不能用省略代替删除，映射键操作须单独检查重复与冲突。

省略对象上的显式子属性写入可建立必要容器，仅落入该操作要求的值；不自动创建不存在的第 N 个列表元素，不把 null 自动恢复为默认对象。重置/删除能力仍由宿主决定，Timeline 等禁用通用 Reset 的策略保留。

Editor 提供已有 FieldEditorContext / IFieldBinding 的属性绑定适配；工作名 SerializedPropertyBinding。项目级作者数据仍唯一归项目文档，未增加与之同步的可变 SerializedObject 副本。候选经当前字段规则、partial 领域校验及 EntryFieldAuthoringService 进入一条历史；准备失败不得部分提交。

EntryEditSession 的子字段草稿提交改用公共属性 patch；关闭表单只释放控件租约，不使作者 incarnation 失效。保存时仍能从当前作者状态重建属性访问并提交有效草稿。旧归档路径通过当前 schema 和 wire 规则明确映射，无法唯一对应时保留为不可恢复提示，不猜测目标。

读取快照的版本、写入方身份与控件 lease 分开：写入必须核对项目 scope、Group/Entry 稳定 ID、类型、作者 incarnation、根来源/相关依赖及集合结构代次。无关条目变更不必使当前字段输入全部失效；列表移动/删除、换类型、删除后 Undo 或动态结构切换不能让旧子属性写入新的位置。结构变化后旧索引句柄一律重新解析，不将 JSON 相同视为身份相同。

## 校验与引用分析接入

共享结构访问允许统一执行每个已提供叶值的 ValueValidation，并在对象层检查形状、必需项和重复成员。提供作者 Partial 与完整检查的明确模式：逐字段编辑不强迫其他尚未填写的字段完整；完整编译规则及领域业务校验继续由原契约入口执行。

现有 ValueValidation.Validate 的行为不通过本次迁移被全局偷偷加强。可添加基于属性树的嵌套检查入口，再由已迁移调用方显式使用；默认值和约束校验与实际 wire 解码须一致。ValueCodec 继续负责最终类型转换；可描述结构不代表任意 converter 能安全跳过，也不能把标量检查成功当作嵌套业务有效。

LocalizationReferenceIndex 改用公共迭代：识别有语义的字符串叶值，未知/无结构处生成现有分析未覆盖结果；缺失文本/角色继续独立报错。保持 Group/Entry 身份和属性路径，不再根据“成功找到的引用”反推诊断位置。未知成员不因其他兄弟字段已知而被吞掉。

Transform 的正常已知成员可完整遍历，索引不再报 field.opaque。若 Transform 内含未描述的非空扩展对象，仍报告该扩展路径未覆盖；其未知值必须在编辑后保留。字段格式损坏或叶值类型错误仍可被明确定位。

Character 删除/替换等依赖 Complete 的行为保持保守；迁移后的 Complete 只反映引用分析覆盖，不能复用为编译成功状态。

## 控件与动态场景

### 默认分组展示与专用覆盖

2026-10-27 用户确认：保留现有专用编辑体验；没有专用覆盖的结构使用紧凑分组表单。结构元数据统一不意味着所有控件都必须呈现为同一种树形布局。

| 节点 | 默认展示 |
| --- | --- |
| 标量叶字段 | 标签与编辑器一行，诊断紧随字段，不额外建立对象 Header |
| 对象 | 单一可折叠 Header，下方缩进展示子字段；沿用简短对象默认展开、复杂对象按需展开的策略 |
| 序列 | Header、当前数量及新增操作；简单元素直接展示编辑器，复杂元素使用可折叠标题，提供删除/移动操作 |
| 字符串键映射 | Header、数量及新增入口；内部为键/值行，操作明确区分改键与改值 |
| 未知或暂不支持的结构 | 保留明确的 JSON 编辑/修复入口及相关诊断 |

Header 使用本地化字段名称，父字段行与内部对象标题只保留一份名称，不再同时堆叠字段标签、类型名标题和 element 标签。复杂集合元素可显示序号或已有领域摘要，不以统一布局为由新增稳定身份。

同组字段采用紧凑间距，分组之间增加一档间距；层级通过 Header 和缩进表达。间距由宿主主题/样式统一控制，不在每层插入空白行或随深度累加底部空白。实际尺寸沿用宿主样式，在 P4 挂载验证中检查深层/窄窗口的密度与可读性。

Transform、Flipbook、颜色、资源选择及 Timeline 等专用交互继续保留；本次迁移范围内的专用控件共享结构访问与字段规则，布局仍由各自工厂负责。未迁移的专用控件不因保留展示而宣称已经接入共享遍历。

### 属性绑定与动态解析

StructuredFieldEditor 的成员映射、集合判断、子属性读取及 patch 使用公共结构入口；仍负责懒加载、样式、控件数量限制及 JSON 修复入口。字符串键字典使用独立键/值行和显式新增/删除/改键操作；禁止静默覆盖重复键，键编辑失败时保留草稿。

Transform/Flipbook 保留当前专用布局，子输入仍按 registry 选择 NumericUpDown、Slider 或精确文本，并转发 HostContext、CanReset、草稿与本地化能力。EditorRegistry 的引用语义、成员、类型、EditorId 和 DTO 覆盖顺序保持原行为，结构解析不依赖控件注册。

音效 apply 的类型来自 id，update 的参数类型可能来自前缀查询的实例候选；Timeline 事件的 Parameters 来自事件 Type 对应目录。因此本次不通过一套字段名列表将这些对象宣称为固定结构。预留 IValueStructureResolver 的纯查询接口，输入描述、属性位置与调用方提供的不可变上下文，返回结构/未解析原因和依赖令牌；Core 不认识项目资源、effect 或 SceneTargetCatalog。

未来迁移时，目录读取和异步资源准备由现有项目查询服务完成，worker 遍历只消费其冻结结果。动态结果变化使相应写入上下文失效，不以全项目 revision 代替局部依赖。当前不更改这些领域的专用编辑器，也不把未接入归为通过检查。

## 模块职责与数据流

| 模块 | 新增或收敛的职责 | 保留的边界 |
| --- | --- | --- |
| Core.Parameters | ValueStructure、成员映射、属性路径、快照访问、有界迭代、纯 patch 与可选嵌套检查 | 无 UI、DI、项目 I/O、内置类型依赖 |
| SchemaGenerator | 输出固定结构关联，校验声明，复用既有字段元数据 | 不执行构造函数或领域逻辑 |
| Contracts.Builtins | Transform/Flipbook schema 与历史 wire 适配，相关 Entry 字段关联 | 原运行数据及业务算法 |
| Editor.Shared | 只读引用索引消费公共遍历，保留作者地址 | 不持有 Avalonia 控件 |
| Avalonia.Controls | 公共结构的字段绑定、通用对象/集合/映射展示 | 保存和历史由宿主承担 |
| Editor | 专用控件、草稿提交桥接、生命周期、诊断导航 | 项目文档仍是作者数据唯一来源 |

数据流：契约描述 → 公共结构解析 → 当前 JSON 属性快照 → 表单/嵌套检查/引用分析；作者操作 → 属性 patch → 现有提交与领域验证 → 历史/保存 → 新快照及自动检查。

元数据解析器有明确作用域，缓存键至少区分类型、冻结 JSON options 身份和显式结构关联；不只以 Type 缓存。结构定义延迟引用递归类型，不在静态 schema 初始化时构造循环。避免全局强引用插件类型/委托，项目卸载释放解析器、动态上下文和旧快照；后台检查应在释放契约前结束。

## 兼容、迁移与验收

首期新增能力不要求用户迁移项目文件。旧公开构造函数、schema 入口、控件注册与 codec 规则保留；需要新增元数据时同源码重建默认契约，并核对 Editor 打包副本。源码兼容与二进制兼容分别验证，不能把一次解决方案重建当成旧插件加载证据。

先建立结构/路径基础并覆盖命名与 converter，再接固定内置结构和引用分析；随后迁移专用属性写入与草稿，打通 Transform 闭环；再迁移通用对象/集合/字典表单。每类迁移完成才删除其重复反射/字段清单，不混入格式化或全局重命名。

验证重点是同一数据在多个消费者中的一致性：包括嵌套 per-field codec、引用语义、默认/presence、大小写冲突、字符串编码、未知扩展、列表结构变化、MapKey 转义、旧草稿、契约卸载，以及挂载后的真实输入和导航。测试计划见 phase-plan；本设计阶段不运行或宣称构建/测试结果。

正式规范仅在实现验证后同步至 entry-types、control、editor-game-localization、editor-groups 与 editor-history-and-save。后续动态迁移及尚未实现的 UI 能力明确留在 feature 记录。

## 取舍与风险处理

- 采用共享属性层，避免仅为引用索引特判字段；仍复用 System.Text.Json，控制迁移范围。
- 允许 JsonElement 搭配显式结构适配，避免强制 DTO 往返丢失扩展和 presence；代价是必须测试 wire 与逻辑路径的边界。
- 保留显式 schema，避免反射所有 public 成员；计算属性和运行属性不成为作者字段。
- 分开 Partial 编辑、结构有效、引用覆盖和领域完整检查，避免统一逻辑后扩大保存/提交限制。
- UI 与扫描有独立预算，防止“界面回退 JSON”被误判成分析失败，或分析器生成大量控件。
- 动态目录不在当前交付全量迁移，限制跨领域重构扩散；仍有未覆盖警告的场景必须在总结中如实列出。

参考：[feature](feature.md)、[前期讨论](../F-20261023-01-editor-uiux/design-recursive-properties-20261027.md)、[字段契约](../../docs/spec/entry-types.md)、[共享表单](../../docs/spec/control.md#schema-字段表单)、[本地化索引](../../docs/spec/editor-game-localization.md#只读作者索引)。
