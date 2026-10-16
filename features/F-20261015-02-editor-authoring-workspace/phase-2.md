# P2：Graph 现有能力迁移与正确性补齐

日期：2026-10-16。状态：verified。

基线：P1b 提交 2cdae00；在当前工作树实施和验证。保留现有画布、选择、框选与平移缩放交互，不新增另一套画布框架。

## 实现

| 内容 | 结果 |
| --- | --- |
| 基本编辑与所有权 | GraphAuthoringService 的名称、坐标、边和节点记录只持项目数据/ID；Workspace 按文档事件协调投影，页面释放后仍可撤销 |
| 移除重复入口 | 删除 GraphEditingService/接口中的旧节点创建、删除、连线入口；名称移出 GraphChangeTracker；分支和条目接口暂留给对应后续阶段 |
| 汇合与替换 | GUI/CLI 允许多个前驱进入同一目标；连接只替换同一输出口的边，重复相同连接不记新编辑 |
| Group 创建 | 稳定 ID 与独立源路径，生成空 rawgalgroup，不自动插入任意默认 Entry；从输出口创建节点和连接记一步，撤销恢复原边 |
| 删除与恢复 | 多节点选择一次操作；包含 Group 时复用 P1b 文件计划，分别保留 Working/Saved 节点、关联边和条目；一次恢复原顺序与源文件 |
| 普通内容 | 纯 Branch 创建/删除、名称、连接和坐标属于普通内容，保存全部后写盘；Group 文件结构操作立即发布保存基线，但不保存其他未提交字段 |
| 拖动 | GraphNode X/Y 只做预览，松手提交一次多节点移动；Escape、失去捕获、页面脱离取消；预览期间保存不会写出预览坐标 |
| 结构准备时机 | 新增发布门内准备回调，等待先前保存完成后再捕获最新 Working/Saved 撤销材料；重做删除再次捕获当时基线 |
| 身份和通知 | 工作版本恢复保留作者对象身份；边移除/页面释放解除事件；遗留条目监听在项目历史执行时不补记账，结束刷新前值，避免页面重建后撤销重入 |
| 排队有效性 | 排队期间起始节点或输出口失效时，执行前重新校验，失败不创建孤立节点/文件，不推进历史 |

正式行为见 [Graph 基础编辑](../../docs/spec/editor-graph.md)。经验记录：[Headless 拖动状态](../../.agents/lessons/L-20261016-01-headless-drag-modifiers.md)、[结构基线捕获](../../.agents/lessons/L-20261016-02-structure-baseline-capture.md)。

## 验证

共 323 项通过，0 失败、0 跳过：

| 检查 | 结果 |
| --- | --- |
| `dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet` | 0 错误、19 警告；`p2-solution-build.log` |
| `dotnet test test/GalNet.Editor.Tests/GalNet.Editor.Tests.csproj --no-build --no-restore` | 85/85；`p2-editor-final.trx` |
| `dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --no-build --no-restore` | 188/188；`p2-shared-final.trx` |
| `dotnet test test/GalNet.Architecture.Tests/GalNet.Architecture.Tests.csproj --no-build --no-restore` | 38/38；`p2-architecture-final.trx` |
| `dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ProjectContentBuilderTests\|FullyQualifiedName~GameInstallationTests\|FullyQualifiedName~GameTestCaseTests'` | 12/12；`p2-content-integration-final.trx` |
| 格式门禁 | 5 个实际项目、19 个 C# 文件，非空 include 执行 whitespace 与 IDE0005 修正及 verify；最后 Workspace、GraphAuthoringService 和生命周期测试修改分别重验 |
| 真实 UI | Headless 窗口实际 MouseDown/MouseMove/MouseUp 与 Escape；验证预览不改作者数据、取消不记账、松手提交和一步撤销；`p2-graph-canvas.png` 已目视检查 |

上述证据均在本地 `artifacts/F-20261015-02/`，不纳入 Git。最后两项排队保护只修改 Editor 编辑记录及对应测试，重新构建解决方案并运行全部 85 项 Editor；其他边界测试所覆盖实现语义未变化。

具体闭环包括：三节点多前驱汇合保存重开；替换输出边撤销不影响其他前驱；多节点移动一步；Group 删除后分别恢复未保存/已保存条目，保存后再次删除/恢复使用新基线；Workspace 释放重建后撤销，再撤销此前条目修改；多 Group 删除恢复顺序和源文件；文件被外部修改时失败且保留节点、文件和历史；结构准备等候发布中的保存；排队起点节点被删除或输出口失效。

## 过程修正

- 旧测试夹具 ApplySave 只返回成功、不写 settings，导致后续撤销的外部基线检查失败；改为在临时目录发布计划字节，真实磁盘用例单独覆盖。
- 旧 GraphChangeTracker 测试按名字监听计数；名字已迁移为显式编辑，测试改为确认只有遗留字段继续被监听。
- 实际拖动测试最初未携带 LeftMouseButton 修饰符，MouseMove 被识别为未按键并取消。通过命中与指针状态诊断定位，修正输入后通过，没有以直接调用方法替代指针测试。
- 默认受限环境下 VSTest/testhost 连接及 Browser 的 MSBuild 任务通信失败；改在授权环境完成正式构建与回归。途中自动审批超时的命令未执行，重试成功后才计入证据。
- PowerShell Set-Content 产生的尾部空行已清理，提交前再次执行 git diff --check。

## 当前边界

- 分支选项/条件的完整编辑、复制粘贴、统一批量交互继续 P3；Group 条目、变量与表单草稿的完整迁移继续 P5/P6。保留的旧记录仍可能持投影，不宣称所有历史已经无页面引用。
- 删除 Group 会关闭其已注册页面；撤销恢复作者内容，页面可重新打开，不自动恢复先前打开的所有窗口。
- 项目文件树、通用文件操作、metadata/引用协调仍为 P4；没有提前开放通用文件界面。
- 继续采用 P1b 的单进程补偿/恢复边界，不承诺断电或跨进程整体原子性；项目级投影/tombstone 缓存并非全堆内存硬预算。
- 实际画布手势在 Headless 验证，完整原生桌面人工验收尚未进行；游戏预览仍不启用。
