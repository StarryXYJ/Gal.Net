---
feature: F-20260929-13-repository-physical-layout
status: planned
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 共享层与基础设施物理归位

**状态：planned**

**任务：** 移动 Shared、Presentation、Infrastructure 项目；更新全仓 ProjectReference 与主 solution 路径；保持依赖图不变。

**验证：** Architecture、Core、Runtime、Builtins、Assets、Storage、Presentation 测试；相关 Release build；旧路径搜索。

**退出条件：** 内层项目位于对应物理分组，引用解析唯一且测试通过，创建独立 Git 提交。

## Phase 2 - Editor、Samples 与 Launcher 物理归位

**状态：planned**

**任务：** 移动 Editor、Samples 和 Launcher 项目；更新主/Launcher solution、CI、sample 脚本和 GameTestCase 命令。

**验证：** Editor.Shared、Editor、Integration 测试；Editor.Headless、Sample.Headless、Sample.Avalonia、Launcher.Desktop build；两个 sample 脚本。

**退出条件：** 所有宿主项目与 solution 分组一致，自动化入口不引用旧路径，创建独立 Git 提交。

## Phase 3 - 仓库地图、清理与全量收尾

**状态：planned**

**任务：** 更新根 README、架构 spec、路线图和 agent knowledge；清理确认无源码的旧目录；生成 summary。

**验证：** 10 个测试项目、完整 solution Release build、locked restore、活动路径搜索、`git diff --check`。

**退出条件：** 文档与物理结构一致、无活动旧路径或未记录限制，创建独立 Git 提交。
