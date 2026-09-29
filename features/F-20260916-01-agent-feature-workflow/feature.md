---
id: F-20260916-01-agent-feature-workflow
title: GalNet Feature 工作流与 Agent 知识系统
type: tooling
status: done
created: 2026-09-16
updated: 2026-09-29
---

# GalNet Feature 工作流与 Agent 知识系统

## 问题与目标

项目需要一套可重复的 feature 实现记录、agent 踩坑记忆和 skill 晋升机制，避免上下文压缩后重复犯错，同时让开发者能看到实现设计和结果。

## 范围

- 建立推荐的 feature 文档结构。
- 建立 agent 专用上下文和 lesson 目录。
- 建立 feature 阶段 skill、知识维护 skill 和 skill 同步 skill。
- 补充小到中等改动使用的快速 feature 通道。
- 安装当前项目 skill 到 Codex 用户级发现目录。

## 非目标

- 不修改 GalNet 业务代码。
- 不把推荐工作流变成所有任务的强制门禁。
- 不将 agent 私人经验复制进正式系统规范。

## 验收标准

- 新 feature 可以使用统一的 `feature.md`、`design.md`、`phase-plan.md`、`review.md` 和 `summary.md`。
- lesson 可以在开发过程中即时记录，并可晋升为独立 skill。
- 仓库 skill 源文件和 Codex 用户级副本可以被检查和同步。
- 推荐工作流、agent 文档和开发者文档的职责边界明确。
- 边界清晰的小到中等 feature 可以走设计规划、实现验证、总结收尾三段快速通道，并在复杂或不清晰时退回普通流程。

