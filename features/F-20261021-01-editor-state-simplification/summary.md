# Editor 写入口与状态精简总结

## 实现结果

- 删除 Workspace 未使用的创建项目、旧 Graph 编辑包装和 Entry 移动包装；GraphEditingService 仅保留连接器显示状态更新，删除旧 Entry 增删移动实现及对应旧测试。
- 变量声明唯一 GUI 写入口为 VariableAuthoringService。IVariableDefinitionService 改为独立声明快照查询/通知，保留 UID，删除名称修正和声明写接口。
- Preview 只显示声明元数据并编辑运行时当前值。删除声明增删、改名、排序、类型/默认值编辑、重复名称对话框、VariableDefinitions 包装及其旧测试；声明刷新和关闭使旧行停止提交。
- 删除 Workspace 的变量快照捕获、监听后补历史、回放和重入标记，删除不再使用的 PushAlreadyApplied API。
- 删除 Document 与 GalProject 的 dirty 字段/事件/标记 API，以及 Workspace 保存后的多处状态同步。GUI History、保存与关闭检查统一使用 ProjectContentSession 的内容比较和成功保存基线。
- 生产代码（含 XAML）净减少 973 行。不提供旧 API 的兼容转发层；作者文件格式、事务与双版本结构发布规则保持原有语义。

## 验证

Release 配置，关闭 Avalonia telemetry 与共享编译器，相关构建串行运行。

| 验证 | 结果 |
| --- | --- |
| 定向：变量快照、Preview 控件、Workspace、变量历史与保存 | 18/18 通过 |
| Editor 完整回归 | 240 通过，2 项既有布局失败 |
| Editor.Shared 完整回归 | 312/312 通过 |
| Architecture 完整回归 | 45/45 通过 |
| 完整 GalNet.slnx Release 构建 | 通过，0 错误、42 警告 |
| 验证期间 src/test 源码 SHA-256 指纹 | 一致 |
| git diff --check 与本次文档链接 | 通过 |

定向用例包含真实挂载 Preview 控件的双向绑定、四种运行时值、显示刷新不回写、隐藏行拒绝提交、声明快照隔离，以及不创建 Workspace 的变量引用改名、一次撤销/重做、运行时改值不影响 dirty、保存后 Undo/Redo 与重开。

完整 Editor 的两个失败分别是 GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor（尾部留白 0 < 40）与 GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave（窄行宽度 482 >= 480）。它们与 [本次开始前的 P5 记录](../F-20261019-01-editor-game-localization/phase-5.md) 一致，且 [P6e4 已独立复验旧版本](../F-20261015-02-editor-authoring-workspace/phase-6e4.md)；本次未修改相关布局或放宽断言。因此本切片验证不表示全仓回归绿灯。

首轮在沙箱内运行时 testhost 连接超时；在允许本机测试进程通信的环境重跑后正常执行。初轮定向的两个失败来自新增测试漏填 Entry 稳定 ID，以及错误地把 NumericUpDown 主题内部命令按钮视为声明操作；补齐 fixture、改为验证只读声明和实际当前值输入后，18 项重跑通过。

本地证据：`TestResults/editor-simplification/*.trx`、`Temp/editor-simplification-*.log`、`Temp/editor-simplification-source.sha256`。18 项定向属于完整回归子集，不重复累计；三个完整测试项目合计 597 通过、2 失败。

## 文档与边界

规范同步到 [变量编辑](../../docs/spec/editor/variables.md)、[历史与保存](../../docs/spec/editor/history-and-save.md) 和 [Graph](../../docs/spec/editor/graph.md)。Workspace fixture 同步到真实 ContentSession 保存基线，避免继续依赖已删除的人工 dirty 标记。

独立 UndoRedoHistory 仍可使用无项目的游标 checkpoint；生产 GUI 绑定内容会话，不使用游标判定 dirty。CLI 变量引用策略、事件刷新范围、缓存版本化和文件监听优化不在本次范围。已知两项 Group 布局问题留待独立任务。
