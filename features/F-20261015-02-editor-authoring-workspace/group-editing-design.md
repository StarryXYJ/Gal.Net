# galgroup 编辑交互与控件扩展补充设计

日期：2026-10-17。状态：design；P5/P6 尚未实施。来源：用户追加 6 点要求。本轮仅修改设计与计划，第 6 点的注册服务、Locator、timeline/曲线控件及 Sample 迁移均不实现。

## 现有基础与问题

- EntryTypePicker 已是 ToggleButton + Popup + 分类 TreeView，并非必须替换成原生 ComboBox；当前打开时焦点落到树，没有搜索。
- EntryListEditorControl 顶部只有添加，底部有数量/批量追加。Group VM 的 Add 在选择后插入、无选择时末尾插入；Append 始终末尾。新需求调整布局，并统一类型选择的插入意图。
- ParametersToggle 在模板中写死 IsChecked=True，拖动预览重新使用 ItemTemplate，重排/虚拟化也可能重建容器。折叠状态必须离开容器，不能靠保留控件实例修补。
- 现有 SchemaForm、EditorRegistry、IFieldBinding、FieldEditorContext/Lease 已供 Editor 和 Sample 设置页复用。EntryDefinition.FieldDataType 保留根 DTO Type，FieldDescriptor.Value.ValueType 保留字段 Type，可以接类型注册；共享表单不拥有项目历史。
- EditorRegistry 当前按显式 EditorId 或值类型默认工厂解析，不具备整个 DTO 面板替换、按 DTO 成员替换及场景目标分析。AnimationPlanOptions.Plan 当前是 JsonElement，不能直接按 AnimationPlanDefinition 类型命中 timeline。

## 1. 可搜索的类型选择器

保留现有分类展示及快捷键提示，弹窗顶部增加搜索框。每次打开清空上次搜索并直接聚焦搜索框，初始不预选结果；可见文本为当前 UI 语言的名称，旁边始终显示稳定英文 type ID，缺译时使用 ID。搜索按当前显示名称和完整英文 ID 忽略大小写匹配，空查询显示全部。

拼音作为轻量别名：内置中文名称可提供人工确认的全拼/首字母别名；自定义类型可选贡献别名。没有别名也能按名称/ID 查找。不新增大型转写依赖，不承诺多音字推断、声调、纠错或模糊中文分词。语言切换重新建立显示名索引，别名不修改作者内容。

键盘状态分为查询和高亮：输入改变后清除旧高亮；首次 Up 或 Down 都高亮首个可选叶结果，后续 Up/Down 向前/后移动，边界停住，跳过分类标题。搜索框可保持文本焦点并处理导航键；滚动确保高亮项可见。Enter 仅确认当前高亮，零结果/无高亮不创建；鼠标点击叶项直接确认。Escape/外部关闭不改内容。IME 正在组合时不拦截其 Enter/方向键，空格在搜索框中输入文字。

统一组件暴露确认事件/意图，区分两个用途：条目行内选择器确认会更改该 Entry 的类型；顶部/空格插入选择器确认会新建 Entry。不能用 SelectedValue 的属性变化触发新建，防止初始化、重开及选同一类型造成意外行为；重复确认相同类型仍能连续插入。

## 2. 顶部工具区与插入规则

布局顺序：添加条目 | 追加条目 | NumericUpDown | 类型选择器。使用文本按钮，保留无障碍名称和四语说明；窄 Dock 允许自然换行。删除原底部工具区域。

“添加条目”默认在当前活动 Entry 后插入，无选择则追加末尾；“追加条目”无论选择始终追加末尾。类型选择器直接确认时，在活动 Entry 后插入所选类型一条，无选择则末尾一条。新建后选中新项并滚动到可见区域；批量创建后活动项为最后一个。一个批量意图一条项目历史记录，分配全新稳定 ID，不使用 catalog 第一项作为隐式默认类型。

用户已确认：数量作用于添加/追加两个按钮，类型选择器直接确认始终一条。普通按钮使用显式配置的默认类型（优先已确认的插入类型，无配置则内置 dialogue.text；不可用时打开类型选择器），不猜测契约首项。

P5 计划保留多选能力时，插入锚点使用最后活动项，其他选中项不被更改；活动锚点删除/移动后按稳定 ID 重新检查。弹窗打开时捕获插入锚点，不因按钮获取焦点丢失锚点；锚点失效则取消插入并提示，不悄悄插入其他位置。

## 3. 折叠、拖放与选择

页面状态按 (Group DocumentId, Entry StableId) 保存 IsParametersExpanded，不写作者 JSON、不进入内容 Undo、不产生 dirty。新 Entry 默认展开；旧 Entry 重排、同页 Undo/Redo、容器回收与拖动预览读取同一状态，复制生成的新身份使用默认状态。关闭页面释放页面状态，跨重启不承诺保留。

ListBox 内容尾部留约 64 DIP 可点击空白，列表铺满也能滚动到这块区域；不用伪 Entry 充当占位，避免影响序号/重排/编译。点击本 Group 编辑区内 Entry 行之外的背景会取消选择。工具区、弹窗、滚动条和行内控件不触发背景取消，其他 Dock 页面不会通过全局点击监听清空此页选择。清空后新建以末尾为锚点。用户拖动过程中不执行背景取消。

## 4. 空格快速插入

Group 内容页面或列表拥有键盘焦点时，Space 打开与顶部同一套插入类型弹窗：有活动 Entry 则锚定该行边上，无选择则锚定列表尾部空白；必要时先滚动到锚点。确认插入一条后关闭弹窗，选择新项并恢复列表焦点；取消恢复原焦点，不改内容。

TextBox、NumericUpDown、条件/表达式控件、已有类型搜索框和其他正在编辑的控件先处理空格；不通过全窗口 Tunnel handler 抢键。按键重复只保持一个弹窗。没有选中项时“页面有焦点”指该 Group 页面内部键盘焦点，不是鼠标悬停或后台 Dock 页。

## 5. 第 6 点：默认生成表单与两层控件扩展（仅设计）

默认继续用现有 SchemaForm 及 schema/binding 生成全部参数 UI。注册服务放在共享 Avalonia.Controls Forms 层，不将 Avalonia Control 引用加入 Core/契约 DLL。宿主在应用入口显式组合内置及自定义工厂；Sample 可在 SampleCustomization.ConfigureServices/CreateFieldEditors 所在组合点采用同一方式，Editor 与 Sample 使用各自宿主/项目 scope 的注册集合。

建议公开 API 形态（设计名称，非已实现接口）：

~~~csharp
editors.RegisterValue<AnimationPlanDefinition>(context => CreateTimeline(context));
editors.RegisterField<AnimationPlanOptions>(dto => dto.Plan, context => CreateTimelineJsonAdapter(context));
editors.RegisterDto<MyOptions>(context => CreateCustomPanel(context));
~~~

这三种用途不能混为一个映射：

| 粒度 | 命中条件 | 替换范围 |
| --- | --- | --- |
| 成员 | 根/当前 DTO 类型 + 强类型成员路径 | 仅该字段位置；其他字段继续自动生成 |
| 值类型 | 精确字段 CLR 类型 | 所有该类型字段的单个编辑器，例如曲线/颜色/结构对象 |
| 整个 DTO | EntryDefinition.FieldDataType 或设置对象根类型 | 整个参数区；自定义面板可嵌入自动生成部分 |

解析顺序：先查根 DTO 面板映射；未命中则创建自动表单。自动表单每个字段按成员映射 → 值类型映射 → 现有 EditorRegistry（显式 EditorId/标量默认/JSON 回退）解析。类型覆盖是宿主明确注册的替换意图，因此可覆盖内置 hint。nullable 保留原 presence/null 能力，控件选择可解包基础类型；转换型 wire 字段仍走其 ValueCodec。采用精确类型匹配，暂不做继承/接口优先级猜测；重复注册报错，替换使用显式 Replace，注册结束冻结。EditorId 留作内部/旧 hint 适配，普通扩展作者按 Type/成员注册，不要求手填 editor ID。

整个 DTO 控件并不要求先反序列化一个完整可变 DTO：未完成 Entry 可能缺必填字段。Context 应提供 schema、只读快照、字段/子路径 binding、原子 patch 提交、校验/诊断和 lifetime。任何控件都通过同一 binding/项目事务提交；未知字段、缺省/null/default 保留，禁止用新 DTO 整体序列化覆盖作者 JSON。一个 timeline 拖动手势合并为一条撤销，临时预览不直接写作者状态。

需要混合展示时，自定义根控件可调用“创建默认表单（排除已负责字段）”，只跳过根面板映射以避免递归，字段映射仍有效。嵌套对象递归时维护成员路径，深度/循环类型给出反馈；每次生成独立 Control/Lease，schema 切换、行折叠、卸载取消旧提交并释放订阅。注册工厂失败显示诊断，不静默变为成功的空控件。项目契约类型的映射随项目释放，避免应用单例持有 Type 导致程序集无法卸载。

AnimationPlan 的现状取舍：Plan 是 JsonElement 时，第一步通过 AnimationPlanOptions.Plan 成员映射和适配器转换已知字段，不能对所有 JsonElement 全局替换 timeline；以后若改为强类型 AnimationPlanDefinition，可自然使用值类型映射。曲线字段既可只替换嵌套的曲线值，也可由整个 timeline 面板组合，统一复用参数面板和提交协议。真正 timeline/曲线编辑器是独立可验收工作，不因增加 registry 就宣称已完成。

## 6. 第 6 点：场景目标与 Locator（仅设计）

资产库资源（文件/asset ID）与场景实例（某次创建、替换、隐藏后的对象）不是同一身份；一个图片资源可被多个层使用。当前 Core 有 IAssetManager 和 ISceneInstanceManager，尚未查到可直接复用的作者级 Locator 类型。可复用资源选择器的搜索/缩略图界面及底层 asset 索引，但场景目标候选必须来自独立场景分析服务。

建议作者侧提供 SceneTargetRef/Locator：以稳定创建 Entry ID + 输出槽位标识对象，显示友好名称/资源缩略图/对象类型；创建类 Entry 自动分配内部 handle，引用类 Entry 选择目标，不要求剧情作者手输 handle 字符串。复制创建 Entry 分配新身份，同批内部引用重映射，重排及文件改名不改变身份，删除生产者显示断链诊断。禁止按“资源名/GUID 就是目标”解析多实例。

没有游戏预览时，用当前 Entry 之前的作者数据做可解释的静态分析；每种创建/替换/删除等 Entry 由显式贡献的纯场景语义提供器更新抽象场景，不执行游戏命令。条件、分支汇合、循环、跨 Group 和未知自定义 Entry 下区分确定存在、可能存在、无法推断；不伪造确定的运行时状态。未知贡献者返回 unknown，保留已有引用并给出诊断，不能在候选刷新时清空值。后期运行时观测可增强候选，但引用保存仍使用稳定作者身份。

用户已确认隐藏范围为使用 Editor 创作演出/剧本的人；底层继续使用 handle，C# 执行 API 不要求改为 Locator。优先让 Locator 作为 Editor 的目标选择模型/解析服务，创建 Entry 的 handle 自动由稳定作者身份与输出槽位分配，引用选择后写入对应内部 handle；现有 Raw/Compiled handle 字段可以继续沿用。需要持久作者引用元数据时，再单独设计 codec/compiler 的一致性，不预先强制修改格式。多对象歧义、引用到未来对象、无法反查的已有目标与跨 Group 生命周期都必须显示诊断，作者界面展示未解析目标而不是强制手输 handle。自动分配/编译映射的具体方案在实施前验证碰撞、复制重映射及身份稳定性，不把隐藏文本框等同于目标系统完成。

## 验收与阶段归属

P5 承接第 1–5 点：真实弹窗输入/焦点/IME/UpDown/Enter/Escape、当前四语名称和英文 ID、重复选同类型新增、顶部布局/数量、背景取消/末尾插入、跨页选择隔离、折叠后拖放与虚拟化/Undo、每次完整插入一条项目历史。拼音仅验收明确声明的别名，不承诺全汉字转写。

第 6 点保留为 P6 的设计输入，本轮不修改 Forms、Sample、契约或 Runtime。P6 未来实现先完成注册/解析/lease/binding 的小型端到端切片，再分别安排成员/根 DTO 替换、场景引用、timeline/曲线；不把完整场景分析、任意自定义 Entry 推断和专业动画工具塞进同一个最小阶段。Sample 采用同一 API 可行，其设置页继续使用设置 binding，Editor 使用作者文档 binding，二者不共享历史/业务会话。

用户已确认数量作用域和 handle 隐藏边界，没有阻塞本设计的待答问题。P5/P6 状态仍为 planned；本次文档提交不代表相关控件已经实现。
