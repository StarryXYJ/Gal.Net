---
feature: F-20260929-10-editor-responsibility-split
status: implementing
updated: 2026-09-29
---

# 实施计划

## Phase 1 - Workspace 行为保护

**状态：verified**

**目标：** 在改变结构前固定选择、图编辑、undo/redo、保存和变量的已有语义。

**任务：** 补充 Workspace 选择/保存特征测试；确认现有 GraphEditing、History、DocumentMapper 和 VariableDefinition 测试继续覆盖其他行为。

**验证：** `GalNet.Editor.Tests`、`GalNet.Editor.Shared.Tests`；Editor Headless build。

**退出条件：** 关键行为有可重复断言，无生产行为变化，创建独立 Git 提交。

**验证（2026-09-29）：** 新增 3 项 Workspace 特征测试，覆盖节点/边选择一致性、图编辑 undo/redo 对象身份，以及保存后 document/history/project dirty checkpoint。Editor 21/21、Editor.Shared 16/16 通过，Editor Headless Release 构建通过。

## Phase 2 - 内置命令按领域分文件

**状态：verified**

**目标：** 让 Graph、Entry、Variable 和 Project 命令的实现与物理文件一致。

**任务：** 保留现有 domain router，将命令实现和共享 helper 迁入各 partial 文件；不改公共 API 与诊断文本。

**验证：** `GalNet.Editor.Shared.Tests`；Editor Headless build；命令结果 diff 审查。

**退出条件：** 原 800 行实现文件仅保留入口/共享部分，四个领域可独立定位，创建独立 Git 提交。

**验证（2026-09-29）：** handler 入口/共享文件由 800 行降至 47 行，Graph、Entry、Variable 和 Project 实现迁入独立 partial 文件。Editor.Shared 16/16、Editor 21/21 通过，Editor Headless Release 构建通过。

## Phase 3 - Workspace 选择与持久化协作者

**状态：in-progress**

**目标：** 从 ViewModel 移出可独立验证的选择集合管理和项目图读写。

**任务：** 引入 `GraphSelectionState` 与 `EditorWorkspacePersistence`；ViewModel 保留可观察适配、编辑编排、history checkpoint 和自动保存触发；在 DI 中注册协作者。

**验证：** 协作者单元测试、Workspace 特征测试、全部 Editor 测试、Editor Headless 和 solution build。

**退出条件：** ViewModel 不再直接维护选择一致性或调用 repository/save coordinator 细节，创建独立 Git 提交。

## Phase 4 - 文档与收尾

**状态：planned**

**目标：** 同步稳定架构事实、路线图状态和实现总结。

**验证：** 全部 Editor/Editor.Shared 测试、Headless 与完整 solution；diff check。

**退出条件：** Phase 4a 有完整证据并创建独立 Git 提交。
