---
id: F-20260929-14-format-baseline
title: 全仓机械格式基线与 CI 门禁
type: maintenance
status: planned
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# 全仓机械格式基线与 CI 门禁

## 目标

在不改变运行时行为的前提下清理既有空白、using 和行尾格式债务，并启用可重复、不会天然失败的 CI 格式门禁。

## 范围

- 盘点 `dotnet format` 的实际诊断类别和受影响文件。
- 以独立机械提交统一选定的空白、行尾和 using 规则。
- 在 CI 中加入与机械清理范围完全一致的 `--verify-no-changes` 命令。
- 顺序运行全部测试并构建完整 solution。

## 非目标

- 不批量修复 CA/NUnit 等语义分析器告警。
- 不重命名类型、调整 API、重构逻辑或升级包。
- 不把格式变更与其他功能实现混入同一提交。

## 验收标准

- 机械格式命令执行后再次验证无差异。
- CI 格式门禁在当前仓库可通过。
- 10 个测试项目和完整 solution 构建通过。
- 格式化 diff 不包含行为变化。
