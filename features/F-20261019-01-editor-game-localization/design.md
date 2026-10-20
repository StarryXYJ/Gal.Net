# 默认开发语言创作与游戏本地化设计

状态：design，2026-10-19。本文描述目标方案，不代表当前代码已经实现。产品约束见 [feature.md](feature.md)；以下 API 名称和交换格式是设计选择。

## 1. 边界和现状

项目 ProjectSettings.TargetLocale 作为默认开发语言，AvailableLocales 保存支持语言及顺序。Editor 的 UiLocale 只控制工具 UI，不改变作者文本的编辑语言。

以下为设计开工时的事实；已实现阶段的当前行为见 [Editor 游戏本地化基础](../../docs/spec/editor-game-localization.md)：

- Core CharacterDefinition 为 Id/DisplayNameKey；CharacterCatalog 按 Ordinal 验证唯一 Id。Runtime 对话把 speaker 当角色 ID，正文当文本 key。
- GameLocalizationLoader 读取 I18n/catalog/characters.json 和 I18n/<culture>/**/*.json，支持平铺/嵌套字符串对象；同语言文件按规范化路径排序，后文件覆盖前文件。
- 当前默认语言完整性验证只识别内置对白、NVL、分支及角色姓名；自定义参数不会自动推断。
- FieldAttribute 与 FieldDescriptor 尚无成员值本地化语义。简单和带语音的对白 DTO 均需标记；不能只改最终 primitive。
- GroupEntryAuthoringService.Insert 分配 GUID StableId，Paste 重建身份，OrderEdit 按 StableId 重排；EditorDocumentRepository 将 StableId 写入作者 Entry.id，重新加载恢复。整数 Id 是位置。
- ProjectContentSession 已拥有 Working/Saved、Files、draft 注册、捕获保护、保存和恢复。ProjectTextFiles 的现有刷新会以新文档字节覆盖 Text；接入结构化编辑前必须补齐未提交草稿冲突处理。
- 普通文件策略保护 I18n/catalog。ProjectValidationService 目前声明跳过 localization-semantics。

本功能提供作者端投影及交换，不改变 raw 中正文已经是 key、编译原样传递、宿主负责解析的契约。Runtime 不引入 Avalonia、Editor 或 DynamicLocalization 类型。

## 2. 统一字段语义

建议引入互斥的 LocalizedText 与 CharacterReference 成员特性，并将语义写入 FieldDescriptor（例如 FieldValueSemantic.None/LocalizedText/CharacterReference）。独立于 EditorId：单行/多行仍由 UI hints 决定，引用检查不看控件名字。

generator 必须传递源基类与嵌套 DTO 的语义，生成时诊断不适用类型、冲突标记；仅字符串或可空字符串可直接标记。手工 schema 有相同的显式描述入口。缺少标记保持原字符串行为，不按字段名 content/speaker 猜测。

Shared 用统一 walker 遍历 descriptor 与作者 JSON：支持嵌套 DTO/列表，保留未知字段、缺省与 null；循环结构和规模有有限保护，不能分析的范围返回“不完整”，不能据此允许删除引用对象。分支选项虽非普通 Entry，也通过同一语义引用模型显式接入。

标记负责编辑、索引和校验，不改变字符串 ValueCodec 的全局语义。普通表单在作者提交边界将可读文本转为 key；不能把整个 string 编辑器替换为翻译编辑器。

自定义运行指令仍通过 ITextResolver 消费文本 key；不对全部 DTO 字符串进行隐式运行时重写，以免破坏 key、存档和自定义实现契约。

## 3. 唯一作者来源与数据流

建议 Shared 提供项目 scope 的 LocalizationDocumentStore、LocalizationReferenceIndex、LocalizationEditService 和 LocalizationExchangeService（名称可随实现调整）。UI 服务适配现有 EditorHistories，Shared 不反向依赖 GUI 历史实现。

数据流：

```text
作者 JSON / catalog / 翻译 JSON / 项目设置
             ↓ 同一项目文档，按代次建立派生索引
字段语义 walker → 引用位置、角色使用数、文本来源和语言状态
             ↓
Group/Graph 默认语言表单 | 角色 Dock | i18n Dock | 导入预览
             ↓ 显式编辑意图，检查版本和期望值
项目单一内容历史 → Working → Save All 文件事务 → Saved
```

翻译 JSON 的受管理 EditorFileDocument.Data 是保存来源，解析树和表格只是派生视图，不再保留第二份可独立写盘的翻译字典。catalog 通过专属入口纳入同一文档体系，不开放普通文件删除绕过引用保护。

新建翻译/catalog 文件走项目文件结构事务，复用双版本结构计划；不能用 File.WriteAllText 绕过历史或顺带保存其他未提交内容。持久作者辅助信息放 Graph/localization-authoring.json（建议路径，须作为受管理作者文件接入），包含版本、角色编号高水位和必要的翻译复核信息；不放入可丢弃的 .galnet 缓存。Runtime 不依赖它。

JSON 修改只改目标叶值，保留其他属性、未知信息和文件归属；不承诺逐字保留空白格式。同文件重复属性和扁平/嵌套路径碰撞必须诊断。多文件覆盖合法，但显示来源；改已有译文默认修改当前有效的包内来源，不改被遮蔽旧值。新建内容写入确定分片，例如 characters.json、groups/<groupId>.json、branches.json，不重分配已有来源。

普通文本页有未提交草稿时，对同文件的结构化修改先检查并提交有效草稿；无效或冲突草稿保留并拒绝相关修改。结构化编辑之后，干净文本缓冲自动刷新；带草稿缓冲不静默覆盖。过期面板行、异步导入和已关闭项目不得提交。

## 4. 稳定文本身份

普通标量文本建议首次生成 entry_<StableId>_<member>。属性名转义/哈希必须确定且防碰撞，生成后检查全项目占用。key 为不透明身份，分类和归属按引用计算，不解析 key 前缀。

- 更新原文、Entry 重排、保存重开：保留已存 key。
- Entry 跨组移动：保留 key，分类随引用改变，不按新 Group 重算。
- 复制 Entry/Group：新 Entry 身份，复制批次建立旧 key→新 key 映射并复制所有现有译文；角色引用仍共享。相互独立的文本副本不按相同原文合并。
- 已有内容显式共享 key：保留共享关系，修改提示影响位置；必要时提供创建独立文本，不静默拆分已有数据。
- 删除/替换字段：不自动删除翻译；未引用 key 只进入诊断/显式清理，不混入正常 Group 行。
- 分支选项使用已有稳定 option.id，同样不使用选项序号。

列表内文本不能以可变数组下标确定永久身份。采用首次创建字段时分配的独立 token（可组合 Entry StableId 与随机 token）；列表移动搬运含 key 的原 JSON，增项分配新 token，复制项生成新 key。引用位置中的下标只用于导航。属性路径不是稳定时，宁可使用不透明 token，也不能移动后重算。

缺省/null 仍表示原有值状态，不因打开面板就建文本。显式空字符串是有效翻译值，与没有 key 不同；必填规则在原字段/可运行内容校验中处理。已有缺译 key 显示“缺少开发语言文本”，不能把 key 当原文再保存一次。

## 5. 角色表和 speaker

角色页正常数据列保持“开发语言角色名 | key”，使用状态可作为行状态/删除按钮提示。key 对应 CharacterDefinition.Id，隐藏的 DisplayNameKey 与文本表连接。第一版沿用运行格式，不将角色名直接塞入 catalog。

自动角色 key 建议 character_1、character_2 等。高水位项目持久化，创建分配后不因删除、撤销而倒退；重做恢复同一角色身份，分配器同时检查现存和历史保留身份冲突。分配失败不复用已保留编号，空洞允许。手动 key 按 Ordinal 唯一，保留前后非空输入校验；匹配不改变存储大小写。

角色 key 改名同步所有明确 CharacterReference 槽位；DisplayNameKey 可保持不变，避免不必要重命名所有翻译。角色名修改只修改开发语言姓名译文。二者均支持一次历史操作。

搜索：候选按姓名与 key 子串匹配；候选显示姓名和 key。明确选择保留角色身份。失焦提交时，优先精确 key，其次精确姓名：唯一匹配则绑定；同名多个则保留草稿并要求选择；没有精确匹配则以输入文本为姓名创建新角色。搜索不是模糊自动绑定。空白清除 speaker，不建角色；同名角色可在角色表主动新增。

控件需处理 IME composition、候选点击引起的焦点顺序和提交重入：选候选不能先触发创建，失焦创建不能执行两次。创建角色、开发语言姓名和 Entry speaker 引用合为一次历史。

“只增更新”按语义引用监听和全项目扫描维护，不按展示名字回推角色。GUI 新输入正常创建；扫描已经保存的未知 speaker key 时，不能将 key 猜成姓名，给出缺角色诊断及显式补建入口。已有有效角色不随引用归零删除。扫描未完成/某些 Group 无法分析时禁止据此执行未使用删除。

2026-10-20 用户修订：删除 ProjectSettings.Speakers 与迁移候选/导入 UI；角色只来源于 catalog。扫描到未知角色 ID 只报告错误，不自动推断姓名或建立兼容映射。

## 6. 普通创作与 i18n Dock

所有被标记字段默认读取 TargetLocale 的直接译文；不能用其他语言回退值冒充可编辑开发原文。非 i18n 页面不提供独立编辑语言开关。角色搜索、Graph 选项、Group 列表摘要同步显示开发语言。技术诊断和 JSON 修复页可以展示 key。

角色 Dock 与 i18n Dock 都是项目级单实例，关闭视图不丢数据。i18n 左侧为虚拟化滚动分类：角色表、全部 galgroup、分支选项。Group 显示名/路径重命名不影响分类身份。右侧为虚拟化表格：开发语言第一列，其他语言按项目设置顺序，其后加语言按钮；可横向滚动和调列序，普通行不显示 key。行按 Entry/字段顺序提供上下文或导航，不把行号当身份。

同一 Group 内共享 key 显示一个可编辑文本身份并附引用位置，避免两格草稿互相覆盖；角色表和其他 Group 引用同一 key 时也共享同一文本状态。

新增语言验证并规范化 culture，拒绝重复，更新 AvailableLocales 并显示空单元格，不自动复制开发原文。开发语言固定第一，其他语言调序是项目设置编辑。切换开发语言是独立显式操作，先报告目标语言缺失情况，不能通过拖列隐式改变。切 Editor UI 语言不改变文本、dirty 或历史。

用户已确认原文改动保留译文；建议额外标记“待复核”，用每条译文关联的已审阅开发原文指纹判定。已有项目没有历史基线时视为复核状态未知，不声称已复核。该状态是编辑器 metadata，不进入 Runtime 文本值。缺失、显式空串、待复核分开呈现；运行时回退可查看，但不计作已译。

外部翻译 I18n/External 不作为默认写入目标；包内作者数据与第三方覆盖保持现有边界。第一版不增加第三方包管理界面。

## 7. 模板导出与导入

建议第一版提供 UTF-8 JSON 交换模板，避免新引入电子表格依赖；支持当前分类/全项目、单目标语言模板、单语言运行文本导出。CSV/XLSX 可后续加，不作为首版隐含承诺。

交换模板必须有 version、项目身份、sourceLocale、targetLocale 和 rows。项目身份取现有可持久身份；若没有则在作者 metadata 分配，不能使用项目显示名。每行包含稳定 key、sourceText、sourceFingerprint、context、translation（null=未提供，字符串含空串=明确值）。context 包含 Group/Entry/字段位置，仅供阅读，不用于匹配。导出面板无需显示 key，文件中保留机器身份。

导出捕获当前一致作者版本，先提交相关有效草稿，不要求先写盘。以捕获数据生成文件，输出到项目外不改变作者内容。输出路径不得覆盖项目受管理源文件，导出失败不修改会话。

导入先只读解析并显示预览：匹配、将修改、未知 key、重复 key、错项目/语言/版本、原文变化、并发变化和删除目标。按 key 匹配，不按原文/行号；不根据未知模板 key 自动建 Entry/角色。角色改 key 只改角色引用身份，姓名文本 key 保持稳定，因此旧姓名翻译模板仍可匹配。

null 不覆盖；空串默认预览为“清空”，需显式启用清空选项再应用。原文变化仍可接受译文，但标为待复核，不覆盖开发原文。应用时校验预览代次及每个目标期望值，变动则重新生成预览，不盲覆盖；用户选定的可导入部分以一次项目历史提交，失败补偿，不产生部分历史。原文和上下文列为参考，不通过目标语言导入偷偷改开发原文。

建议支持单语言平铺 key→string JSON 导入，供现有翻译文件交换使用；没有原文快照时复核状态未知，同样先预览，不绕过身份检查。单语言运行文本导出与带上下文模板明确区分用途。

## 8. 保存和校验

角色创建/改 key/删除、文本创建和更新、复制重映射、语言增序和导入全部通过项目单栈。历史持有稳定目标及 before/after 数据，不持有面板。草稿保留输入基线，选择/语言或源文档变化不静默覆盖输入；提交冲突时提示并保留。

保存把 catalog、翻译 JSON、作者辅助信息与 Group/Graph/settings 纳入同一捕获和作者文件事务。损坏翻译 JSON 可保留原文本修复，结构化编辑禁用该文件；不能通过保存全量格式化将损坏内容抹掉。保留现有外部文件冲突检测与 RepairRequired 机制。

新增 localization-semantics 检查：明确角色引用、已标记文本 key、默认语言存在性、重复/冲突来源、culture。作者保存允许缺译/未知角色等可修复语义草稿，显示诊断；JSON shape、身份重复和事务安全仍严格。构建/发布前的 Editor 完整检查阻止确定的内容错误；未取得自定义 schema 时报告检查范围不完整。Runtime 继续保留已有内置加载检查，不能为了泛化作者检查而执行自定义模块。

只接受当前 CharacterDefinition、文本 key 与作者 metadata 格式，不提供旧项目迁移。编号分配仍核对当前 catalog 中的手动 key，防止自动编号与手动角色身份冲突；这属于当前格式的唯一性规则。

## 9. 候选方案与取舍

P3 实施细化（2026-10-20）：角色首次写入前以结构事务初始化空 catalog 与开发语言 characters.json；该空基线和角色编号预留均不随角色值编辑撤销。角色记录、姓名译文和 Entry 引用随后作为一次 Working 编辑进入历史。原因是保存后撤销角色创建时，不能删除仍被 Saved Group 引用的文件；保存基线继续有效，下一次 Save All 才把跨文件值变化整体落盘。用户撤销仍完整还原角色及引用，允许留有空文件和编号空洞。

| 方案 | 取舍 |
| --- | --- |
| raw 保存原文，编译时替换 key | 不采用：破坏现有作者/运行契约，编译重排难维持身份 |
| 按行号或原文生成并持续重算 key | 不采用：换位丢译文、相同原文被误合并 |
| 新建独立可写翻译数据库，再同步 JSON | 不采用：与现有文本页/文件保存出现双写 |
| 在 schema 声明语义，基于稳定 key 投影 | 采用：工具共享事实，运行格式保持稳定 |
| 角色按引用自动清除 | 不采用：用户要求只增与显式删除 |

## 10. 风险及验收影响

最高风险是草稿互相覆盖、列表移动误换 key、复制共享文本未隔离、同名角色失焦误绑定和异步导入过期。阶段验证必须覆盖实际控件挂载与保存重开，不能只验证 ViewModel 属性。

多文件覆盖、坏文件和未知 schema 使“没有发现引用”不等于“确定未使用”；删除保护必须有索引完整性状态。大项目只按文档版本缓存并增量失效，单格输入不重复全项目扫描；索引在项目释放后清空并拒绝旧行。

尚未验证：API 名字、metadata 精确 JSON schema、虚拟化控件选择和性能阈值。由实现阶段依据现有控件能力落实，不写入当前 spec。验收与退出条件见 [phase-plan.md](phase-plan.md)。
