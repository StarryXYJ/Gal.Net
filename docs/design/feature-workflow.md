# Feature 工作流与 Agent 知识维护

## 定位

这是 GalNet 推荐的实现工作流，不是强制状态机。它为较大的功能、架构调整和需要长期维护的改动提供统一记录；简单修复、独立审核或文档任务可以直接调用相应 skill。

本流程由 [F-20260916-01-agent-feature-workflow 的实现总结](../../features/F-20260916-01-agent-feature-workflow/summary.md) 建立；本页仅保留已验证、可长期复用的约定。

## 推荐顺序

```text
新建 Feature
    ↓
需求澄清
    ↓
设计文档
    ↓
Phase Plan
    ↓
逐阶段实现
    ↓
Review
    ↓
总结与知识沉淀
```

## 快速通道

边界清晰的小到中等 feature 可以使用 `galnet-feature-fast-track`，把流程压缩成三段：

```text
设计规划 → 实现验证 → 总结收尾
```

快速通道仍然保留 `feature.md`、`design.md`、`phase-plan.md` 和 `summary.md`，但内容可以只覆盖会影响实现和验证的事实。它不是质量门禁的豁免：如果需求不清、需要 ADR、大范围迁移、影响核心分层或无法定义可信验证，应退回普通 feature 流程。

## Feature 产物

每次采用此工作流的实现创建一个新的 `G:\program\GalDotNet\features\F-YYYYMMDD-NN-short-slug\` 目录。

### `feature.md`

需求和状态入口，包括问题、目标、范围、非目标、验收标准、约束、开放问题和相关链接。

### `design.md`

记录系统应该如何变化、候选方案、取舍、接口/数据边界、迁移和风险。它回答“为什么这样设计”。重要且难以逆转的决定另建 `G:\program\GalDotNet\docs\adr\` 下的 ADR。

### `phase-plan.md`

将设计拆成顺序明确、可独立验证的 Phase。每个 Phase 应有目标、依赖、修改范围、测试、文档和退出条件。它回答“如何分步实现”。

### `review.md`

记录独立审核结果、问题严重级别、验证证据和最终结论。Review 默认只报告问题，不自动修复。

### `summary.md`

面向开发者，记录实现结果、实际修改、关键决策、验证、偏差、已知限制和后续工作。agent 经验不应只依赖这里保存。

## Agent 经验

遇到以下情况时立即使用 `galnet-capture-lesson`：

- 非显然的构建、测试、运行或工具问题。
- 用户纠正了 agent 对架构或项目约定的理解。
- 原计划因现有代码事实而失败。
- 发现可能在多个 feature 重复出现的边界或陷阱。
- 重复一次会造成较大返工、数据损坏或错误架构方向的问题。

lesson 先保存在 `G:\program\GalDotNet\.agents\lessons\`。经过重复验证、风险评估或跨 feature 复用确认后，再使用 `galnet-promote-lesson` 生成独立 skill。独立 skill 应写规则和检查方式，不应复制完整事故流水账。

## 文档分层

- `G:\program\GalDotNet\.agents\`：只给 agent 使用的上下文、lesson 和 skill 源文件。
- `G:\program\GalDotNet\features\`：一次 feature 的过程和人类可读总结。
- `G:\program\GalDotNet\docs\spec\`：当前已经实现的系统事实。
- `G:\program\GalDotNet\docs\design\`：跨 feature 设计和长期计划。
- `G:\program\GalDotNet\docs\adr\`：重要架构决定。

不要把 agent 的临时经验直接写进 `docs/spec`；只有已经验证并属于系统公共约定的内容才同步到正式文档。
