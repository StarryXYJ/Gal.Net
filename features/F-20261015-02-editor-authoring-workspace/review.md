# Editor Group 专项审核（2026-10-26）

结论：**blocked**。删除引用保护、剪切的身份语义和草稿生命周期仍存在影响日常创作的缺口。本次是问题分析，没有修改业务代码、设计或正式规范，也没有宣告整个 feature 完成。

后续实施：2026-10-26 GC1 已完成作者提交边界迁移，四项目 900 项执行通过，4 项 Explicit 原生 GUI 未运行，单阶段自审为 pass-with-follow-up，见 [实施记录](phase-group-consistency.md)。本审核的问题 5 所述普通作者提交通知缺口已接入；查询局部失效、引用、身份、草稿和回收等剩余事项没有据此标记完成。上段结论与验证范围描述的是原始审核轮次。

## 范围与证据边界

检查 Group 新增/删除/剪切/粘贴/换类型/重排、项目历史、作者投影、场景查询、普通 Locator 字段、Timeline、未提交草稿、Problems 和保存链路。规范以 `docs/spec/editor-groups.md`、`editor-scene-targets.md`、`editor-history-and-save.md` 为准，并核对本 feature 的 feature/design/phase-plan。

工作区有正在变化的其他未提交编辑器改动，本次未覆盖、回滚或提交它们。以下区分当前源码静态证据与运行探针：

- 场景 Shared 项目由当前源码成功编译；标准 vstest 宿主连接超过 90 秒而中止。改用独立进程直接调用无 setup/teardown 的 `Scene*` 测试，包括 TestCase/TestCaseSource，**116 个用例通过、0 失败**。这不是标准 NUnit 集成运行，也不覆盖 GUI。
- Editor 正常构建首次被当时未提交的 Flipbook 字段命名错误阻断；该文件随后由其他工作改动。最后一次正常构建在复制 DLL 时被运行中 Editor 的文件占用阻断。尝试 C# 13 也因仓库已有 C# 14 语法而失败，不作为修复方案。
- Editor 行为探针使用现有 `test/GalNet.Editor.Tests/bin/Debug/net10.0` 二进制（Editor DLL 时间为 2026-10-25 01:43），创建全新的临时项目，执行真实项目服务。其结果用于复现并与当前未改动的相关服务源码交叉核对，**不能替代当前工作区的完整 Editor 回归**。
- 运行材料在 `artifacts/group-audit-20261026/`：`probe-results.txt`、`shared-probe-results.txt`、`editor-build.log` 及两个独立探针项目。artifacts 为忽略目录。

## 首先澄清：删除后底层查询会重算

探针使用 show → hide 的最小项目：hide 执行前候选数为 1；删除 show 后再次查询变为 0；撤销后恢复为 1。删除时查询 `Changed` 通知次数为 0。

因此目前不能把症状解释为“底层场景算法完全没有更新”。实际有三件不同的事：

1. 当前作者列表已更新，重新查询能得到新事实。
2. 原消费者参数中的旧 handle 被原样保留，查询不会替作者清空或改绑。
3. 已打开控件的显示、候选和过期状态依赖历史、表单、选择上下文和资源通知等不同入口。

Group 投影和 Timeline 已订阅项目历史，正常删除后存在刷新路径；不能笼统宣称它们一律不刷新。未解析引用保留也是当前规范的明确行为。真正缺少的是影响分析、统一发布和可操作诊断。

`parameter-editor-design.md:116` 已要求删除生产者后保留引用并提示未匹配目标；`:124` 明确缺目标应跳过，不能仅因候选缺失阻止保存或编译。因此以下引用诊断建议属于作者提示，不能擅自升级为编译错误或删除门禁；参数格式、类型/属性和值契约错误仍按原规则处理。

## 问题清单

### 1. [P1] 删除和换类型没有场景引用影响分析，显式检查也补不上

**证据**：`src/Editor/GalNet.Editor/Services/GroupEntryAuthoringService.cs:91` 的 Delete 直接构造 PresenceEdit；`EntryFieldAuthoringService.cs:31` 的 SetType 直接替换类型和参数，并通过 NewEntry 重新分配创建者输出身份。普通删除、键盘删除、菜单删除和剪切均通向这套删除入口。

`src/Editor/GalNet.Editor/ViewModels/ProblemsPanelViewModel.cs:74` 的 Validate 检查 Graph、Group 契约、值与本地化；`src/Editor/GalNet.Editor.Shared/Services/ProjectValidationService.cs:111` 以单条 Entry 进行参数/编译校验，没有场景引用可达性检查。SceneTargetResult 只提供候选与 Complete/Reachable，没有“哪个消费者的哪个字段引用了已移除来源”的诊断。

**复现**：创建层并让 hide/move/Timeline 引用它，删除创建者，或将 show 换成 showColor。探针确认换类型后创建者 handle 已改变，消费者仍保留旧 handle；Problems 没有给出相应场景断引诊断。

**影响**：作者不知道哪些后续 Entry、下游 Group、Timeline 轨道/事件受影响；错误或无效果可能直到运行时才暴露。尚未重新选择的旧参数不会自动获得新对象身份。这里的引用是场景实例引用，不等于图片资源是否还在项目里。

**方向**：在作者操作前生成项目级影响结果，至少给出来源稳定 ID、消费者稳定 ID、字段/JSON 路径、确定失效/可能失效和分析不完整原因；删除批次内部的相互引用应排除。没有外部影响时直接删除，有影响时让作者明确选择保留断引、重选或取消。相容的换类型评估保留输出身份，不相容时同样报告影响。检查应覆盖跨 Group、Timeline 轨道/事件和播放实例。对不完整/自定义语义保持明确的未知状态，不能声称没有引用。

### 2. [P1] 剪切后粘贴使用复制语义，批次外引用永久留在旧身份

**证据**：`src/Editor/GalNet.Editor/Controls/EntryListEditorControl.axaml.cs:159` 附近的 CopySelectionAsync(cut) 写入普通剪贴板后删除原条目；`GroupEntryAuthoringService.cs:76` 对所有粘贴执行 RemapCopy。剪贴板没有项目内移动身份/事务信息。

**复现**：只剪切创建层的 Entry，保留一个引用它的消费者，再粘贴创建者。探针结果为 `preserved handle=False`、`outside consumer unchanged=True`。即使放回原 Group，外部消费者仍指向已删除身份。剪切整个引用闭包可能正常，现有批内重映射并不能证明单独剪切安全。

**影响**：作者以为是在搬移已有对象，实际创建了新对象；Undo、原条目与粘贴副本之间的身份含义也不同。

**方向**：复制与项目内剪切移动需要明确区分。项目内移动应保留或整体迁移引用身份，验证源版本，并将搬移及必要引用调整纳入同一项目历史。跨项目/失效剪切凭据按复制处理时应明确告知。即使暂不实现移动协议，也应复用问题 1 的影响提示。

### 3. [P1] 删除带未提交输入的 Entry 后，孤立草稿继续阻止整个项目保存

**证据**：`src/Editor/GalNet.Editor/Services/EntryEditSession.cs:25` 的 Pending 包含全部字典草稿；CheckSources 只标记冲突，没有删除目标的迁移/归档流程；`:120` 的批量提交继续提交这些草稿。`ProjectContentSession.CommitDraftsAsync` 在写盘前统一提交。

**复现**：为层的 opacity 建立未提交的 0.7 草稿，再删除层。探针得到 PendingCount=1、IsConflicted=true；统一提交失败，提示“源值或目标已改变”。Undo 恢复 Entry 后，草稿仍冲突。

**影响**：看起来已经删掉的条目还会阻止保存其他内容。当前可以到 Problems 明确丢弃草稿，因此这是可恢复的问题，不是数据已经损坏。

**规范关系**：源改变后保留冲突、Undo 不自动复活草稿，是现有保存规范明确规定的保护行为。缺口在于删除操作未把这项后果展示给作者，也没有把删除目标的草稿与其生命周期协调起来；不能简单通过静默丢弃输入“修复”。

**方向**：删除前展示相关未提交输入并决定提交/归档/放弃/取消；若归档随历史保留，应从活动 Save All 队列中移出，并为撤销恢复制定明确规则。保留冲突时，错误直接定位到删除操作与可用的取消草稿入口，避免只留下不再存在的 Entry 定位。

### 4. [P2] 全项目 Revision 被当成当前场景事实版本，误伤无关 Timeline 输入

**证据**：`src/Editor/GalNet.Editor/Services/ProjectSceneTargets.cs:96` 序列化完整 Graph/所有 Group 后比较，任何作者内容改变均推进同一 Revision。`TimelineEditSession.cs:92` 即使根 Timeline、选择和 sceneSignature 相同，也要求 Revision 相同才保留行；否则重建树、递增 generation 并使旧行失效。Submit 同样拒绝旧 Revision。

**复现**：在 Timeline 行填入尚未提交的 99，再修改它后面一个 Wait 的 condition。前缀场景与 Timeline JSON 没变，探针仍观察到新行不再保留 99，旧行 Commit=false。

**影响**：其他页面或后文的无关编辑会取消正在输入的 Timeline 数值、菜单或手势；容易被理解为偶发失效。规范承诺无关且事实未变的更新保留本地输入，现有版本粒度比该承诺更宽。

**方向**：区分作者事务代次、资源代次、来源/位置代次与当前查询事实版本。不可只删掉 stale 检查；应在根值、目标身份和相关事实仍一致时重基当前有效输入，在真正受影响时才取消。

### 5. [P2] 作者变化没有统一的场景查询发布事件，消费者各自补通知

**证据**：`ProjectSceneTargets.cs:55` 的内容事件只比较 Assets 工作字节；Capture 在查询时推进作者 Revision，但不发 Changed。Changed 主要来自资源准备、刷新。`EditorGroupProjectionStore.cs:26` 另行订阅 DocumentChanged/字段 Changed/History Changed；Timeline 又自行订阅这些部分事件。

**复现**：删除 show 后显式查询已经改变，但 Changed=0。该事实不等于当前所有控件都停留在旧场景：它们可经其他通知刷新。

**影响**：只监听场景服务 Changed 的新消费者容易遗漏普通作者修改；批量编辑期间的同步集合/字段刷新也可能多次观察中间内容。界面最终是否刷新依赖订阅组合与顺序，难以定义一致性契约。

**方向**：以项目操作完成后的稳定作者快照发布一次有范围的通知，查询层拥有自身的失效契约；控件按该契约更新显示和过期状态。已打开的旧选择需要明确失效/重新查询，不能自动把旧用户选择替换成新目标。批量与补偿失败路径应一并验证。

### 6. [P2] 场景快照检查和全投影刷新同步放大编辑成本

**证据**：ProjectSceneTargets.Capture 每次 Query/IsCurrent/PreparationStamp 都序列化整个作者项目；`SceneTargetAnalysis.StateBefore` 每次从 Group 入口重放到指定 Entry。`EditorGroupProjectionStore.cs:31` 每次历史变化同步全部已加载 Group，`:90` 内容相等时仍刷新每条 Entry 的字段和表单；字段选项再次触发场景查询。

**影响**：存在多个 Group/多个 Locator 表单时，一次小修改可能引发多次完整 JSON 序列化及前缀分析，均可处于 UI 调用链上。这是源码确认的复杂度风险，本次没有测量大型项目延迟，不声称已经测得卡顿阈值。

**方向**：先用单调作者代次替代读时全量序列化；按受影响 Group/Entry 范围刷新，并缓存 Group 摘要/前缀。先建立代表性计数/耗时证据，再决定是否异步化；异步化仍需保留正确的过期保护。

### 7. [P2] 已删除对象越过历史预算继续被项目缓存强引用

**证据**：`EditorGroupProjectionStore.cs:59` 的 `_allEntries` 只 UnionWith/Add，直到整个项目 Dispose 才 Clear。`EntryEditSession` 的 `_drafts` 同样直到项目关闭才整体清空，没有按失效、干净与历史可达性回收。

**复现**：删除后清空历史，探针观察到 Group 活动条目为 3，而 `_allEntries` 仍保留 5。清空历史后这些对象仍可从投影集合直接到达，不依赖 GC 时机。

**影响**：频繁新增/删除或长期编辑时，200 步/字节历史预算无法约束这部分 Entry/Source/草稿/闭包。历史释放不等于对象释放。

**方向**：明确活动投影、历史保留对象、未提交草稿各自的所有权；用可释放句柄或按可达性回收，不用永久 HashSet 作为统一销毁清单。关闭页面仍须保留有效草稿，不能简单清掉所有缓存。

### 8. [P2] 过滤列表不随搜索字段的编辑重新发布

**证据**：`GroupEditorPanelViewModel.cs:71` 的过滤依赖 Id/Type/Summary/Condition，但只在 SearchText 和集合变化时通知 VisibleEntries；`:271` 的条目属性订阅仅处理展开状态与 Type→Configure，没有在上述搜索字段变化后刷新过滤结果。

**影响**：搜索期间修改条件、文本/资源摘要或类型时，画面中的条目可能继续保留或不能及时出现；此时屏幕所见与重新计算的 VisibleEntries 不同，增加选择、删除和重排的理解成本。此项为静态确认的通知缺口，未完成当前主题下的真实键鼠复现。

**方向**：对实际搜索字段变化重筛，批量/输入时合并通知，并按稳定 ID 保持或明确移除选择。需要测试匹配→不匹配、不匹配→匹配及过滤中的多选/重排。

### 9. [P2] Group 摘要仍直接展示内部 handle

**证据**：`EntryEditSession.cs:140` 附近的 Summary 和 `GraphPresentationModels.cs:437` 的备用 Summary 都拼接 handleId 原字符串；常用字段 Group 头部展示 Summary。专属 Picker 隐藏了 handle，但摘要没有同样适配。

**复现**：探针确认消费者 Summary 包含其 opaque handle。

**影响**：作者在对象被删除、换类型或重命名后看到 GUID，而不是对象来源、资源和失效原因；与现有隐藏内部身份的创作交互方向不一致。

**方向**：摘要使用统一的场景来源/资源描述；缺失时明确显示未解析对象并能跳到诊断。JSON 高级修复入口仍可保留真实身份。

## 修复顺序与验收建议

1. **先建立引用影响与诊断闭环**：覆盖删除、换类型、剪切、重排越过创建者和 Group/拓扑变化；复用创建/引用槽位声明，避免另写按参数名猜测的扫描。必须区分一定缺失、条件/分支可能缺失、分析不完整；不要因为候选暂时缺失就自动删除作者轨道。
2. **明确剪切/复制与草稿语义**：同项目搬移、跨项目复制、过期剪切凭据、删除带草稿目标都应有可预测的单次历史和 Undo/Redo 行为。有效旧引用不能静默改绑到新对象；旧控件也不能因撤销恢复提交权。
3. **再统一变更发布与失效粒度**：一项作者事务只发布稳定结果；删除创建者必须更新其后及下游 Group，删除 hide/stop 应恢复后续候选，撤销/重做应重算；改无关后文应保留当前 Timeline 输入。
4. **最后处理缓存、过滤与呈现**：监测全量序列化/前缀重放次数和长期对象保留，修复过滤通知与摘要。完成后执行当前工作区的真正 Editor 集成/挂载测试及保存重开流程。

关键回归矩阵应同时包含普通层、差分层、粒子、效果、视频、animate/Timeline 播放、转场输出，以及 Timeline 事件内部创建者；同 Group/跨 Group、条件、分支、循环、未知条目均要覆盖。删除自身批次不产生多余外部引用提示，资源文件引用与场景实例引用分开报告。

本次没有复现并确认用户提到的全部具体异常堆栈；不能将任何运行时错误都归因于 entry 删除。已复现的草稿冲突和 stale 拒绝提供了具体来源，其余异常仍需以对应日志和最小作者数据继续定位。
