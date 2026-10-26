# Group 一致性实施记录

## GC1：作者操作提交边界

日期：2026-10-26。状态：**verified**。用户在整体设计后明确要求开始实施；总体设计见 [一致性设计](group-authoring-consistency-design.md)，阶段依赖见 [phase-plan](phase-plan.md)。

### 实现

- Abstraction 增加中立 `AuthorChangeSet`：单调版本、Execute/Undo/Redo 原因、按 Group 合并的稳定 Entry 身份、最早受影响位置及变更标记。旧操作/混合复合操作保守标记 UnknownScope。
- Editor 的 `AuthorChangeJournal` 由现有 `UndoRedoHistory` 拥有，在其串行门内积累范围；操作与历史游标成功后只发布一次。同步/异步、复合操作和撤销/重做共用边界；失败及补偿丢弃本次范围。状态通知保留原 History.Changed，Clear/MarkSaved/空撤销不制造作者提交。
- Entry 字段和批量新增/粘贴/删除/重排直接修改 `EditorGroupDocument`，批量历史保留中立 Entry DTO，不以投影集合通知写回文档。正常表单仍经原有 schema、partial 校验及本地化复合操作。
- Group 可观察集合只允许投影同步修改；字段/集合更新发生在作者和历史完整提交之后。集合与属性通知按订阅者隔离异常，后续订阅者仍收到更新；投影错误留在 LastNotificationError/日志，不回滚已经提交的作者内容。真正操作失败仍按历史原子性与 RepairRequired 规则处理。
- Group 投影和 Timeline 改用成功提交通知。ProjectSceneTargets 在作者提交后捕获稳定版本并发 Changed，删除/撤销不再依赖资源刷新才能主动通知。
- 更新故障注入测试为新发布契约，并补充单调版本、无操作、组合范围、混合旧操作、失败补偿、重入拒绝、跨 Group 相同 Entry ID 和删除/恢复场景候选通知测试。仍验证旧字段不能因撤销而恢复提交权。

### 本轮验证与修正

1. 第一份工作区内源码快照完成 9 项定向测试，全部通过。随后源码目录与日志不再存在，保留下 focused TRX；清理原因未确认。本轮后续验证改用独立临时目录，未关闭运行中的 Editor，也未修改其他任务的文件来解除 DLL 锁。
2. 第一轮完整 Editor 为 308 通过、2 失败。一项旧生命周期测试直接调用投影集合 Remove；改为真实 Group 作者删除入口，继续验证项目释放后旧字段失效。
3. 另一项 `UnknownCharacterIdsRemainErrorsAndUncertainReferencesBlockDestructiveEdits` 在重构前服务源码上独立复现失败：空参数未知 Entry 按已实现空引用规则不使分析不完整。将这条测试夹具改为非空 opaque 载荷，保持“未知非空引用阻止破坏性角色操作”的原测试意图，未修改生产本地化规则。基线证据见 `gc1-baseline-character.trx`。
4. 基线备份恢复带回旧时间戳，曾使 MSBuild 复用基线 DLL，导致候选测试编译报缺 AuthorChanges。显式刷新临时快照被替换源码时间戳后重新编译，不能把该次编译失败归为候选 API 未实现；经验见 `.agents/lessons/L-20261026-01-baseline-restore-timestamps.md`。
5. 最终候选 Editor **311 项通过、0 失败、0 跳过**，使用标准 dotnet test/NUnit 宿主，包含真实 Headless 表单/Timeline、多面板、剪贴板、历史、保存重开与查询相关回归。

最终验证：

| 项目/检查 | 实际结果 |
| --- | --- |
| GalNet.Editor.Tests | 311 通过、0 失败 |
| GalNet.Editor.Shared.Tests | 417 通过、0 失败 |
| GalNet.Architecture.Tests | 49 通过、0 失败 |
| GalNet.IntegrationTests | 123 通过、0 失败；4 项 Explicit 原生 GUI 用例未执行 |
| 当前阶段 C# 格式与 IDE0005 | Abstraction、Editor、Editor.Tests 的 13 个变更文件均通过 verify-no-changes |
| 源字节核对 | 主工作区与候选快照 13 个实现/测试文件 SHA-256 一致 |

合计 **900 项执行通过、0 失败**。四个测试项目及其依赖在固定候选中成功编译；没有执行全解决方案/Launcher 构建，也没有把 4 项未执行的原生 GUI 用例计为通过。现有编译警告保留，不宣称零警告。

材料目录：`artifacts/group-consistency-gc1/`，包括各轮 TRX、最终项目日志、`source-hashes.json` 与临时快照路径。已知基线失败与首次构建失败均保留，最终结果未以旧 DLL 或直接反射调用替代标准测试宿主。

本阶段自审结论：**pass-with-follow-up**，没有发现阻止 GC1 交付的问题；GC2–GC5 的原始问题仍属于后续必需工作，专项审核对整个 Group 一致性改造的 blocked 结论没有据此撤销。正式当前事实已同步到 [历史与保存](../../docs/spec/editor-history-and-save.md#作者内容提交通知)、[Group 编辑](../../docs/spec/editor-groups.md) 和 [场景目标查询](../../docs/spec/editor-scene-targets.md)。

### 当前边界

GC1 不新增删除引用弹窗、不改变剪切语义、不归档删除目标的草稿，也不实现相容换类型身份迁移。整项目作者 JSON 比较、整组投影刷新及 `_allEntries` 保留仍存在，分别留 GC4/GC5。Graph/文件/本地化的旧编辑只以 UnknownScope 接入发布边界，原有文件事务和 Working/Saved 约束未重写。

投影通知不再参与作者回滚是本阶段刻意采用的设计变更；原来的集合补偿故障注入用例不能继续将观察者失败当作领域操作失败。复合领域步骤失败与补偿失败仍有独立回归，不能删除这类原子性保证。

原工作区有其他编辑器 UI、音频、导出/发布和脚本整理改动，均非本阶段业务范围；验证使用固定快照，并在提交时限制本阶段文件。项目整体 feature 保持 in-progress，GC2–GC5 不据此宣告完成。
