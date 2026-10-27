# 递归属性访问实施阶段

状态：in-progress；2026-10-27。依据 [需求](feature.md) 和 [设计](design.md)。P1–P4 已按下方证据推进，P5 进行综合验证与收尾。

## 顺序与本次退出范围

| Phase | 状态 | 前置 | 交付 |
| --- | --- | --- | --- |
| P1 结构、路径与快照遍历 | verified | 当前设计 | Core 全量与生成器全量通过；旧 API consumer 加载通过，见下方证据 |
| P2 固定结构与引用索引 | verified | P1 | Editor.Shared 全量 457 项通过，含已知固定结构、未知扩展及编码路径 |
| P3 专用编辑与项目写入闭环 | verified | P1、P2 | Editor 聚焦 29 项及 Core 全量 136 项通过；共享纯 patch 与专用绑定接入 |
| P4 通用结构表单迁移 | verified | P1、P3 | Presentation 聚焦 17 项、Editor 聚焦 39 项；含字典操作与关闭全部视图后保存深层草稿 |
| P5 综合验证、审核与文档 | in-progress | P1–P4 | 证据齐备，迁移边界明确，正式文档与总结同步 |

P3 退出时进行首批闭环评估；P4 仍属于本次交付。动态音效/视觉参数与 Timeline 实际迁移不在本计划范围，只预留解析接口并验证其上下文边界，后续另行形成有范围与证据的计划。不能把它们标成已经迁移或已经消除警告。

## P1：结构、路径与快照遍历

**目标/模块**：Core.Parameters 与 SchemaGenerator；提取共同结构事实，暂不改变已有消费者行为。

任务：建立不可变结构描述及对象成员、集合元素、Map 值规则；增加兼容的描述组合入口与固定 StructureType 声明；统一 JsonTypeInfo/wire 映射及 converter 边界；实现类型化路径、只读属性快照、查找和显式栈遍历。递归结构延迟展开，动态解析接口只消费调用方冻结上下文。明确缓存所有权，不全局持有插件契约。

验证：Core/SchemaGenerator 测试覆盖同类型不同 JSON options、JsonPropertyName、父子 converter 优先级、自定义对象 converter 回退、递归/深层/宽集合预算、取消、missing/null/default、未知/重复属性、Map 的空键/数字键/`/`/`~`、集合元素引用语义。沿用旧构造函数与手写 schema 的调用样例；另用固定旧 API fixture 验证二进制加载，保留旧公开签名。

文档：在 feature 中记录最终 API 与设计偏差，正式 spec 暂不把消费者能力写为已实现。

风险/退出：防止 schema 静态初始化循环、单按 Type 缓存及扫描 getter。相关测试和构建通过，现有 codec 的实际 wire 行为保持一致，公共遍历无 UI/项目依赖后才标 verified。

## P2：固定结构与引用索引

**目标/模块**：Contracts.Builtins、Editor.Shared，依赖 P1。

任务：声明 LayerTransform/FlipbookDefinition 的实际可编辑字段；复用标签、默认值及约束；给相关固定 Entry 字段关联结构与历史编码适配。LocalizationReferenceIndex 迁入共享迭代，保留现有 code、Entry 身份及覆盖判定，支持结构内逻辑位置与实际 wire 位置。

验证：非空正常 Transform/Flipbook 不报 opaque；直接 DTO、对象数组/列表及 Map 中嵌套文本/角色引用仍可找到；缺失引用正确报错；非空未知扩展、未知 converter、坏 JSON 字符串、错误形状与超限仍使覆盖不完整。对照迁移前已有内置 scalar/Choice/catalog 行为；证明扫描不改作者 JSON、不补默认、不依赖页面打开。

相关现有回归：Editor.Shared 的 LocalizationReferenceIndexTests；必要的角色删除/替换及文本查询集成；Builtins 固定结构契约测试。上轮用真实 Transform 制造 opaque 的导航测试需改用真正未知结构，同时新增已知 Transform 不报警断言，不能直接删掉导航回归。

文档：记录已覆盖的固定 Entry 字段清单与残余盲区，准备 editor-game-localization 的实际行为更新。

风险/退出：不能把未知扩展当已知字段，也不能因传递了结构定义就绕过格式错误。新旧引用结果和诊断位置对照成立，固定结构误报消失且未知覆盖仍保守，相关构建/测试通过。

## P3：专用编辑与项目写入闭环

**目标/模块**：Core 的纯 patch、Editor 属性绑定桥接、Transform/Flipbook 控件、EntryEditSession 与导航适配，依赖 P1/P2。

任务：实现 Set/Remove 等路径修改与必要容器创建规则；专用布局从共享 schema/属性取得子字段，移除重复字段清单和 JSON 寻址。子草稿 Save All 使用相同 patch；旧档案路径显式兼容。写入仍经过现有作者意图与 partial/领域校验，保留类型/成员/DTO 覆盖及数字编辑路径。作者身份、控件 lease、根来源与列表结构代次分别复核。

验证：实际挂载 Transform/Flipbook，编辑后立即 Enter、失焦、Escape、上下调整和精确值输入；验证待提交/错误分级、自动检查及点击根/子字段聚焦。包含对象与历史字符串编码、大小写保留与冲突拒绝、未知兄弟成员、缺省/null、关闭所有页面后 Save All、保存重开、Undo/Redo 和跨 Common/Inspector 草稿。删除/换类型/删除后 Undo/根来源改变拒绝旧提交；关闭页面不丢作者草稿；同 JSON 替换不能复活旧句柄。

相关现有回归：GroupDiagnosticsNumericTests、StructuredGroupFormTests、DtoEditorIntegrationTests 与作者历史/草稿用例；补充针对公共 patch 的 Core 测试，不只测试 UI 最终赋值。

展示验收：Transform/Flipbook 专用布局与子字段控件覆盖保持现有体验，公共结构访问不强制把它们改成默认树形表单。

文档：记录首批闭环证据与最终绑定接口；准备 editor-groups、editor-history-and-save 的变化。

风险/退出：不允许写入一半、重序列化根 DTO 丢扩展、别名不一致或关闭视图错误推进作者代次。完整闭环通过、旧格式语义保持、Transform/Flipbook 读写定义与索引一致，再进入 P4。

## P4：通用结构表单迁移

**目标/模块**：Avalonia.Controls 的 StructuredFieldEditor、绑定与默认 registry 适配，依赖 P1/P3。

任务：以公共属性访问替换对象/集合判断和成员反射；保留 DtoEditorHost、整 DTO patch、成员/类型工厂及懒加载。加入字符串键映射的明确键值行与新增/删除/改键行为。元素移动/复制/删除后的子绑定重新解析，保留八层/200 元素 UI 回退与 JSON 修复入口。

展示任务与验收：实现用户确认的单一对象 Header、子字段缩进、序列/字典数量及操作；简单元素不额外包标题。组内/组间间距由主题统一，避免字段标签与内部类型标题重复、逐层空白累加。实际挂载宽/窄窗口及多层对象/集合混合结构，检查折叠、诊断、按钮可达性与布局密度。

验证：Presentation 的 StructuredFormTests、DtoEditorTests、DtoBindingTests；Editor 的 StructuredGroupFormTests 与 DtoEditorIntegrationTests。增加深层 DTO/列表/Map 组合的实际挂载，检查专用覆盖仍命中、集合和字典元素类型/语义一致、原子多字段 patch、未知成员、键冲突草稿、空键/数字键、重排后旧第 N 项提交拒绝、折叠重开和宿主能力转发。UI 回退 JSON 不降低只读分析覆盖。

文档：准备 control 和手册的嵌套结构/Map 扩展示例，记录移除的重复逻辑及仍保留的领域实现。

风险/退出：防止控件重建丢草稿、重复 lease/订阅、通用表单覆盖优先级变化，以及绑定把字典键当成员 ID。迁移范围内的表单与索引/codec 的字段身份一致，相关测试通过并有实际挂载证据后标 verified。

## P5：综合验证、审核与文档

**目标/模块**：全部受影响模块、正式文档与 feature 收尾，依赖 P1–P4。

任务：审核需求逐项覆盖、公共 API/模块依赖、兼容策略与未迁移边界；修复发现的问题。验证默认契约生成及 Editor 打包副本一致。更新 spec/manual，写 review.md、summary.md 和最终状态，已验证经验按仓库流程记录。

验证计划：先确认整解决方案构建成功，再运行 Core、SchemaGenerator、Editor.Shared、Editor、Presentation、Architecture 相关回归；Builtins 运行受影响结构/契约/效果参数用例，按失败或实际改动扩大。构建与共享二进制的 no-build 测试不重叠。运行契约兼容/卸载证据、四语资源一致性、changed C# 格式和 git diff 检查；需要时挂载窄/宽窗口检查焦点与键/值布局。记录实际测试数、失败、环境跳过及证据路径，不能复制旧 feature 的通过数。

文档：实现事实进入 docs/spec/entry-types.md、control.md、editor-game-localization.md、editor-groups.md、editor-history-and-save.md；接入方式进入 docs/manual/editor-authoring-workspace.md；未实现动态场景留在 summary 的边界和后续项。

风险/退出：不因本次统一而意外改变编译或运行 JSON、原生命令业务规则、契约可卸载性及现有专用交互。需求验收矩阵逐项有证据、review 无未处理阻塞问题、正式文档一致、残余盲区明确后才标 feature complete。

## 需求到阶段映射

| 验收点 | 主要阶段 |
| --- | --- |
| 共享身份、类型、wire 路径与迭代 | P1、P2、P4 |
| 固定结构正常值不再 opaque、未知结构仍报告 | P2、P5 |
| 嵌套引用识别与可点击定位 | P2、P3 |
| 局部修改、未知成员、编码与 presence | P1、P3、P4 |
| 数字/专用覆盖、草稿、保存及 Undo/Redo | P3、P4 |
| 作者/列表/动态上下文失效、契约卸载 | P1、P3、P4、P5 |
| 有界行为、无隐式业务执行、架构边界 | P1、P5 |
| 重复逻辑收敛、验证与正式文档 | P2–P5 |

## 变更记录

P1 证据：`recursive-p1-core.trx`（Core 全量）及 `recursive-p1-generator.trx`（生成器全量）；包含递归元数据复用、预算/取消、命名/编码/MapKey、动态上下文、无 getter 扫描，以及对旧 API stub 编译出的独立 consumer 在当前 Core 上实际加载。日志为 `artifacts/.tmp-recursive-p1-*.log`。旧公开构造函数未修改；增加 WithStructure 组合入口。初次兼容 fixture 的 using/assembly attribute 顺序错误已修正，非产品兼容失败。

P5 自审补充：实际 Pointer 在字符串边界终止，而逻辑路径可以进入内部。角色参数比较/替换与文本 draft/复制提交不能再用外层 Pointer 当唯一位置；增加基于 PropertyPath 的纯 Rewrite、LocationKey 并迁移这些消费者，保留旧公开 Pointer Set 签名。CLI/本地化表也保留 Entry 身份及内部位置。补充编码数组中多个引用共享外层 Pointer 但分别安全回写、保留未知大整数的回归。此次属于统一属性路径的必要补全，没有扩大到动态领域迁移。

当前 artifacts 目录曾被工作区清理，部分中间日志/截图已不在；保留的阶段 TRX 仍在各测试项目。最终验证与重新生成的截图统一保存到本 feature 的 `TestResults/`（gitignored），summary 只链接实际存在的最终证据。

2026-10-27：将前期草案的动态参数/Timeline 全量迁移从本次交付中分离，原因是其结构依赖类型、资源和剧情前缀查询，若同时迁移会把固定结构闭环扩大成多个领域改造。本次保留上下文解析扩展口及其失效验证；不得以此默认隐藏未迁移字段警告。后续若扩大范围，先更新设计与计划并在 summary 中保留原因。

2026-10-27：用户确认保留专用展示，通用结构使用可折叠 Header、缩进及主题间距；将展示规则和挂载验收补入 P3/P4，阶段仍为 planned，本轮不实施代码。
