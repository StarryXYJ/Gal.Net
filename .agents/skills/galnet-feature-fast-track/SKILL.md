---
name: galnet-feature-fast-track
description: "按 GalNet 快速 feature 通道处理边界清晰的小到中等改动：压缩设计规划、实现验证和总结收尾；复杂或不清晰时退回普通流程。"
---

# Fast-Track a GalNet Feature

## 适用场景

用户明确要求“快速 feature 通道”“一条龙 feature”“设计规划、实现、总结一起做”，并且目标相对清楚、影响范围可控时使用。

如果用户只是询问流程、只要求单独设计/实现/总结，使用对应单步 skill。简单修复、临时问答和独立 review 不需要强行创建 feature。

## 入口判定

1. 读取 `.agents/agent-knowledge.md`、相关正式文档、现有 feature 目录和 `git status`。
2. 判断是否复用已有 feature；如果没有对应记录，创建新的 `features/F-YYYYMMDD-NN-short-slug/`。
3. 只有用户明确要求完成或实现该 feature 时，才进入代码修改；单纯“规划一下”不等于实现授权。
4. 遇到以下情况时退回普通 feature 流程，并说明原因：
   - 需求或验收标准仍不清楚。
   - 需要 ADR、长期架构决策或大范围迁移。
   - 影响核心分层、存档语义、公共 API 或多个并行 feature。
   - 工作树中已有改动与本任务冲突。
   - 无法定义快速、可信的验证方式。

## 三段流程

### 1. 设计规划

遵守 `galnet-feature-create`、`galnet-feature-design` 和 `galnet-feature-phase-plan` 的边界，但允许文档轻量化：

- `feature.md` 记录目标、范围、非目标、验收标准、约束和开放问题。
- `design.md` 只写会影响实现判断的架构、数据流、接口、取舍和风险。
- `phase-plan.md` 可以只有一个或少数垂直切片，但每个 Phase 仍要有退出条件和验证方式。

不要为了模板完整而填充无信息内容；缺少关键决策时停在设计规划段。

### 2. 实现验证

遵守 `galnet-feature-implement`：

- 一次推进一个可验证切片，完成后更新 Phase 状态和证据。
- 发现设计偏差时先更新 feature 文档，再继续实现。
- 同步必要测试和文档；非显然踩坑立即使用 `galnet-capture-lesson`。
- 如果实现暴露出超出快速通道的风险，停止压缩流程并切回普通 feature。

### 3. 总结收尾

遵守 `galnet-feature-closeout`：

- 在 `summary.md` 记录实际实现、验证证据、计划偏差、已知限制和后续工作。
- 将稳定事实同步到 `docs/spec/` 或 `docs/design/`；只把 agent 经验放入 `.agents/lessons/` 或 `.agents/agent-knowledge.md`。
- 需要独立审核或风险较高时，先使用 `galnet-feature-review`，不要把 review 隐藏进 summary。

## 完成条件

快速通道完成时，至少应有：

```text
features/<feature-id>/feature.md
features/<feature-id>/design.md
features/<feature-id>/phase-plan.md
features/<feature-id>/summary.md
```

如果实现、测试或文档仍有阻塞，保留真实状态，不把 feature 标记为完成。

## 边界

- 快速通道只是压缩流程切换成本，不降低设计、测试、文档和用户授权要求。
- 不把多个无关改动塞进同一个快速 feature。
- 不因用户希望“快”而跳过工作树保护、分层边界或失败验证记录。
