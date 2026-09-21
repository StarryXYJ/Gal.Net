---
name: galnet-feature-phase-plan
description: "把 GalNet feature 的设计拆成有依赖、可验证、有退出条件的实现 Phase；不编写业务代码。"
---

# Plan GalNet Feature Phases

## 适用场景

用户要求制作实施计划、拆分阶段，或 feature 已有设计需要转成可执行顺序时使用。它也可以独立为已有改动补充计划。

## 工作方式

1. 读取 `feature.md`、`design.md`、项目上下文和相关代码。
2. 在 `features/<feature-id>/phase-plan.md` 中按依赖顺序列出 Phase。
3. 每个 Phase 写明目标、前置条件、涉及模块、任务、测试、文档、风险和明确的退出条件。
4. 优先拆成能独立验证的垂直切片；把大规模重命名、格式化或纯重构与行为变化分开。
5. 标记 Phase 状态为 `planned`、`in-progress`、`verified` 或 `skipped`，但不要把推荐流程当成全局硬门禁。
6. 计划发生变化时保留变更原因，并让最终偏差在 `summary.md` 中可追溯。

## 边界

- 不直接修改业务代码。
- 不把不确定的实现细节伪装成已确认决定。
- 不为了让计划看起来完整而拆出没有独立价值的微型 Phase。

