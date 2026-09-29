# Editor 工作区与命令职责拆分总结

## 实现结果

- 新增 5 项 Editor 测试，覆盖节点/边选择一致性、图编辑 undo/redo 对象身份、保存 checkpoint，以及两个新协作者的独立行为。
- 保留 `BuiltInEditorCommandHandler` 的单一入口和领域 router，将 Graph、Entry、Variable、Project 实现迁入四个 partial 文件。
- 新增 scoped `GraphSelectionState`，集中维护节点与边的选择集合及 `IsSelected` 一致性。
- 新增 scoped `EditorWorkspacePersistence`，组合 repository、document service、mapper、save coordinator 与 project service，负责图加载、保存和 Preview 数据构建。
- `EditorWorkspaceViewModel` 继续拥有可观察 UI 状态、编辑编排、history checkpoint、自动保存触发和错误处理，不再直接维护选择集合或引用 repository、mapper 与 save coordinator。

## 关键决定与偏差

- 采用 partial 文件而不是多个 DI command handler，保留现有单一公共入口、诊断 code、history key 和 resource path，避免为无状态方法增加服务编排。
- 未按路线图字面新增 history coordinator。现有 `EditorHistories`、`GraphChangeTracker` 和 `IGraphEditingService` 已有清晰所有权，额外包装只会增加转发层。

## 验证

- 10 个测试项目共 316/316 通过，其中 `GalNet.Editor.Tests` 23/23、`GalNet.Editor.Shared.Tests` 16/16。
- `GalNet.Editor.Headless` Release 构建通过。
- 完整 `GalNet.slnx` Release 构建 0 错误。
- 仍有 37 个既有 analyzer、Avalonia XAML 和 Browser WebAssembly native reference 警告，本 feature 未扩大处理范围。

## 后续工作

继续维护性路线图 Phase 4b，拆分游戏宿主与展示职责。
