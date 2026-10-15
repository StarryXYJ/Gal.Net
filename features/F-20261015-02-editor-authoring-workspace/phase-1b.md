# P1b：项目统一历史、保存与恢复

开始：2026-10-15。验证完成：2026-10-16。状态：verified。

基线：P1a 提交 7079f65；并发游戏状态重构已提交 f17a593，本阶段回到当前工作树进行集成构建，不继续沿用旧隔离快照。仅修改本 feature 所属文件，完成后提交 Git。

## 实现与消费者

| 入口 | 实际变化 |
| --- | --- |
| EditorHistories / UndoRedoRouter | 项目只保留 Content 一栈，页面/Inspector 激活不再选栈；菜单显示下一条描述 |
| UndoRedoHistory | 支持异步串行执行、重入防护、成功后更新游标；复合 Execute/Undo/Redo 失败补偿；失败不截断 redo；观察者异常不改写提交结果 |
| ProjectSettingsPanel | 名称及设置为页面草稿，Apply/Enter 提交一次数据编辑，Escape/Cancel 取消；记录仅持 Graph、settings 数据及前后值，不持页面闭包；保留已有音轨溢出策略 |
| ProjectContentSession | 捕获同一作者版本，将 Graph、Group、settings、editor-state 合入一次计划；真实内容比较决定 dirty；显示行号/查询字典顺序不造成伪 dirty；后续编辑不被旧捕获保存误标 |
| ProjectService / Workspace persistence | GUI 保存委托唯一内容会话，删除单独 Graph 保存入口与直接 settings 写入；视口状态单独保存不提交其他内容 |
| 结构基础 | Working/Saved 分别应用结构变换，复用同一作者文件计划；验证 Group 创建/删除、路径变更、不同工作/已存值的恢复、安装失败补偿 |
| ProjectSaveScheduler / Close | 挂起并等待当前编辑/保存，取消未开始自动保存；项目关闭及应用窗口关闭支持保存/放弃/取消；取消/保存失败保留会话及无效设置输入 |
| AuthorFileTransaction | 在 `.galnet/editor/transactions/` 写 manifest、前后哈希及备份；补偿失败保留材料并进入 RepairRequired；打开时核对未完成计划，不覆盖外部修改 |
| ProjectRecoveryStore | 作者快照含版本、时间、载荷及 SHA-256；默认 30 秒/3 份；关闭与失败时也捕获；可配置软预算及独立读取校验 |
| 资源清理 | 200 步及已声明持有字节的软预算；裁剪、截断 redo、清空及释放时清理自有 disposable 资源；复合记录传递清理 |

实现中的锁顺序问题已记录为 [lesson](../../.agents/lessons/L-20261015-04-editor-history-publication-lock-order.md)。普通保存先获得历史捕获租约并尝试发布门，忙时释放租约后异步等待，绝不占有发布门等待历史；发布独立字节时释放历史租约。小型结构计划在门内同步发布/安装，避免内存提交间隙，不提前为大型文件异步搬运建立第二条通道。

关闭时还修正了生命周期问题：先清 Current 指针供页面解除订阅，但不在 Workspace 脱离时 Unload 项目作者文档，保证后续 scope 关闭回调捕获的恢复快照不是空文档。

## 验证

最终验证在当前工作树 f17a593 加本阶段改动上完成，未使用旧隔离快照。共 311 项通过，0 失败、0 跳过：

| 检查 | 结果与证据 |
| --- | --- |
| `dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet` | 成功，0 错误、8 警告；`artifacts/F-20261015-02/p1b-solution-build.log` |
| `dotnet test test/GalNet.Editor.Tests/GalNet.Editor.Tests.csproj --no-build --no-restore` | 75/75；`p1b-editor-final.trx` |
| `dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --no-build --no-restore` | 186/186；`p1b-shared-final.trx` |
| `dotnet test test/GalNet.Architecture.Tests/GalNet.Architecture.Tests.csproj --no-build --no-restore` | 38/38；`p1b-architecture-final.trx` |
| `dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ProjectContentBuilderTests\|FullyQualifiedName~GameInstallationTests\|FullyQualifiedName~GameTestCaseTests'` | 12/12；`p1b-content-integration-final.trx` |
| 格式与无用 using | P1a/P1b 合计 60 个 C# 文件，按实际 7 个项目及非空 include 执行 whitespace、IDE0005 修正与 verify；最后修改的 3 个 Editor VM 和生命周期测试再次 whitespace/verify 通过 |
| 实际设置控件截图 | `artifacts/F-20261015-02/p1b-project-settings.png`，已目视检查；由挂载真实主题的 Headless 测试生成 |

上述 TRX 均位于 `artifacts/F-20261015-02/`（本地生成，未纳入 Git）。测试覆盖真实控件本地 Ctrl+Z、提交后清空局部撤销缓冲、显式设置提交、关闭对话框按钮和窗口关闭的取消语义、页面释放后撤销、完整保存重开、外部冲突拒绝、文件发布/补偿故障、周期快照、W/B 结构变换与锁顺序。最终补充验证首次打开 Workspace 不清空已有设置历史、语言标准化后仍可撤销，以及无效语言/名称不留下部分设置修改。

首次集成命令误写为不存在的 `GalNet.Integration.Tests` 项目，未执行测试；修正为实际路径后 12 项通过。上一工作日的最终构建曾因自动审批额度耗尽未执行，本次已重新执行并保留日志，不将拒绝算作成功证据。P1a 历史格式脚本的 include 风险见 phase-1a.md，本次实际非空范围检查覆盖该阶段源码。

## 当前边界

- 原有 Graph 交互已进入 Content 同一栈，但其属性监听与持有投影的既有记录按 P2/P3 迁移；新设置切片没有保留第二条历史。旧 Graph 闭包/投影缓存尚未全面计量，软预算不代表全进程堆内存限制。
- 当前结构入口只是作者文件计划基础，无通用项目文件树/移动操作 GUI；任意文件/metadata/引用分析仍为 P4。
- Group 复杂或无效字段草稿的全面关闭协调、未知字段和不完整作者保存仍属 P5/P6，当前完整契约校验未放宽。
- 作者快照保留与校验已接入，没有交互式还原页，也不恢复跨重启 Undo 栈。不声称断电或跨进程整体原子性。
- Headless 测试挂载实际主题/控件；原生 IME 候选窗口与完整桌面人工验收未做。游戏预览仍不注册。
