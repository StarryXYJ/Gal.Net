---
name: galnet-feature-closeout
description: "收尾 GalNet feature，生成面向开发者的实现总结，整理 agent lessons，并同步已验证的正式文档；不掩盖未完成事项。"
---

# Close Out a GalNet Feature

## 适用场景

feature 实现和 review 后，或用户要求总结实现、整理经验、维护项目文档时使用。也可以对已有完成改动补做收尾。

## 工作方式

1. 读取 feature 全部文档、实际 diff、review 结果、测试输出和已记录的 lessons。
2. 在 `features/<feature-id>/summary.md` 中记录实现结果、修改模块、关键决定、验证证据、计划偏差、已知限制和后续工作。
3. 调用或执行 `galnet-sync-docs` 的规则，把稳定的当前事实同步到 `docs/spec/`、长期设计同步到 `docs/design/`，重要架构决定同步到 ADR；不要把 agent 日志复制进正式文档。
4. 将开发过程中新发现的经验整理到 `.agents/lessons/`；重复、跨 feature 或高风险的经验交给 `galnet-promote-lesson`。
5. 更新 `.agents/agent-knowledge.md`，只保留未来任务仍有用的规则、索引和来源。
6. 如果仍有 Blocker、失败验证或未完成迁移，明确保留状态，不把 feature 标记为完成。

## 结果分层

- `summary.md`：给开发者看的 feature 历史和实现概要。
- `.agents/lessons/`：给 agent 看的具体踩坑。
- `docs/`：当前系统和长期设计的正式文档。
- `.agents/agent-knowledge.md`：给 agent 的精简长期上下文。

## 边界

- 不为了“收尾整洁”改写历史设计或删除原始 lesson。
- 不把没有验证的推断写成当前系统事实。
- 不自动隐藏、关闭或删除未完成的后续工作。

