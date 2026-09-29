---
feature: F-20260929-14-format-baseline
status: planned
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 格式范围盘点

**状态：verified**

分别运行 whitespace 与 using 验证，记录文件数、诊断类别、稳定排除项和最终命令；不修改源码。

**证据：** `dotnet format whitespace ... --verify-no-changes` 报告 67 个文件、165 处机械差异，其中 `WHITESPACE` 146、`FINALNEWLINE` 12、`CHARSET` 7；`dotnet format style ... --diagnostics IDE0005 --verify-no-changes` 报告 78 个文件、78 处 `IMPORTS` 差异。两条命令均只覆盖格式与 import，不包含 CA、NUnit 或其他语义分析器修复。受限沙箱中的 Roslyn build host named pipe 无法连接，盘点需在正常用户环境执行。

**退出条件：** 格式范围不包含语义分析器修复，命令可在 CI 重放，创建独立 Git 提交。

## Phase 2 - 机械格式基线

**状态：planned**

执行已确认的格式命令，审查 diff 只含空白、行尾和 using 机械变化，顺序运行 10 个测试项目并构建完整 solution。

**退出条件：** 格式命令二次验证无差异，测试和构建通过，创建独立 Git 提交。

## Phase 3 - CI 门禁与收尾

**状态：planned**

将同一验证命令加入 Desktop CI，生成 summary 并同步工程待办和 agent knowledge。

**退出条件：** 本地门禁通过，workflow 与本地命令一致，创建独立 Git 提交。
