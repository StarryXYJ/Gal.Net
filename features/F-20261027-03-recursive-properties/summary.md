# 递归属性访问实施总结

状态：implemented / verification-in-progress。实现已落地，P5 正在汇总最终验证；不能把本文件解释为整仓库构建和回归均通过。

## 已实现

Core.Parameters 以现有 FieldDescriptor / ValueDescriptor 为基础增加 ValueStructure、ValueStructureResolver、PropertyPath 和 ValueDocument。旧公开构造函数保持不变，WithStructure 为组合入口；静态 schema 使用延迟类型引用，元数据缓存区分类型、冻结 JSON options 与显式结构声明，并由调用方作用域释放。

属性快照支持对象、现有一维数组/List 类集合及字符串键映射。GetIterator 使用显式栈、深度/节点预算与取消，FindProperty 解析实际 wire 名和字段身份；Validate 显式区分 Partial/必填检查，Patch 生成局部候选而不写作者文档。类型化路径区分成员、索引、Map 键和 JSON 字符串边界。未知结构、重复/歧义成员及不匹配 codec 不被静默认定为覆盖完整。

生成器支持 StructureType 及显式编码/命名兼容标记。LayerTransform / FlipbookDefinition 的可编辑成员提供共享 schema，相关内置 Transform/ToTransform/Flipbook 字段关联结构；计算属性不进入作者字段。正常非空已知值不再仅因为 JsonElement 外层类型而报 opaque，未知扩展仍报告未覆盖。

LocalizationReferenceIndex 改用共享遍历，保留 Entry 身份与实际/逻辑位置。字符串编码结构的引用比较、角色替换、文本草稿和复制回写使用 LocationKey / Rewrite；本地化来源、CLI 诊断和 Problems 保留其正确位置。原公开 Pointer Set 调用入口保留。

Transform/Flipbook 保留专用布局，子字段读取、修改和草稿保存接入公共属性层；通用结构表单也使用同一结构解析与 patch。DTO/成员/类型工厂覆盖、NumericUpDown/Slider/无损文本、宿主能力、已有历史与保存入口保留。通用对象使用单一可折叠 Header、缩进及主题间距；列表显示数量，字典提供键/值、新增/删除/改键及重复键拒绝。字面 Map 键/序号和本地化字段名分别处理，完整 PropertySegments 用于深层字段聚焦。

## 验证记录

最终证据位于本目录的 `TestResults/`（gitignored）。阶段与最终运行分开，不重复累计复跑数量。

| 范围 | 当前结果 | 证据 |
| --- | --- | --- |
| Core | 138 通过；后续保守覆盖加固待最终复核 | final-core.trx |
| SchemaGenerator | 20 通过，包含旧 ABI consumer 实际加载 | generator.trx |
| Editor.Shared | 458 通过；编码数组多引用分别回写 | final-shared.trx |
| Presentation | 267 通过；已验证编码对象/列表、语义标量复制和字典操作 | display-presentation.trx |
| Editor | 全量曾 337 通过、1 图集选择器失败；定位复跑及最终轮次仍在跟进 | final-editor.trx、atlas-prefix.trx、editor-diagnostic.trx |
| Architecture | 52 通过 | architecture.trx |
| Primitives.Builtins | 321 通过 | builtins.trx |

已验证场景包括：只读遍历无 getter/DTO 构造、递归 metadata 复用、深度/步数/取消、命名与 converter 边界、missing/null/default、空/数字/转义 Map 键、未知兄弟成员及大整数、旧接口调用、编码内多个引用、上下控件/精确值输入、根/子字段定位，以及关闭全部视图后保存深层 Map 值草稿和 Undo/Redo。截图 `recursive-map-group.png` 及 Group 宽/窄视图已实际检查；原生 IME、DPI、辅助技术仍保留人工验收边界。

相关模块 Release 构建通过并保留分析器警告。整解决方案先前运行通过，但最后保留的 `solution-build.log` 被未在本 feature 修改的 SampleCustomization.cs:105 视频参数类型不匹配阻挡；不得将当前整仓库状态描述为全部可构建。格式检查只声称 changed C# whitespace；源码指纹及默认契约/Editor 打包 DLL 哈希另存 TestResults。部分早期 artifacts 日志被工作区清理，最终证据改存此 feature 的 TestResults。

## 验证中修正

- 已知 Transform 从“制造 opaque 的测试数据”改为正常覆盖断言；警告导航测试改用真正尚未描述的 Timeline JSON，保留定位回归。
- 公共路径回写不能把已属于 JsonArray 的原节点再挂回原位置，编码数组及旧文本/角色列表回归实际发现此问题；修复后按节点引用决定是否替换。
- 编码对象叶编辑不应按外层 string 误判结构改变并重建控件；编码列表结构操作也须保留字符串表示。
- 列表标量元素的引用语义需要一起交给宿主 CopyValue；通过带元素描述的临时结构包装保留原回调 ABI。
- Header 的本地化 key 与字面 Map 键需分别标记，避免显示 Entry.Parameter.* 或把数值 Map 键误写成列表序号。旧无结构的专用工厂输入回退 JSON，避免创建空面板。
- 全量中的图集冷加载用例已补强初次选择断言及失效信息，没有通过去掉版本校验或弱化断言使之通过；其顺序/时间相关性仍以最终诊断结果为准。

## 边界与计划落实

当前 shader 参数边界已按用户纠正修订为[开放参数对象](follow-up-open-shader-parameters.md)：已知 shader 的额外成员合法、原样保留且不 warning，不再按普通封闭 DTO 判为漏分析。声明字段仍校验；此规则不全局覆盖未知 schema 或其他封闭领域契约。

首轮交付将动态音效/视觉参数和 Timeline 的全量结构/索引迁移按设计留待后续。用户随后反馈正常效果参数仍有 opaque，已追加[效果参数结构补全](follow-up-effect-parameters.md)：应用效果、shader 预设及音频 apply/update 接入领域 metadata。再次反馈后追加 [Timeline 结构与按目标诊断](follow-up-timeline-structure.md)，接通轨道、关键帧、作者 metadata 与按项目契约解析的事件 payload，修正无关能力缺失污染已知轨道的诊断。实际范围和验证分别以补充记录为准，其他未关联 JSON 仍保留覆盖诊断。IValueStructureProvider 保留纯查询入口；返回结构或未解析结果，冻结上下文及其依赖有效性由调用方拥有，未引入额外的异步领域解析框架或独立依赖令牌系统。

未新增可变 SerializedObject 作者副本。ValueDocument 是只读快照/纯候选操作，作者数据、草稿与历史继续归现有项目会话。序列元素身份由当前快照索引及宿主结构代次保护，未增加全局元素 UID。失败的新增/改键候选仅留在当前控件；字段值草稿才进入跨视图的项目归档。

实际后端仍使用 System.Text.Json；没有重写 JSON 序列化器、替换全部 JsonElement、扩展 Prefab/多对象系统或改变运行/存档格式。当前只读结构解析集中复用现有 Schema，未强制改写所有生成 DTO 的公共接口。完整编译/运行和跨字段领域规则继续由原契约负责。

正式行为同步：[字段契约](../../docs/spec/runtime/entries.md)、[共享表单](../../docs/spec/presentation/game-view.md)、[本地化索引](../../docs/spec/editor/localization.md)、[Group 编辑](../../docs/spec/editor/groups.md)、[历史与保存](../../docs/spec/editor/history-and-save.md)；接入示例见[创作工作区手册](../../docs/manual/extensions/parameter-editors.md#声明嵌套结构)。经验见 `.agents/lessons/L-20261027-06-property-wire-boundaries.md`。
