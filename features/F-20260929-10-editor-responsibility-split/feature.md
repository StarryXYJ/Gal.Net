---
id: F-20260929-10-editor-responsibility-split
title: Editor 工作区与命令职责拆分
type: refactor
status: done
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# Editor 工作区与命令职责拆分

## 目标

完成维护性路线图 Phase 4a，缩小 `EditorWorkspaceViewModel` 和 `BuiltInEditorCommandHandler` 的职责面，使选择、编辑/历史与持久化协作可独立测试，同时保持现有 UI 和命令行为。

## 范围

- 为 Workspace 的选择、图编辑、undo/redo、保存和变量行为建立特征测试保护。
- 将内置编辑命令按 Graph、Entry、Variable 和 Project 领域分文件组织。
- 从 Workspace ViewModel 抽取选择状态与项目图持久化协调，继续复用现有 `IGraphEditingService`、`GraphChangeTracker`、`GraphDocumentMapper` 和 `IProjectSaveScheduler`。
- 保持 ViewModel 现有对外属性、命令与事件表面。

## 非目标

- 不重设 Editor UI、快捷键、图数据格式或自动保存语义。
- 不迁移 Editor 公共扩展 API，不修改 Runtime 或 Sample 行为。
- 不为追求文件行数而增加无独立行为的包装层。

## 验收标准

- Workspace 的选择、历史与保存关键行为有可重复测试。
- `BuiltInEditorCommandHandler` 的领域实现不再集中于单一文件，现有命令诊断与结果保持不变。
- Workspace ViewModel 不再直接实现选择集合管理和文件持久化细节。
- Editor 与 Editor.Shared 测试、Editor Headless 和完整 solution 构建通过。

## 约束与假设

- 用户已授权按路线图逐阶段实施，每个实施阶段独立提交。
- 保持现有 DI lifetime 和 UI thread 调度方式。
- 已建立的 Editor.Shared 与 Editor 测试边界不反向合并。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [实现总结](summary.md)
- [路线图 Phase 4a](../F-20260929-03-maintainability-roadmap/phase-plan.md)
