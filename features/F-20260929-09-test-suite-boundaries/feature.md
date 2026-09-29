---
id: F-20260929-09-test-suite-boundaries
title: 测试套件按生产边界重组
type: refactor
status: implementing
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# 测试套件按生产边界重组

## 目标

完成维护性路线图 Phase 3，拆除引用几乎全部生产程序集的 `GeneralTest` 聚合项目，使纯逻辑、UI 和跨模块集成测试可以按边界独立编译和执行。

## 范围

- 建立 Core、Runtime、Builtins、Assets、Storage、Editor.Shared、Editor、Presentation、Architecture 和 Integration 测试边界。
- 将现有测试按被测职责迁移，保留测试行为与数量。
- 每个测试项目只引用测试所需的生产项目；共享 NUnit/coverage 配置集中维护。
- 更新 solution、lock files 和 CI，使纯逻辑测试与 UI/集成测试分组执行。

## 非目标

- 不重写生产代码或测试行为。
- 不为了追求单一程序集引用而复制复杂 test double；确有跨模块协作的测试归入 Integration。
- 不在本 feature 引入新的 coverage 平台或设定覆盖率阈值。
- 不修改历史 feature 中记录的旧测试命令。

## 验收标准

- `GeneralTest` 项目和目录删除，所有既有测试完成数量对账。
- Core 测试不编译 Runtime、Editor、Sample 或 Avalonia；其他测试项目引用与名称匹配。
- CI 分开执行纯逻辑、Presentation/Editor UI 和 Integration 测试，并继续收集 trx/coverage。
- 全部测试、Headless 和完整 solution 构建通过。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [路线图 Phase 3](../F-20260929-03-maintainability-roadmap/phase-plan.md)
