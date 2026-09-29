# Editor 工作区与命令职责拆分设计

## 当前问题

`EditorWorkspaceViewModel` 同时维护 UI 选择、图编辑编排、undo/redo 记录、变量快照、项目加载/保存和自动保存触发。多个职责共享内部字典与标志，导致 ViewModel 难以在不构建整个 Editor 环境的情况下验证。

`BuiltInEditorCommandHandler` 已使用四个 domain router 分派命令，但 Graph、Entry、Variable、Project 的实现与共享辅助函数仍集中在一个约 800 行文件中，修改局部命令时需要理解整个处理器。

## 目标边界

### Workspace ViewModel

ViewModel 继续拥有 Avalonia/CommunityToolkit 可观察表面、RelayCommand 和对 Dock/Inspector 的通知。它只编排用户意图，不自行执行以下细节：

- `GraphSelectionState` 管理节点/边选择集合与 `IsSelected` 一致性；ViewModel 负责把结果同步为可观察属性。
- `EditorWorkspacePersistence` 组合 repository、document service、save coordinator、mapper 和 project service，返回可由 ViewModel 跟踪的 graph model，并执行一次完整保存。
- 现有 `IGraphEditingService` 继续负责图结构变更，`GraphChangeTracker` 继续负责属性订阅，`EditorHistories` 继续拥有 undo/redo stack。本 feature 不再包装它们，避免新增只转发方法的“coordinator”。

### 命令处理

`BuiltInEditorCommandHandler` 保持单一公共入口和现有 domain router。实现按 partial class 分为 Graph、Entry、Variable、Project 与 Shared 文件；不改变公共 API、诊断 code、history key 或 resource path。这是物理职责拆分，不把无状态领域方法注册为 DI 服务。

## 数据流

```text
UI / Dock / Inspector
        |
EditorWorkspaceViewModel
   |          |                 |
selection   graph edits      load/save orchestration
   |          |                 |
GraphSelectionState  IGraphEditingService  EditorWorkspacePersistence
                         |                 |
                    EditorHistories   Repository / Mapper / ProjectService
```

## 兼容性与风险

- Workspace 对外属性和方法名保持不变，AXAML、Dock 和 Inspector 不需迁移。
- 选择抽取的主要风险是 `IsSelected`、`SelectedNode`、`SelectedEdge` 与 `HasMultipleNodeSelection` 通知不同步；使用特征测试覆盖单选、多选、边选与清空。
- 持久化抽取的主要风险是保存成功前过早清除 dirty/history；协作者只负责写入，ViewModel 在 await 成功后统一标记已保存。
- 命令文件拆分仅移动方法，通过 Editor.Shared 命令测试和 diff 审查防止行为偏移。
