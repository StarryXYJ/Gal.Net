# Editor 游戏本地化基础

当前实现包含字段语义、项目索引、受管理本地化文档写入和角色管理 Dock/speaker 输入（F-20261019-01 / P1–P3）。正文的默认语言表单映射、多语言表格及导入导出仍是后续阶段。

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
- 递归分析带静态 Schema 的 IFieldDto 和数组/List/IList/IReadOnlyList/IEnumerable；列表下标只是导航位置，索引不生成或重算文本身份。
- 缺省与 null 不生成引用，不补默认值；未知字段/类型、不可遍历结构与错误 shape 报告分析不完整。
- 深度上限 16，整个分析的值预算 100,000。超限产生 analysis.limit，不能以未找到引用认定角色未使用。

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

角色 Dock 是项目级单例文档页，可从面板菜单打开，关闭后释放订阅，重新打开读取当前作者文档。列表显示开发语言姓名与角色 ID；搜索同时匹配两者，编辑草稿通过应用/还原按钮处理，Save All 仅保存已应用的作者值。

CharacterAuthoringService 以 catalog 为唯一角色身份来源，以项目 TargetLocale 的直接姓名译文为显示值；编辑器 UI 语言不改变作者姓名。允许同名、Ordinal 唯一 key；新角色不填 key 时使用持久预留的 character_n。修改角色 key 更新全部已声明的 CharacterReference，包括未打开 Group 与嵌套 DTO/列表中的实际 JSON 路径；姓名 DisplayNameKey 不变，其他语言译文保留。

新建记录、姓名译文及 speaker 引用构成一次项目历史，保存前只修改 Working。第一次需要文件时，先发布空 catalog 和开发语言 characters.json 保存基线；初始化文件和编号预留不随值历史撤销，允许空文件与编号空洞。保存后撤销创建只还原 Working 值，下一次 Save All 整体落盘，不删除仍服务于 Saved Group 的文件。未使用角色只能显式删除，撤销可恢复；删除保留姓名文本。引用分析不完整时禁止删除和改 key，仍允许修改姓名。

标记 CharacterReference 的字符串字段使用专属输入框，根字段与嵌套表单共用语义注册。候选按姓名/key 不区分大小写匹配，最多显示 50 项；选择提交精确 ID。直接输入失焦或 Enter 时按精确 key、唯一精确姓名的顺序解析；同名歧义要求选择，其他非空文本创建角色。空白输入清空引用，不创建角色。输入法组合态的 Enter 不提交，Escape 还原草稿；过期表单、取消或已移除 Entry 不能继续创建角色。普通未标记字符串保持原提交行为。

角色身份只来源于 catalog，ProjectSettings 不再包含 Speakers。没有旧角色候选、旧姓名迁移或导入兼容入口；未知 speaker ID 报告 character.unknown。原文/JSON 草稿冲突沿用受管理文档的校验与保留规则。

实现与验证记录见 [P1](../../features/F-20261019-01-editor-game-localization/phase-1.md)、[P2](../../features/F-20261019-01-editor-game-localization/phase-2.md)、[P3](../../features/F-20261019-01-editor-game-localization/phase-3.md)，完整目标见 [设计](../../features/F-20261019-01-editor-game-localization/design.md)。
