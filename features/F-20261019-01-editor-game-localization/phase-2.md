# P2：受管理本地化文档与单次文本编辑

状态：verified（含下述既有工作树回归限制），2026-10-20。P3–P6 尚未实施。

## 实现

- Shared 的 LocalizationDocumentStore 以受管理 EditorFileDocument 为唯一作者来源，捕获文件/文本目标与编辑前值，不另建可独立保存的翻译数据库。
- LocalizationJson 验证翻译、catalog 和 version=1 的作者 metadata；按既有嵌套路径改文本，保留无关叶值、未知 catalog/metadata 成员与原编码/BOM，拒绝重复属性及扁平/嵌套 key 冲突。
- 专属 TrackLocalizationFile 可接管已有只读文件记录并同步保存基线；路径仅允许包内翻译、固定 catalog 和 Graph/localization-authoring.json，拒绝 External、其他 catalog 路径及任意受保护文件。
- LocalizationEditOperation 是不依赖 GUI 的可逆数据操作。已有文件的值只改 Working；新文件通过双版本结构事务发布空 JSON 保存基线、同时保留 Working 的实际作者内容。跨文件补丁先全部校验，失败不移动历史。
- LocalizationAuthoringService 将一次结构化修改适配到项目单栈。创建后保存再撤销/重做，会保留撤销当时的 Saved 版本，不以初始空文件覆盖后来的保存内容。
- 文本目标按 key、文件身份、有效来源和期望原文检查；同文件其他叶值的草稿可先提交后重基，不覆盖已变的同一目标。普通值 no-op 不新增历史。
- RegisterFileDraft/CommitFileDraftsAsync 只验证并提交相关文件草稿。普通文本 Save All 仍可保存待修复原文；严格校验只用于结构化编辑前的协调，不全面收紧普通文件保存策略。
- ProjectTextBuffer 保留草稿的原字节基线。文档刷新遇到冲突时保留输入并提示，拒绝过期提交；取消后同步当前文档。文本页优先从内存打开受管理文件，受保护文档不因被其他服务接管而变成可写。
- ReserveCharacterKeyAsync 持久预留 character_<递增编号>，结合 catalog 与 metadata 的高水位分配；预留独立于内容撤销，允许编号空洞，不顺带保存其他未提交内容。P2 不自动创建角色记录。

## 设计落实

作者 metadata 采用 Graph/localization-authoring.json，version=1 与非负 Int64 characterHighWater；运行时不读取它。默认新文本分片是 I18n/<culture>/authoring.json，后续表单/面板可显式传入 Group 分片。

角色编号预留通过结构事务同时推进 Working/Saved 的计数，属于不可回收的身份分配，不在创作 Undo 栈中添加独立条目。P3 的角色创建将使用预留结果形成一条角色/姓名/引用历史，撤销时仍保留高水位。这落实用户要求的只增身份，而非提供普通可撤销 metadata 编辑 UI。

JSON 草稿提交和随后的结构化修改是两个已明确提交的输入动作，各自进入历史。结构化服务只提交相关文件，不顺带应用其他文件或设置草稿。

## 调查与修正

1. 回归稳定复现同路径身份复用的保存错误：撤销第一份新文件、在同路径创建第二份文件后，CheckTrackedFiles 同时要求旧删除身份的路径不存在和新身份的路径存在，报告 files.changed。`p2-pre-fix.trx` 为 10 通过、1 失败。
2. 保存检查与 TrackedExpectations 改为先按路径归并、优先唯一有效身份，再筛选受管理内容；删除记录继续保留供历史使用。查询阶段已有相同原则，此次补齐保存路径。已更新 lesson L-20261019-04-file-document-path-reuse。
3. 首轮故障注入未触发，因为既有 FaultRepository 夹具仅识别白名单中的故障类型。将新的 LocalizationPublicationFault 纳入同一夹具后，确认发布失败补偿和恢复失败 RepairRequired 两条路径；没有为此更改生产事务机制。

## 验证

解决方案最终构建成功：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet
```

结果为 0 errors、91 warnings，日志 Temp/localization-p2-solution.log；没有建立整个工作树警告增量基线，不声称无新增警告。依照已知任务宿主限制，在受限环境外运行完整构建与测试，未修改产品代码绕过限制。

最终测试：

| 测试项目 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| GalNet.Editor.Tests | 196 | 3 | 0 |
| GalNet.Editor.Shared.Tests | 254 | 0 | 0 |
| GalNet.Architecture.Tests | 45 | 0 | 0 |
| 合计 | 495 | 3 | 0 |

P2 新增 Editor 12 项、Shared JSON 9 项，共 21 项全部通过，包括真实控件挂载测试与修正后的同路径重建保存用例。没有将测试宿主退出非零记成全量通过。

Editor 的三个失败均与此前 [P6e3 主工作区记录](../F-20261015-02-editor-authoring-workspace/phase-6e3.md) 中明确列出的失败一致：BrowserAddressBlocksDropAndGlobalSearchExitFollowSelection（目录块点击）、GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor（尾部留白）、GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave（行宽）。本阶段未修改这些测试及相关布局实现；未将并行工作区的 UI 改动恢复或纳入本任务修正。因此 P2 标记 verified 指本阶段验收与直接回归通过，不能解释为当前整个工作树全绿。

TRX 位于 Temp/localization-p2-results/；以三个项目名称的结果文件为最终回归证据，早期 focused/pre-fix 结果不混计。13 个任务 C# 文件的 whitespace verify 与测试前后 SHA-256 一致性检查通过，清单为 Temp/localization-p2-sources.json。文档链接与 git diff --check 也通过。

新增用例覆盖：既有文件修改/撤销/重做/保存重开、保留其他语言和无关叶值、新建文件不保存其他 dirty 内容、保存后创建历史重基、同路径重建保存、有效相关草稿重基、无效相关草稿阻止修改、冲突草稿保留与取消、真实 FileDocumentPanelView/TextBox 挂载、catalog/metadata 多文件历史、外部修改冲突、编号预留跨重开和故障补偿/RepairRequired。

## 边界与后续

- 本阶段提供受管理写入和历史 API，没有角色 Dock、speaker 搜索/失焦创建、多语言表格、自动 Entry 文本映射、复制文本 key 重映射或导入导出。
- 默认语言原文更新保留其他语言译文；待复核标记仍由后续文本生命周期阶段接入。
- 结构化表单尚未消费新 API；普通页面不会仅因 P2 完成就自动把输入转成 key。
- 未增加新的输入控件，也未进行原生 IME 人工验收；文件页绑定/草稿行为有 Headless 实际控件验证。
- 全项目查询仍为按需快照和文件读取，未做大项目性能验收。
- 工作树中另有并行 Timeline/UI 改动，未重置、提交或将其归入本 feature。

当前事实见 [规范](../../docs/spec/editor/localization.md)；下一阶段为 P3 角色 Dock 与可输入 speaker。
