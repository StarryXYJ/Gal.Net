# P4：默认语言创作与稳定 key 生命周期

状态：verified；2026-10-20。先完成 [P3a](phase-3a.md) 的兼容移除并提交 d2e4b82，再推进本阶段。

## 实现

GameTextAuthoringService 处理带 LocalizedText 语义的编辑意图；普通表单读取项目 TargetLocale 的直接原文，提交边界把编辑意图替换成稳定 key，同时更新原文文件、复核 metadata 和 Entry/分支引用。未标记字符串继续按原值编辑。控件不把 key 或其他语言回退值显示为开发原文；缺译有提示，共享 key 显示影响引用数。

初次提交分配不依赖行号/数组下标的 GUID 文本身份，之后修改原文和移动沿用 key。单行 Enter、多行 Ctrl+Enter、失焦提交；Escape 还原草稿，IME 组合态不提交。草稿固定原引用、原文和开发语言基线，相关变化后拒绝提交并保留输入。文本输入使用显示缓存，避免每个按键重复全项目查询。

根字段和嵌套 DTO/列表共用语义编辑器。结构化列表增加复制项；复制只重映射明确文本引用，保留普通字符串、未知 JSON、null/缺省和共享角色 ID。Entry/Graph 剪贴板 version 2 携带各语言直接文本及复核快照；不接受旧剪贴板，也不借目标项目恰好同名的 key 猜测来源。复制批次维护旧 key→新 key 映射，内部共享关系保留，副本与原文互不串改；跨项目按快照复制。

新作者文件先初始化空 Saved 基线；随后值编辑与内容粘贴合为一条历史。Graph 复制的 Saved 基线使用空 Group 内容及空选项文本，防止撤销→保存→重做时提前发布缺译的新 key；Working 保留完整副本，Save All 整体发布。相关 JSON 草稿先校验/提交，失败时不应用跨文件值修改。

metadata.textStates 保存 sourceLocale、sourceFingerprint 与各语言已关联的原文指纹；原文变化保留译文并标为需复核，没有关联证据时保持未知。角色姓名更新复用该状态记录。角色编号预留属于不可撤销字段：文本历史检查和应用只对 characterHighWater 允许单调重基，其余 metadata 仍严格检查，防止创建/撤销角色后无法再撤销文本或回退编号。

## 验证

首轮定向 14 项为 12 通过、2 个多行 UI 提交失败；TextBox 类处理先消费 Ctrl+Enter，改为 Tunnel 阶段处理后修正。后续 27 项及补充后的 29 项定向均通过。最终新增 16 项全部通过：GameTextAuthoringTests 12 项、LocalizationTextStateTests 4 项，覆盖普通原文/译文保留、Entry 与 Graph 复制、保存撤销重做、列表复制/移动、缺开发语言文本、分支真实焦点提交、重绑定和删除保护、取消及原文冲突、角色历史和编号交错、跨项目与混合 DTO 提交。

解决方案构建 0 错误、110 警告；后续仅修正 Graph 复制用例对文本 key 和保存重开的预期，Editor.Tests 再构建 0 错误、59 警告，该用例单独通过。最终完整回归：Editor.Shared 300 通过，Presentation 243 通过（总发现 249，日志有媒体环境跳过），Architecture 45 通过，Editor 224 通过、3 失败；合计 812 通过、3 失败，不宣称全绿。

Editor 失败中，GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor 的尾部空间 0 < 40、GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave 的宽度 482 >= 480，均已在 [既有 P6e3](../F-20261015-02-editor-authoring-workspace/phase-6e3.md) 记录。另一次 TimelineSelectionNeverNormalizesOrRewritesIncompleteOrInvalidAuthorJson 的 events:null 参数发生 Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync 空引用；同一方法 7 个参数用例在独立进程复跑全部通过，未复现功能断言失败，也未修改无关时间线代码。

37 个 C#/XAML/资源文件的最终指纹核对通过；C# whitespace 验证、四语 UI 资源 key 一致性（各 850）、文档相对链接和 diff whitespace 检查通过。构建及 TRX 证据保留在本地 Temp/localization-p4-solution.log、Temp/localization-p4-final-results/、Temp/localization-p4-format-verify.log、Temp/localization-p4-sources.json；Temp 不纳入 Git。

## 边界

- 不提供旧 Speakers/角色迁移或旧剪贴板兼容；P3 历史文档只记录当时状态。
- 原生 IME 人工验证未执行，自动测试验证输入、焦点和组合态边界；不宣称已做大项目性能验收。
- 多语言表格与语言管理属 P5；模板导入导出与发布前完整校验属 P6。自定义运行指令仍需通过 ITextResolver 解析 key。
