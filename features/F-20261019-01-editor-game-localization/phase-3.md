# P3：角色表与 speaker 输入

状态：verified；2026-10-20。目标与退出条件见 [phase-plan.md](phase-plan.md)。

## 实现

- 增加角色 Dock、二元组列表、姓名/key 搜索、显式应用/还原、新建/改名/改 key、未使用删除、旧值导入候选。项目级单例，关闭页面释放订阅。
- CharacterAuthoringService 统一角色身份、开发语言姓名与引用统计；自动编号复用 P2 持久预留。同名允许、key Ordinal 唯一；改 key 按稳定 Entry 身份更新全部明确引用，包括未打开 Group 与嵌套 DTO/列表。
- 字段编辑器按 CharacterReference 语义注册；结构化表单克隆字段保留 Semantic。专属 speaker 输入显示开发语言姓名、匹配姓名/key、键盘及指针选中提交 ID、失焦创建；精确 key 优先，精确姓名歧义拒绝，空白不创建，组合态 Enter 不提交。
- 角色记录、姓名和 Entry 参数补丁进入同一项目历史。新文件先建立空保存基线，值修改只进入 Working；撤销角色创建不删除 Saved Group 仍可能使用的文件。编号和空基线允许留空洞，不提前保存其他 dirty 内容。
- 旧 Settings.Speakers 和未知 speaker 只进入显式迁移候选；正常页面不再往 Settings.Speakers 追加姓名。未知 schema/不完整分析禁止删除、改 key。
- 四种 Editor UI 语言补齐面板、历史与错误文案；UI 语言切换保留作者姓名和待应用草稿。

## 验证与修正

新增 10 个 Editor 用例，覆盖一次撤销、保存重开、同名/唯一 key、精确 key 优先、未打开 Group 改 key、保留其他语言、显式删除与编号不回收、旧值导入、不完整分析保护、并发姓名复用、取消/过期 Entry、无效 JSON 草稿及过期改名，以及三个真实挂载用例（角色面板/Dock、speaker 焦点/搜索/键盘/候选点击/组合态、嵌套列表字段）。Shared 新增默认 PascalCase 与 camelCase 的四项实际 JSON 字段路径回归，包括多层 DTO 继承外层值契约的序列化配置。

早期失败均保留日志：取消和 JSON 错误最初使用精确异常类型断言，改为接受对应基类；嵌套键盘测试必须先派发文本变更。组合态检查只验证 Enter 防误提交，清除 PreeditText 后再渲染，避免 Avalonia 静态 TextDecoration 跨独立测试 dispatcher 的已观察异常；不声称验证原生 IME 窗口交互。

首次完整回归发现 AuthoringDraftsReopenInMountedGroupAndKeepOpaqueValuesThroughEditsCopyUndoAndSave 失败：异常 speaker 对象在初始化时变成字符串。根因是 Avalonia TextChanged 延迟派发，程序化 Refresh 后 updating 标志已解除，初始化被错误当作用户输入并安排失焦提交。改用同步 TextBox.TextProperty 变更监听，程序化刷新受同一 guard 保护；异常 JSON 值保留并显示原文，不因挂载或编辑其他字段被清空。修正后该旧数据生命周期用例在两轮完整 Editor 回归中均通过，未把本阶段引入的失败归为既有失败。

最终解决方案构建：`dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet`，启用 Avalonia telemetry opt-out；0 错误、94 警告，52 秒，日志 Temp/localization-p3-final-solution.log。未建立整个工作树警告增量基线，不声称无新增警告。构建、testhost 和 format 的本地管道在受限环境外执行，没有修改业务实现绕过宿主限制。

| 测试项目 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| GalNet.Editor.Tests | 207 | 3 | 0 |
| GalNet.Editor.Shared.Tests | 264 | 0 | 0 |
| GalNet.Architecture.Tests | 45 | 0 | 0 |
| GalNet.Presentation.Tests | 243 | 0 | 0 |
| 合计 | 759 | 3 | 0 |

本阶段新增 Editor 10 项、Shared 4 项，14 项全部通过。结果以 Temp/localization-p3-results/ 下四份 TRX 为准，早期 focused/pre-fix 结果不混计；最后两份 Editor/Shared 日志为 Temp/localization-p3-final-GalNet.Editor.Tests.log 与 Temp/localization-p3-final-GalNet.Editor.Shared.Tests.log。共享控件和架构回归之后仅修改 Editor 输入事件监听和 Shared walker，受影响的 Editor/Shared 已在最终构建后完整重跑。

三个失败均为 [P6e3 已记录](../F-20261015-02-editor-authoring-workspace/phase-6e3.md) 的目录点击、列表尾部空间和行宽用例：BrowserAddressBlocksDropAndGlobalSearchExitFollowSelection、GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor、GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave。其中目录点击在前一轮完整重跑通过、最终重跑失败，保留其偶发记录；未修改这些测试或把相关并行布局改动混入本 feature。P3 verified 只代表本阶段验收成立，不代表当前工作树全绿。

20 个任务 C# 文件 whitespace verify 通过；25 个任务 C#/XAML/UI JSON 文件最终 SHA-256 与测试前一致，清单为 Temp/localization-p3-final-sources.json。四语各有相同的 23 个角色 UI key，文档链接与 git diff --check 通过。两条 agent lesson 分别记录实际 wire 名与程序化 TextBox 刷新边界，正式行为同步到 [spec](../../docs/spec/editor/localization.md)。

## 边界

- 角色面板草稿需要显式应用，Save All 保存作者文档中已应用的值，与变量面板一致。
- 候选上限 50；每次输入使用页面缓存，项目查询仍有 P1/P2 已记录的全快照/文件读取开销，未进行大型项目性能验收。
- 原生 IME 人工验收未执行；自动测试覆盖实际 TextPresenter 组合态防提交、焦点和 routed input。
- 正文/分支默认语言映射与复制 key 生命周期仍属 P4；多语言表格、语言管理属 P5；导入导出与发布前完整校验属 P6。不能把 P3 完成解释为整个 feature 完成。
