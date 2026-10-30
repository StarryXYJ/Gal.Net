# P4a：项目文件事务与文档协调

日期：2026-10-17。状态：verified。

## 实现

- Shared 的 ProjectFileOperation 接收编辑器意图，无需 GUI 构造 CLI JSON；覆盖文件/目录创建、导入、复制、移动/改名、删除和资源 metadata 修改。规范化项目相对路径，拒绝链接、非法路径、受保护入口、目标占用、批次碰撞及跨类别移动，折叠父子选择并保留空目录。Windows 大小写改名有持久中间路径。
- 扩展既有作者文件事务，以一个 manifest 发布作者写入、普通文件复制/删除、目录前后状态和大小写改名。普通文件与哈希使用磁盘复制/流，备份不作为大 byte[] 保存在历史。导入重做不依赖外部源；历史淘汰释放材料，owner lock 保护活动材料，打开项目清理无所有者材料。
- Working/Saved 分别变换 Group source path；已打开 Group 和 Entry 仍保留身份。之后保存再撤销文件改名，移动的是当前已保存文本，未保存字段不被顺带写盘。关联 Group 删除需要显式选择，节点/边/Entry 在一条历史中删除和恢复原顺序。
- 引用检查扫描全部作者 Group，不依赖已打开页面；区分字段声明资源引用与普通字符串候选，工作版 settings/UI JSON 提供候选。确定引用不能绕过，候选需显式允许，不全局猜测替换。metadata 保留 Gallery/扩展，复制分配新 ID、移动保留 ID；无效字段拒绝重写。
- GUI 接入项目历史，现有 Assets 面板/Inspector 改走该入口；AssetCatalogService 删除写方法，只读索引与通知，发布完成后刷新。CLI 现有资产命令共用预检/持久事务，移除旧专用事务及全局字符串替换，不持有 GUI 历史。
- 新增历史与错误四语 key；Assets 面板保留错误对象，切换语言后重新显示。Shared 的异常细节不写入 CLI JSON。

## 验证与问题修正

初期 Editor 全量 117 项通过。覆盖 dirty Group 改名→保存→撤销/重做、文档/Entry 身份、父子选择折叠、目录/空目录和 metadata Gallery/扩展保留、导入源删除后重做、外部目标冲突、路径保护、跨文化拒绝、未打开 Group 引用、关联 Group 删除恢复与 24 MiB 普通文件的托管分配上限。提交、Undo、Redo 的中段 I/O 注入均要求全部文件哈希、Working 快照和历史位置恢复，随后可重试。

首轮 Shared 全量 185/186 通过；失败是旧测试要求 Graph 损坏时仍绕过文档加载执行文件操作。统一文件机制需要有效作者文档及引用检查，改为返回明确失败，并检查磁盘完全不变，未保留该旧旁路。补充 CLI JSON 错误结果序列化、持有文件租约/孤儿清理、恢复时外部内容保护、大小写改名中间对象恢复、非法恢复路径和无法类型化 metadata 的保护测试。

首轮最终 Editor 回归 116/117 通过，唯一失败位于既有 SchemaFieldCommitUsesWorkspaceHistoryAndSaveSnapshot(true) 的 HeadlessUnitTestSession.DisposeAsync，堆栈为框架释放空引用，产品断言未失败。失败结果保留为 p4a-editor-session-disposal.trx；没有修改或跳过该用例，完整复跑 117 项通过。与已有 [Headless 会话发布竞态](../../.agents/lessons/L-20261014-05-headless-session-publication-race.md) 的表现一致，不宣称本阶段修复了第三方竞态。

补充声明资源值的引用检查：即使显式允许字符串候选，确定引用也阻止删除。收尾审查发现 metadata 请求可误传目录，已在预检拒绝，并用空目录 fixture 验证不会删除目录或生成 sidecar。metadata 容错读取与作者重写的差别记录为 [lesson](../../.agents/lessons/L-20261017-02-tolerant-reader-authoring.md)。

## 最终门禁

当前工作树解决方案构建成功，0 错误、36 个警告；四组相关回归共 365 项通过，0 失败、0 跳过：

| 测试组 | 通过 | 结果文件 |
| --- | --- | --- |
| Editor.Shared | 191 | p4a-shared-final.trx |
| Editor | 117 | p4a-editor-final.trx |
| Architecture | 45 | p4a-architecture-final.trx |
| 内容集成 | 12 | p4a-integration-final.trx |

构建命令为 `dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false`；四组顺序执行 `dotnet test <project> --no-build --no-restore`，内容集成过滤 `ProjectContentBuilderTests|GameInstallationTests|GameTestCaseTests`。运行期间保持源文件不变，未并行构建共享 obj。

日志、最终 TRX、验证脚本与自有源文件 SHA-256 位于 `C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/p4a-validation/`，不纳入 Git。项目内 Temp/artifacts 曾被环境清理，最终证据改用该目录。限定本阶段 21 个 C# 文件的 whitespace/IDE0005 修复与 verify 均通过，SHA-256 在验证脚本结束后记录。

本轮从 ede12a9 开始；另一任务修改的 `test/GalNet.Presentation.Tests/PlayerInputTests.cs` 保留且不纳入本阶段提交。同步 CLI README 时纠正其过期的新建项目说明：Editor 新建面板已自动使用分发的 DefaultContracts，并支持自定义目录；未修改项目创建行为。

## 计划调整与边界

用户本轮明确要求文件面板参考 Unity：左侧目录树，右侧当前目录网格。已写入 feature/design/P4b；该界面本阶段尚未实现。P4b 将导航、面包屑与前后退共用当前目录状态，展开、选择和缩略图大小为视图状态。

CLI 执行文件命令现在也加载作者文档，损坏文档阻止文件发布；未添加旧行为兼容。CLI 仍仅暴露现有资产命令，通用项目文件操作通过 typed service 提供给后续 GUI。普通文本内容页、完整文件树/网格、原生文件对话框验收均在 P4b。

事务提供可核对恢复与补偿，不承诺断电/跨进程整体原子性。结构 staging 当前同步执行，P7 再测大型项目交互；完整游戏预览仍另立 feature。正式规则见 [项目文件操作](../../docs/spec/editor/project-and-files.md)、[历史与保存](../../docs/spec/editor/history-and-save.md)、[资源](../../docs/spec/content/assets.md) 和 [目录格式](../../docs/spec/content/file-formats.md)。
