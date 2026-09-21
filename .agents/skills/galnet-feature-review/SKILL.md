---
name: galnet-feature-review
description: "独立审核 GalNet feature 或代码 diff 是否符合需求、设计、测试和文档要求；默认只报告问题，不修改代码。"
---

# Review a GalNet Feature

## 适用场景

用户要求 review、审核、检查完成度，或实现已准备好接受独立检查时使用。可以审核完整 feature，也可以单独审核任意 diff。

## 审核顺序

1. 读取 `feature.md`、`design.md`、`phase-plan.md`（如果存在）和实际 diff。
2. 先检查目标和整体设计，再检查行为、边界条件、并发/生命周期、依赖方向、测试质量和文档同步。
3. 对每个问题说明证据、影响、严重级别和建议修复方向；区分 Blocker、P1、P2 和 Nit。
4. 运行与风险相称的只读验证或测试，并记录实际结果。
5. 有 feature 目录时写入 `review.md`；没有时直接给出审核结果。

## 结论

使用 `pass`、`pass-with-follow-up` 或 `blocked`，不要用模糊的“看起来没问题”替代证据。`pass` 表示没有发现阻止交付的问题，不表示代码完美。

## 边界

- 默认不修改代码、设计或测试；修复必须由用户另行授权。
- 不把个人偏好当作阻塞问题。
- 不因为缺少完整推荐工作流文档就拒绝审核一个独立 diff，但要说明审核上下文缺失。

