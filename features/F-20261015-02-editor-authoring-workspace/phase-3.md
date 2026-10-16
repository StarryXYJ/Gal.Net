# P3：Graph 完整交互

日期：2026-10-17。状态：verified。

## 实现范围

- 分支选项/条件新增、删除、重排及文本/表达式修改通过 GraphAuthoringService 修改作者数据，记录只保留项目文档、ID 和值；移除 GraphEditingService 中旧分支写入口及 Workspace 的页面闭包历史。
- 连线按稳定分支项身份重新计算 outlet；结构创建在 Working 与 Saved 的不同分支顺序中各自定位同一项，不顺带发布未保存的重排；删除中间项只移除该项连线，其余出口顺移。投影保留 DTO/输入控件身份，刷新已有 Inspector、节点高度和边路径；锁定 Inspector 在删除后解绑，撤销后按 ID 恢复目标，页面释放后仍可撤销；空分支没有可连接出口。
- 复制选择中的非 Entry 节点和内部边，Group 内容深复制；节点/边/分支项/Entry 稳定 ID 全部重建，Group 文件使用新路径。作者字符串和未知自定义 JSON 不猜测为引用，不做全局替换。当前字段契约没有声明额外 Graph 引用类型；已声明的关系是边端点与 Group 文件归属。
- 粘贴复用已有 PresenceEdit 与项目结构发布，整批一次撤销，包含复制内容和源文件；重做使用历史保留的内容，不再读取剪贴板。
- 画布 Ctrl+C/V/A、Delete、F 定位选区、Home 显示完整图、Ctrl/Shift 点击多选，右键菜单显示对应操作及键位。文本控件先消费自己的键盘操作，定位仅更新视图状态。Graph 专用键位当前固定，未新增设置页自定义手势入口。
- 新增菜单、历史、错误、分支输入/按钮提示与无障碍名称接入四语，历史显示目标名称或批次数量。

## 验证过程

基线为当前提交 12077f0，开始时工作区干净。与 P2i 不同，本轮直接验证当前工作树。

初次 Editor 构建成功。首轮 95 项中 94 项通过，一项 Condition 测试把普通文本填进表达式，保存时被既有语法校验正确拒绝；改用合法条件，保留 Choice 文本含花括号的用例。补充真实文本本地撤销、批量文件发布中段故障、Undo 故障补偿和重试验证，中间轮次 Editor 97 项全通过，随后增加排队创建、空分支、两套基线顺序、锁定 Inspector 与视口几何断言。

加强中间项测试时，两种分支均暴露 After 快照的全局边索引未随删边前移；`p3-middle-edge-regression.trx` 稳定失败，修正后 `p3-middle-edge-fixed.trx` 的 3 项通过。lesson 见 `.agents/lessons/L-20261017-01-local-edit-global-indices.md`。没有通过放宽预期校验绕过问题。

审查补充结构创建的两套出口映射：保存后的分支重排尚未保存，随后从原项创建 Group；检查 Working 与 Saved 出口下标不同，但均替换同一项，Undo 恢复原边。`p3-independent-orders.trx` 的相关创建/队列/控件 5 项通过；`p3-locked-inspector.trx` 验证锁定目标删除、恢复与页面释放后历史。

截图最初仍遗漏远处分支，定位为 Frame All 的固定缩放下限；Graph 最小缩放调整为 0.02，Frame 使用视口实际范围，`p3-framing.trx` 通过真实视口坐标断言每个节点都可见，而非只判断调用返回。

## 最终门禁

已完成 locked restore；限定非空源文件范围的 whitespace/IDE0005 修复与 verify 覆盖 Editor 10 个、Editor.Tests 2 个 C# 文件。最终当前工作树解决方案构建成功，0 错误、10 个警告。最终测试 339 项通过，0 失败、0 跳过：

| 测试组 | 通过 | 结果文件 |
| --- | --- | --- |
| Editor | 102 | p3-editor-final.trx |
| Editor.Shared | 180 | p3-shared-final.trx |
| Architecture | 45 | p3-architecture-final.trx |
| 内容集成 | 12 | p3-integration-final.trx |

计数以当前宿主重构后的基线为准，不沿用 P2i 的项目测试数量。最终 19 个自有源文件的 SHA256 记录在 artifacts/F-20261015-02/p3-source-sha256.json。

命令使用 `dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false`，四组使用 `dotnet test <project> --no-build --no-restore`；集成过滤 `ProjectContentBuilderTests|GameInstallationTests|GameTestCaseTests`。没有并行构建共享 obj，验证期间不修改源文件。中间验证输出目录曾被环境清理，最终构建日志、四组 TRX、截图和源文件 hash 已重新生成；以上中间轮次文件名仅记录当时执行过程。最终日志与 TRX 位于 `artifacts/F-20261015-02/p3-*`，真实界面截图为 `p3-graph-inspector.png`，均不纳入 Git。

## 后续边界

文件改名/移动与完整项目文件界面属于 P4a/P4b；Group 内容交互和 schema 完善属于 P5/P6；游戏预览另立 feature。未声明的自定义引用不自动重写，复制结果由作者检查。原生 OS 剪贴板人工验收与 IME 不由 Headless 自动测试替代。

下一阶段为 P4a；整个 feature 保持 in-progress。
