---
feature: F-20260929-14-format-baseline
status: done
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 格式范围盘点

**状态：verified**

分别运行 whitespace 与 using 验证，记录文件数、诊断类别、稳定排除项和最终命令；不修改源码。

**证据：** `dotnet format whitespace ... --verify-no-changes` 报告 67 个文件、165 处机械差异，其中 `WHITESPACE` 146、`FINALNEWLINE` 12、`CHARSET` 7；`dotnet format style ... --diagnostics IDE0005 --verify-no-changes` 报告 78 个文件、78 处 `IMPORTS` 差异。两条命令均只覆盖格式与 import，不包含 CA、NUnit 或其他语义分析器修复。受限沙箱中的 Roslyn build host named pipe 无法连接，盘点需在正常用户环境执行。

**退出条件：** 格式范围不包含语义分析器修复，命令可在 CI 重放，创建独立 Git 提交。

## Phase 2 - 机械格式基线

**状态：verified**

执行已确认的格式命令，审查 diff 只含空白、行尾和 using 机械变化，顺序运行 10 个测试项目并构建完整 solution。

**证据：** 两条格式命令共修改 129 个 `.cs` 文件，diff 只包含 whitespace、最终换行、UTF-8 编码与 import 排序/清理；随后两条 `--verify-no-changes` 命令均通过。10 个测试项目顺序执行并以 340/340 通过；完整 `GalNet.slnx` Release build 覆盖 Desktop、Browser、Android 和 iOS，结果为 157 个既有分析器、XAML 与平台告警、0 个错误。`git diff --check` 通过。

**退出条件：** 格式命令二次验证无差异，测试和构建通过，创建独立 Git 提交。

## Phase 3 - CI 门禁与收尾

**状态：verified**

将同一验证命令加入 Desktop CI，生成 summary 并同步工程待办和 agent knowledge。

**证据：** CI 新增独立 `format` job，在 Windows 上恢复 solution workload 与 locked dependencies 后运行与本地基线完全一致的 whitespace 和 `IDE0005` import 验证；根 README 已公开本地命令，工程待办和 agent knowledge 已同步。两条本地门禁在机械基线提交后再次执行并通过。

**退出条件：** 本地门禁通过，workflow 与本地命令一致，创建独立 Git 提交。
