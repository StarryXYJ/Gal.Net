---
id: L-20260929-06-format-gate-clean-baseline
status: candidate
source: F-20260929-04-quality-baseline
created: 2026-09-29
---

# 格式门禁必须建立在干净基线上

## 现象

在现有 Windows checkout 中为根 `.editorconfig` 强制 `end_of_line = lf` 后运行 `dotnet format --verify-no-changes`，报告约 3,840 项行尾、using 排序和空白差异，远超本 feature 的行为改动范围。

## 根因与发现方式

仓库长期混用 CRLF 与其他既有格式，且此前没有仓库级格式基线。`dotnet format` 会同时检查新增规则和历史债务，无法只表达当前 feature 是否引入新格式问题。移除强制 LF 后，历史 using 与空白债务仍然存在。

## 正确做法

1. 在独立、工作树干净的机械格式化 feature 中统一格式，避免混入行为变化。
2. 记录格式化前后的构建与测试结果，并隔离并行 feature 的文件。
3. 只有仓库能够无修改通过 `dotnet format --verify-no-changes` 后，才把该命令设为阻断式 CI 门禁。
4. Windows 仓库若没有跨平台硬需求，不要仅为追求统一而强制切换全部历史行尾。

## 禁止做法

- 不在业务或重构 feature 中顺手接受数千项机械格式改动。
- 不添加当前主分支必然失败的格式门禁。
- 不把行尾转换提示当作业务编译或测试失败。

## 适用范围

适用于 GalNet 的 `.editorconfig`、`dotnet format` 和 CI 格式检查。来源与验证记录见 `features/F-20260929-04-quality-baseline/summary.md`。
