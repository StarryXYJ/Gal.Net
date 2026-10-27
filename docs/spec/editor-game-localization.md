# Editor 游戏本地化基础

当前实现包含字段语义、项目索引、受管理本地化文档、角色表/speaker 输入、默认语言正文/分支编辑与稳定复制身份、多语言表格、语言管理、JSON 交换及发布前本地化检查（F-20261019-01 / P1–P6）。只支持当前作者及模板格式，没有旧 Speakers、角色迁移或旧版本模板入口。

## 字段值语义

Core.Parameters 提供 `LocalizedTextAttribute`、`CharacterReferenceAttribute` 和 `FieldValueSemantic`。

- `[Field(...), LocalizedText]` 声明字符串成员存储游戏文本 key。
- `[Field(...), CharacterReference]` 声明字符串成员存储角色 ID。
- 未标记成员语义为 None，不根据名称或 EditorId 推断。

两种标记互斥，generator 对非字符串、没有 Field 的成员或冲突标记报告 GNF001；支持继承字段。生成的 FieldDescriptor.Semantic 独立于字段 UI 的 LabelKey/DescriptionKey 和 EditorHints。手工 schema 可通过 FieldDescriptor 构造函数的 semantic 参数声明，同样检查类型与枚举值。

DialogueTextOptions、NvlAppendOptions、TextVoiceOptions、NvlVoiceOptions 的对应成员已经标记。标记保留在动态参数表的 FieldDescriptor 中；项目 schema 查询的参数描述提供 Semantic 字符串。

ValueCodec、编译和运行格式不变：字段仍保存原 key。自定义运行指令需自行调用 ITextResolver，标记不隐式改写运行 DTO。

## 只读作者索引

Editor.Shared 的 LocalizationReferenceIndex 分析一个 EditorProjectDocument 与给定 LocalizationSource 列表：

- 遍历全部 GroupEntries，不依赖页面是否打开；定位包含 Group ID、Entry StableId 和 JSON Pointer。
- 对 Choice 分支使用稳定 option ID，显式索引 Text；角色 catalog 的 DisplayNameKey 也进入文本引用。
- 通过 Core.ValueDocument 的有界迭代分析带静态 Schema 的 IFieldDto、数组/List/IList/IReadOnlyList/IEnumerable、字符串键字典和显式关联 ValueStructure 的 JSON 字段；列表下标只是导航位置，索引不生成或重算文本身份。
- 缺省、null、空字符串和纯空白字符串不生成引用，不补默认值；空 LocalizedText key 表示有意的空文本，启动时不要求对应译文，运行时解析为空字符串。空对象和空数组也不包含引用，不因缺少 schema 或未知字段而阻止分析；非空的未知字段/类型、不可遍历结构与错误 shape 仍报告分析不完整。
- 深度上限 16，整个分析的值预算 100,000。超限产生 analysis.limit，不能以未找到引用认定角色未使用。

内置 Transform、ToTransform 与 Flipbook 字段关联其可编辑子字段的共享 schema，正常非空值不再因为 JsonElement 外层类型而产生 field.opaque；未知非空扩展仍报告未覆盖。Transform 的历史字符串编码由显式结构适配解释。LocalizationReference / LocalizationIssue 的 Path 保留带编码边界的类型化逻辑路径；JsonPointer 指向实际外层 wire 位置，不伪造穿过 JSON 字符串的 Pointer。Problems 使用逻辑路径定位内部字段。动态效果参数、Timeline 及其他未关联结构的 JSON 字段仍按原覆盖规则报告，未声称全量迁移。

引用的 LocationKey 使用逻辑位置区分同一外层字符串中的多个叶值，Rewrite 使用类型化路径产生新参数快照并保留编码。角色引用比较/替换、文本草稿/复制写入以及本地化来源显示已接入该位置；旧公开 Pointer Set 入口保留，新的引用回写不再以外层 Pointer 猜测内部地址。

I18n/catalog/characters.json 的角色按 Ordinal ID 检查；允许姓名译文相同。I18n/<culture>/**/*.json 支持平铺与嵌套字符串叶值，按文件路径 Ordinal 排序并保留每个来源及属性位置。FindDirect 返回同语言最后生效的定义；缺少直接翻译返回 null，不做运行时回退。显式空串与缺失独立。External 不进入作者索引。

重复 JSON 属性、同文件扁平/嵌套 key 冲突、非字符串叶值、坏 JSON/无效 culture、无效 catalog 均有诊断，其他可读取文件仍可查询。未知角色和默认语言缺 key 单独报告；如果引用位置已经全部分析，这类目标不存在错误本身不会使 Complete 变 false。Complete 只描述分析覆盖程度，不代表项目可运行。

## 项目会话查询

ProjectLocalizationQuery 已注册为 Editor 项目 scope 服务。Get 捕获当前 Working 文档，通过 ProjectRawReader 读取尚未受管理的 I18n 来源，再以受管理文件的内存内容/删除状态覆盖磁盘来源。新建、未保存修改和删除因此可被同一索引观察；查询不写盘。

查询按作者快照和来源内容缓存分析并提供 Revision；文档卸载或服务 Dispose 时清缓存，已释放查询始终返回 null。没有 I18n 目录的项目可正常查询。文件读取或枚举失败进入不完整诊断，文件系统链接按现有路径策略拒绝。

当前是按需查询：每次 Get 会捕获文档和读取来源以判断变化，尚未实现文件级增量性能优化。该服务不是文本编辑/草稿提交入口。

## 受管理作者文档与写入

LocalizationDocumentStore 捕获文本编辑目标和文件快照，LocalizationEditOperation 执行可逆的字节补丁，GUI 的 LocalizationAuthoringService 将整个操作接入项目内容历史。快照和历史不持有页面；项目 scope 释放后拒绝继续修改。

专属文件入口只允许 I18n/<culture>/**/*.json、I18n/catalog/characters.json 和 Graph/localization-authoring.json，拒绝 External 与其他受保护路径。已有只读跟踪文件可被专属入口提升为受管理文档，提升时核对磁盘字节并建立保存基线。catalog 和作者 metadata 仍不能从普通文件菜单修改或删除；普通 JSON 页面也不因其他服务提升文档而获得编辑它们的权限。

文本编辑选择当前生效的包内定义，沿原有嵌套属性路径替换值，保留其他叶值和原文件编码/BOM；新 key 默认写 I18n/<culture>/authoring.json，调用者可指定分片。重复属性、扁平/嵌套冲突和非字符串叶值在写入前拒绝；未知 catalog/metadata 字段保留，不承诺保留原空白格式。

已有文件的普通值修改只更新 Working，在 Save All 才写盘。包含新文件的操作通过双版本结构事务建立有效 JSON 的空保存基线（文本 {}、catalog []、metadata 初始对象），同时只在 Working 应用实际内容，不顺带保存其他 dirty 字段。撤销创建会删除该结构文件；重做恢复撤销当时的 Saved 内容，Working 仍按当前历史意图恢复，避免保存之后用旧空文件覆盖新基线。外部冲突、事务补偿和 RepairRequired 复用项目会话机制。

## 作者 metadata 与角色编号

Graph/localization-authoring.json 使用 version=1、characterHighWater=非负 Int64；其他字段可保留。ReserveCharacterKeyAsync 结合 metadata 及已有 character_<数字> 角色 ID 的最大编号，通过作者文件事务预留下一个编号，返回 character_<数字>。

预留同时推进 Working/Saved 的计数，保留各自其他内容，不是可撤销的创作操作，也不保存其他未提交字段。后续角色创建可单独形成一条历史；撤销或创建失败允许留下编号空洞，不回收编号。溢出与发布失败拒绝分配。只有实际调用预留 API 才会分配编号，P2 不自动建立角色记录。

## 普通 JSON 草稿协调

文件缓冲通过 RegisterFileDraft 注册文件身份、提交和结构校验。CommitFileDraftsAsync 只校验并提交相关文件的有效草稿，其他文件或设置草稿不被顺带提交。Save All 的普通文本提交仍允许保留作者的待修复原文；结构化编辑前的严格校验不改变普通文本保存策略。

结构化文字更新按原目标 key、文件身份和期望原文检查冲突；同文件其他叶值的有效草稿可先提交，再在新文档上修改目标文本。相同目标值已变、有效来源已换文件或旧行已过期时拒绝修改。JSON 草稿的提交与随后结构化修改是两条各自完整的历史，不伪装成同一个输入操作。

文档刷新只有在缓冲无草稿或内容等于刚提交值时同步 Text；存在冲突草稿则保留输入及原字节基线、显示冲突，并阻止它覆盖新文档。取消草稿后重新读取当前受管理文档，控件随绑定刷新。受管理文件即使尚未保存，也从内存打开，不依赖旧磁盘字节。

同一路径可以保留多个删除身份和一个有效身份；保存哈希期望与外部冲突检查优先唯一有效记录，无有效记录才使用删除遮罩。不会因为旧删除记录而误拒绝同路径新文件保存。

## 角色表与 speaker 创作

角色 Dock 是项目级单例文档页，可从面板菜单打开，关闭后释放订阅。列表显示开发语言姓名与较弱显示的角色 ID，搜索同时匹配两者；右上角“＋”开始新建并聚焦姓名，应用/还原/删除为带提示的图标按钮。编辑区仅在选中或新建时显示，未应用状态单独标记；删除禁用时说明引用或分析不完整的原因。

CharacterPanelState 属于项目 scope，以角色 ID 与 DisplayNameKey 的组合保存草稿及原声明基线；姓名文本 key 在会话内始终唯一时，可据此跟随角色 ID 改名，应用仍检查原声明基线。曾共享姓名文本的角色不通过文本 key 猜测身份，互不继承选择或草稿；新建草稿单独保存。换项、搜索过滤及关闭重开不丢失输入。Save All 仅保存已应用的角色作者值，草稿不跨项目或应用重启恢复。

异步应用完成时只清理与本次提交快照相同的草稿；期间换项不抢回选择，关闭重开后产生的较新输入也不会被旧视图清理。应用或还原已有角色不顺带放弃独立的新建草稿。

CharacterAuthoringService 以 catalog 为唯一角色身份来源，以项目 TargetLocale 的直接姓名译文为显示值；编辑器 UI 语言不改变作者姓名。允许同名、Ordinal 唯一 key；新角色不填 key 时使用持久预留的 character_n。修改角色 key 更新全部已声明的 CharacterReference，包括未打开 Group 与嵌套 DTO/列表中的实际 JSON 路径；姓名 DisplayNameKey 不变，其他语言译文保留。

新建记录、姓名译文及 speaker 引用构成一次项目历史，保存前只修改 Working。第一次需要文件时，先发布空 catalog 和开发语言 characters.json 保存基线；初始化文件和编号预留不随值历史撤销，允许空文件与编号空洞。保存后撤销创建只还原 Working 值，下一次 Save All 整体落盘，不删除仍服务于 Saved Group 的文件。未使用角色只能显式删除，撤销可恢复；删除保留姓名文本。引用分析不完整时禁止删除和改 key，仍允许修改姓名。角色面板显示前五条不完整诊断的代码、Entry、位置和说明；尝试删除或改 key 时记录包含最多二十条原因的结构化 warning 日志。

标记 CharacterReference 的字符串字段使用专属输入框，根字段与嵌套表单共用语义注册。候选按姓名/key 不区分大小写匹配，最多显示 50 项；选择提交精确 ID。搜索后默认高亮第一项，候选打开且输入非空时，在输入框按 Enter 可直接采用高亮角色；也可用向下键进入候选列表后按 Enter 确认。候选关闭或没有匹配时的 Enter、直接输入失焦仍按精确 key、唯一精确姓名的顺序解析；同名歧义要求选择，其他非空文本创建角色。空白输入清空引用，不创建角色。输入法组合期间不拦截 Enter/Escape，组合完成后 Escape 还原草稿；过期表单、取消或已移除 Entry 不能继续创建角色。普通未标记字符串保持原提交行为。

角色身份只来源于 catalog，默认项目配置不包含 Speakers。没有旧角色候选、旧姓名迁移或导入兼容入口；未知 speaker ID 报告 character.unknown。原文/JSON 草稿冲突沿用受管理文档的校验与保留规则。

## 默认语言文本与稳定身份

LocalizedText 字段使用 GameTextAuthoringService 与专属编辑器，读取项目 TargetLocale 的直接文本；UI 语言切换不改变作者值。缺译显示提示，不显示 key 或其他语言的回退文本。共享 key 显示引用数量，修改影响全部共享位置；未标记字符串保持原值语义。

初次提交生成不依赖位置的 GUID 文本 key，正常修改沿用原 key。Entry StableId、选项 id、文本 key 是不同身份；移动 Entry/列表项搬运含 key 的原 JSON，不能按新位置重算。null、缺省与显式空译文分别保留；打开表单不创建文本，清空文本并明确提交得到空字符串译文，移除字段不自动删除翻译。

单行 Enter、多行 Ctrl+Enter 和失焦提交，普通多行 Enter 输入换行；Escape 还原草稿，IME 组合态不提交。控件固定首次输入时的原引用、原文与开发语言基线，引用重绑定、原文变化、切开发语言、删除或关闭项目后，过期草稿不能覆盖当前值。根字段和嵌套 DTO/列表走同一语义提交边界；原文、复核状态与引用更新是一条历史。角色与正文在同一次 DTO 提交中也保持单次历史。

Entry/Graph 剪贴板 version 2 保存作者数据及各语言当前生效的包内文本快照，旧剪贴板直接拒绝；External 和被遮蔽的定义不作为复制译文。复制批次为原文本 key 分配新 key，保留批次内共享关系，各语言文本和已有复核状态一起复制。普通字符串、未知 JSON、nullable/default 保留，角色 ID 仍共享；跨项目复制文本使用快照，缺角色仍按当前项目诊断，不自动迁移 catalog。

列表的“复制项”使用相同语义规则。副本不会按相同原文自动合并；原本缺开发语言文本的副本仍缺译，不伪造空字符串。复制后的移动和保存重开保持新 key，撤销/重做恢复同一身份。

新翻译/metadata 文件先建立空 Saved 基线，实际值与复制内容进入 Working。含本地化引用的 Graph 复制在结构发布时使用空 Group 内容/空选项文本作为 Saved 基线；Working 持有完整副本，Save All 才整体发布，避免撤销→保存→重做时提前发布缺少译文的新 key。历史不持有控件或输入草稿。

## 原文指纹与复核状态

Graph/localization-authoring.json 的可选 textStates 记录每个文本 key 的 sourceLocale、sourceFingerprint 和 translations（locale→原文指纹或 null）。指纹为原文 UTF-8 的 SHA-256，不复制译文值作为第二作者来源。源文变化保留各语言译文；有旧原文关联的译文标为需复核，没有关联证据时保持未知。角色姓名修改复用该记录，复制保留已有状态，跨开发语言复制按目标开发原文建立状态。

字符编号预留和文本历史共享 metadata 文件，但 characterHighWater 不可撤销。文本历史只对该字段允许单调重基，应用 before/after 时保留当前更高预留值；其他 metadata 内容仍严格检查。不能为了撤销文本而回退编号，也不能借此忽略其他冲突。

## 游戏本地化面板

Localization Dock 为项目级单实例，可通过视图菜单打开。左侧包含角色表、全部作者 Group（含未打开页面）和分支选项；分类与文本行使用 ListBox 虚拟化，表格整体横向滚动。定位列保持在左侧，和表格使用同一个不透明背景，不显示“上下文”标题。Group 左侧显示当前 Entry 编号（如 Entry1）与字段槽位，编号沿用当前 Group 数据；提示也使用该编号，不显示稳定 Entry ID。右侧按钮垂直居中；分支显示名称、选项身份及槽位；角色显示开发语言姓名，完整身份在提示中查看。正常表格不显示文本 key。

同分类共享 key 合为一行，提示引用数量，定位时可选择引用位置；修改影响所有共享位置。搜索匹配文本与完整定位信息；“仅缺译”检查当前显示的非开发语言列，任一列缺译即保留该行，显式空串不算缺译。不再提供草稿或复核状态筛选，也不为每格显示状态文字。

工具栏仅保留搜索、缺译筛选、语言管理、导入和导出。语言管理列出已有语言名称和代码，勾选控制显示列；开发语言固定显示且排第一。候选输入框按代码、本地名称和英文名称搜索 CultureInfo 预设，排除已有语言，支持有效代码直接新增；选中候选或失焦不会隐式添加，点击“＋”才新增。I18nLocale 仍使用标准大小写，包括脚本子标签。语言调序与开发语言切换集中在管理器，不再占据每个列头。列宽可拖动调整，列可见性和宽度只属于项目会话的视图状态，不写入作者历史。

移除语言只修改 AvailableLocales，保留已有翻译文件和指纹证据，再添加时恢复其直接文本；不是物理删除文件。开发语言必须先切换才能移除。移除入口先等待该语言的正在更新输入，失败则阻止移除；语言结构变化清除导入预览，旧单元格写入仍在历史执行边界检查支持语言。添加、移除、调序及开发语言切换均可项目撤销，保留其他设置。

切开发语言时，在对应语言的操作菜单检查，按需显示缺译数量及应用按钮；执行时重查预览证据。缺译数量不代替完整项目检查，不自动翻译或复制文本。项目设置页面的原开发语言字段只读。

单元格浏览时显示紧凑的两行文字，活动单元格展开多行输入。输入期间只保留单元格内的未提交文字，失焦、Ctrl+Enter 或 Save All 时提交到 Working，不存在单独的“保存草稿”或复核步骤；清空后提交得到显式空字符串。普通 Enter 换行，Ctrl+Enter 提交当前输入或显式将缺译保存为空串；Escape 放弃尚未提交的输入并退出编辑，已提交内容通过项目撤销恢复。正文输入与浏览摘要均靠左上排列。组字期间不提交候选文字，更新期间输入框保持可编辑及焦点。

连续输入不逐字写作者文档或刷新表格，只在首次变为待提交状态时通知项目保存状态。提交由面板串行协调，值和来源指纹进入同一次项目历史。提交期间新增的文字继续保留，等待下一次提交；目标或来源失效时保留失败输入并就地提示，可重新输入或明确重新读取现值。失效输入不会自动覆盖外部修改。Save All 收拢待提交输入、等待当前提交并阻止未解决错误，再按项目事务写盘。ViewModel 属于项目 scope，切分类或关闭/重开视图不丢失未提交内容。

语言列头不随文本纵向滚动；表头与内容行均明确左对齐，隐藏列使内容宽度小于视口时仍保持列边界一致。定位列通过共享横向补偿冻结，不复制第二套列表。键盘聚焦的输入自动滚入冻结列右侧。分类区域可拖动，窄面板使用分类下拉框。

## 模板、导出与导入

面板提供当前分类/全部分类、单目标语言的 JSON 模板导出；勾选“运行文本”则输出已定义直接译文的平铺 key→string JSON，缺译不写入，显式空串保留。External 不进入作者交换来源。模板是 UTF-8 JSON，当前仅接受 version 1：

```json
{
  "version": 1,
  "projectId": "0123456789abcdef0123456789abcdef",
  "sourceLocale": "zh-CN",
  "targetLocale": "en-US",
  "rows": [{
    "key": "text_example",
    "sourceText": "Hello",
    "sourceFingerprint": "185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969",
    "context": ["group/entry/content"],
    "translation": null
  }],
  "complete": true
}
```

projectId 来自 Graph/localization-authoring.json 的 exchangeProjectId，不使用显示名或目录名。首次模板导出持久预留 GUID，分别保留 Working/Saved 中其他内容，不顺带保存 dirty 文本；身份不随文本历史撤销，已有身份不能被历史替换。首次预留可能在后续输出失败后保留，译文仍不改变。complete 表示引用分析范围是否完整，不表示全部文本已翻译或整个项目可发布；无法分析的范围保持 false，已知文本仍可交换。

导出先提交有效作者草稿，选择取消则不启动；目标须在项目目录外且不得经过目录链接，先写临时文件再原子替换，避免覆盖受管理作者来源或产生半个文件。文件中的 key 是机器身份，表格和导入预览不显示它。

导入接受上述模板，或单语言平铺 JSON（key→string/null）。模板核对项目 GUID、开发语言、支持的目标语言、版本、重复属性/行及原文指纹自洽；平铺 JSON 使用面板所选语言，没有原文证据。导入仅写开发语言以外的已支持语言，不隐式添加语言或改原文。单文件最多 8 MiB、100000 行，拒绝其他 JSON 值类型。

预览按稳定 key 匹配，显示当前开发原文、待写译文和状态；原模板原文可在提示中查看。未知 key 不可选；null 跳过；空串默认不选，选入后仍须显式允许清空。未变值默认不选。原文已变化的模板译文可选择应用，并按旧指纹标待复核；平铺输入或缺原文时复核状态未知。行顺序和 context 只供阅读，不用于身份匹配，角色改 ID 不改变姓名文本 key。

应用前提交相关有效 JSON 草稿，失效或损坏的草稿保留。预览捕获包内文本/引用、语言与 metadata 证据，在历史实际执行时再次核对；变动或项目关闭则拒绝旧预览。按文件共用快照并批量修改叶值，保留非目标译文、嵌套布局和其他 metadata。值与复核状态作为一次项目历史，撤销/重做沿用相同身份；新文件先初始化空 Saved 基线。应用期间预览控件禁用，取消预览用于尚未开始的应用。

## 发布检查与自定义契约

完整非 structure-only 的 project.validate 接入 localization-semantics，列出检查的包内翻译、catalog 和 metadata；独立 graph/group 范围明确报告跳过完整本地化检查。ProjectContentBuilder 在创建/替换输出前检查已声明文本、角色引用、默认语言、来源 JSON 和语言设置，确定的缺译、未知角色、重复/损坏来源和非法设置阻止发布，已有输出保留。External 不能补足包内默认语言完整性。

未知 schema、opaque 字段或分析预算上限报告 warning 和范围不完整，project.validate 的 Complete 为 false，不声称已检查自定义代码内部字符串。构建结果及 CLI diagnostics 保留这些警告；确定的错误仍阻止构建。作者 Save All 继续允许可修复的语义草稿，不能把作者保存等同于发布检查。

自定义 DTO 在 string/string? 成员上使用 [LocalizedText] 或 [CharacterReference]，生成/手工 schema 应保留 FieldDescriptor.Semantic；嵌套 DTO 和列表使用同一 walker。标记不改变 ValueCodec 的字符串契约，也不执行自定义游戏模块。自定义运行指令收到的仍是 key，须自行调用 ITextResolver；无标记字符串不被猜测或替换。接入示例见 [使用说明](../manual/game-localization.md)。

操作步骤见 [游戏本地化使用说明](../manual/game-localization.md)。实现与验证记录见 [阶段计划](../../features/F-20261019-01-editor-game-localization/phase-plan.md)、[P6](../../features/F-20261019-01-editor-game-localization/phase-6.md) 和 [实现总结](../../features/F-20261019-01-editor-game-localization/summary.md)。
