# P1a：基本页面、作者文档与项目会话

日期：2026-10-15。状态：verified，基于下述隔离基线验证。当前阶段之外未开始实施，尤其没有将旧分栈包装后宣称项目统一撤销已完成。

## 基线与验证环境

- 实施起点提交：`a674feef2f7ad47d2f0570f3ead5b91ea3057bf4`。
- 修改前：Editor 43/43、Shared 170/170 通过。结果在 `artifacts/F-20261015-02/p1a-baseline-editor.trx`、`p1a-baseline-shared.trx`。
- 受限执行的测试宿主在启动后未报告 NUnit discovery；中止该次运行后，同一已构建程序集在沙箱外运行正常。后续测试采用沙箱外执行；不将无结果视作测试通过。
- 实施中出现独立 `F-20261015-03-modular-game-state` 改动，移除 IGameRuntime.SceneState，而 Builtins 调用尚未完成迁移；主工作树构建出现相应 CS1061。未修改/回退该 feature 的文件。
- 使用 `git archive a674feef` 在 `Temp/F-20261015-02-validation/source` 建立无 Git 元数据的验证副本，再仅复制本次 Editor 与相关测试文件。锁定依赖恢复后顺序构建/测试；没有并发写主工作树 obj。
- 验证文件清单与最终 SHA-256 保存在 `Temp/F-20261015-02-validation/`；本记录中的隔离结果不等同于已经验证当前游戏重构中的完整工作树。

## 实现与消费者迁移表

| 对象/入口 | 本阶段处理 | 后续边界 |
| --- | --- | --- |
| IEditorDocumentService / EditorDocumentService | 从 singleton 改项目 scoped；项目打开时加载一次；保留 LoadedProject、Group 文档身份/路径索引和原始条目 | 文档版本、完整持久化基线与项目单栈在 P1b |
| ProjectService / GalProject.Settings | 打开时初始化项目作者文档；验证显式 Entry 入口；Graph 为变量定义权威，保存配置时派生变量副本 | 保存全部的原子计划、关闭保存/放弃/取消在 P1b |
| EditorGroupProjectionStore | 项目持有 Group 投影；中立 Group 内容不再归 GraphNode/页面所有；记录曾出现条目，项目释放时连同历史保留条目一起关闭旧字段绑定 | Group 页面功能与大列表优化在 P5/P6 |
| GraphNode/GraphEdge/选项/条件 | 投影直接引用作者 DTO，属性变更更新原对象，集合改变同步作者 Graph；停止保存时重新推断 Group 文件路径 | 明确编辑操作和事务替换历史监听在 P1b/P2/P3 |
| EditorEntryData / repository / CLI handlers | 作者参数为拥有生命周期的 JsonElement；现有 CLI 文本输入在边界使用唯一 schema 转换；snapshot 深拷贝 JSON | 未知类型保留和未完成作者文档完整保存策略在 P5/P6 |
| Entry 字段 UI | 字符串仅作为当前控件适配投影，成功提交更新原始 JSON；字段转换不预先要求构造完整 DTO | 不声称已完成复杂对象/数组等 P6 能力 |
| EditorDocumentCloner | 补齐 AudioTracks、策略、Speakers，防止会话/历史快照丢配置 | 历史事务本身仍按 P1b 替换 |
| GroupEditorPanelViewModel / Dock contribution | 参数由 GraphNode 改 Group ID；页面释放取消事件订阅；页面关闭不卸载项目内容 | 默认 Dock 仍复用同 Group 页；底层多页面共享内容已验证 |
| Dock factory / preview | factory 项目 scoped；释放旧 Group 页面与本地化/Workspace 订阅；移除游戏预览 contribution，旧布局的不可用项按既有恢复规则移除 | 游戏预览另立 feature；不保留第二套作者路径 |
| NewProject / Startup | 新增契约路径与浏览按钮、父目录选择；失败显示实际错误；取消不创建文件 | 原生文件夹对话框人工全程未验收 |
| GraphDocumentMapper / Workspace | 删除补入口和样例图 fallback；无效边/入口明确失败；Group source path 原样保留 | Graph 新交互不提前在本阶段实现 |
| EditorHistories / GraphChangeTracker | 仅保留当前 GUI 历史运行所需路径 | 尚未完成项目单栈与失败补偿，不算本阶段完成项 |

## 阶段验证

最终结果（2026-10-15）：

| 验证 | 结果 | 证据（相对仓库根目录） |
| --- | --- | --- |
| Editor 测试 | 50/50 通过 | `artifacts/F-20261015-02/p1a-final-editor.trx` |
| Shared 测试（含 CLI） | 172/172 通过 | `artifacts/F-20261015-02/p1a-final-shared.trx` |
| 架构测试 | 37/37 通过 | `artifacts/F-20261015-02/p1a-final-architecture.trx` |
| 内容集成测试 | 12/12 通过 | `artifacts/F-20261015-02/p1a-final-content-integration.trx` |

合计 271 项通过，无失败、无跳过。内容集成仅选择 `ProjectContentBuilderTests`、`GameInstallationTests` 和 `GameTestCaseTests`，不是完整 Integration 套件。Editor、Shared 及相关测试构建成功，分析器开启；保留基线已有警告，不宣称零警告。

验证命令在隔离副本中顺序执行：`dotnet build <project> --no-restore -m:1 -p:UseSharedCompilation=false`，再以 `dotnet test <project> --no-build --no-restore --logger trx` 运行对应套件；集成套件使用上述三类的 FullyQualifiedName 过滤。仅依赖恢复关闭 NuGet audit，未修改项目配置。最终源文件清单 `Temp/F-20261015-02-validation/files.txt` 共 35 项，主工作树与验证副本逐项一致；SHA-256 见同目录 `source-manifest.txt`。

格式门禁在这 35 项中的 C# 文件上按所属五个项目执行 `dotnet format whitespace/style --no-restore --verify-no-changes --include ...`，style 仅检查 `IDE0005`，全部通过；自有 diff 的 `git diff --check` 也通过。最终再次核对 35 项源文件与验证副本，差异为零。

已生成并检查新建项目页面 PNG：`artifacts/F-20261015-02/p1a-new-project.png`。测试在挂载主题和实际控件模板后操作路径输入及绑定命令，使用真实 ProjectService、契约部署、文档库和 EditorPage 创建。系统文件夹选择结果由测试适配器提供，不声称已人工操作原生文件夹对话框。

关键回归场景：新建→契约选择→工作区无预览；两个 Group 页面共享项目内容，关闭/重开不丢未保存条目；保存/关闭项目/重新打开保留数据和身份；配置保存从 Graph 派生变量；无入口图在发布项目之前拒绝且文件不被样例覆盖；移除条目后项目关闭仍使旧字段绑定失效；原始 JSON 大整数、嵌套值、显式 null 与缺省保留；完整音轨配置复制独立。

## 已知边界与下一阶段

### 创建入口修正（2026-10-15）

用户指出不能要求先准备契约 DLL 才能创建项目。此前将内部契约加载要求暴露成新建项目的必填前置条件，设计不符合普通 Editor 的创建体验，现已修正：`GalNet.Editor.DefaultContracts` 只组合内置 schema，构建为默认契约模板，随 Editor 输出/发布到 `DefaultContracts/`；创建页面不填写自定义目录时自动部署该模板。用户只需正常填写名称、位置和分辨率。自定义目录仍可选，已有项目契约严格加载规则不变。

补充验证：Editor 51/51（`p1a-default-contract-editor.trx`），架构 38/38（`p1a-default-contract-architecture.trx`），两项均无失败或跳过；真实挂载页面分别验证空目录默认创建和自定义目录创建、契约部署、关闭/重开及 catalog 可用。修正后的页面图为 `artifacts/F-20261015-02/p1a-new-project-default.png`。`dotnet publish --no-restore --no-build -c Debug` 成功，检查发布目录包含默认契约 DLL。沿用此前未受影响的 Shared 172 项和内容集成 12 项证据；总覆盖更新为 273 项。仍基于隔离基线，不宣称已完成并发重构的主工作树集成验证。

用户另要求每次完成后提交 Git，本次连同 P1a 实现一并提交，排除并发游戏重构的文件及共享文档中的他项修改。

- P1b 尚未实施：历史目前仍按既有领域组织；保存不是统一的版本化事务；项目关闭仍沿用现有行为，没有完整保存/放弃/取消提示。
- Group 复杂表单、未知作者字段和局部草稿的磁盘保存政策仍按 P5/P6 推进。本阶段不降低 CLI 编译/保存的完整校验。
- 项目文件移动/重命名等 P4 操作未提前开放；Group 路径身份只是准备，并未声称当前可移动文件。
- 完整游戏重构工作树的编译/测试需要等其 API 稳定后重新集成验证；本次隔离测试保留清晰来源与文件清单。
