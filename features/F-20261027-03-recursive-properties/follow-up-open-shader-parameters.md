# 合法 shader 扩展参数不产生 warning

2026-10-27 用户明确纠正：切换 shader 后留下的额外参数本身合法，不应产生 `Unknown author field cannot be analyzed` warning。此前将“允许保留”解释为“保留但警告”不符合需求。

已核对渲染器 ShaderProgramTextureEffect：仅枚举 descriptor.Parameters 绑定 uniform/texture，额外 JSON 成员不被执行、也不承载隐含本地化引用。因此已知 shader 的 parameters 是开放对象，而不是封闭 DTO。该规则覆盖 effect.apply、shader 预设及 Timeline 中的 shader 效果事件。

实现计划：公共结构元数据支持带显式附加值契约的开放对象；shader 将附加值声明为不解释其内部含义的原子 JSON。扫描保留附加值，不将其误认为漏掉的引用，也不将未知 shader 当作已知结构。已声明字段仍按原类型/范围校验；普通 DTO、事件 envelope、Timeline、音效和 legacy catalog 的封闭字段规则不变。

验证：用户 Bloom 参数示例（angle/strength/softKnee/radius/threshold）无警告且原字节语义保留；附加嵌套对象/数组、空键均合法；重复成员、已声明字段的非法值和未知 shader 仍诊断；实际 Editor 资源切换与 Timeline 回归。

## 实现与证据

Core.ValueStructure 新增 OpenObject 和显式 Scalar 元数据，ResolvedValueStructure 保留原公开构造函数并增加 AdditionalField。ValueDocument 只在领域显式声明时接受附加成员，沿实际成员名定位；附加值描述的字段 ID 不成为所有额外成员的公共别名。默认 Object 仍封闭。

EntryParameterStructures 仅为成功解析 metadata 的 shader 参数使用开放结构。额外值作为原子 JSON，不遍历其中未经声明的引用语义；原作者 JSON 保留。该行为通过同一适配覆盖普通效果、shader 预设和 Timeline 效果事件。

- Core Release 构建与全量 **139 项通过**；`TestResults/open-shader-core.trx`。新增开放对象/原子 JSON/空键/重复成员/局部 patch/封闭对象边界用例。
- Editor Release 构建与聚焦 **7 项通过**；`TestResults/open-shader-editor.trx`。实际 Problems 验证保留的 angle/旧参数不 warning，shader 不再支持 radius 时轨道引用仍正确提醒。
- Editor.Shared Release 构建通过。全量 473 项首轮 **472 通过、1 失败**：ProtectedAndEscapingTargetsAreRejected 在 SetUp 创建项目的 WriteManifest/File.Move 阶段出现 UnauthorizedAccessException，与参数扫描断言无关；不把未知原因归结为已确认的环境根因。未改测试逻辑，复跑该组及效果/本地化索引 **43 项全部通过**，包括用户 Bloom 参数、附加嵌套 JSON、Timeline shader 事件、重复成员与缺失 metadata。证据 `TestResults/open-shader-shared.trx`、`open-shader-shared-recheck.trx`；不声称单轮全量零失败。
- 本轮 `git diff --check` 通过。正式字段契约、本地化规范与作者手册已同步，经验记录为 `.agents/lessons/L-20261027-10-open-shader-parameters.md`。

## 整理与复验

按用户要求移除只做转发的 metadata 适配，由 ProjectSceneTargets 直接实现快照接口；shader 表单和预设直接调用公共字段投影。合并参数结构选择分支，使用具名遍历上下文，复用 Timeline JSON options，并将引用诊断改为直接收集结果。英文/XML 注释仅说明快照一致性和开放字段边界。

整理后 Release 构建通过；Core 全量 **139 项**、Editor.Shared 全量 **473 项**、Primitives.Builtins 全量 **321 项**、Editor 聚焦 **8 项**全部通过。证据为 `TestResults/cleanup-{core,shared,builtins,editor}.trx`。此次共享层全量零失败；此前失败与复跑记录保留，不覆盖历史结果。

状态：本修复已实现并验证。此记录修订此前补充中“shader 旧字段保留并 warning”的结论。
