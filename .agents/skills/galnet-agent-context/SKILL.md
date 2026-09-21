---
name: galnet-agent-context
description: "在 GalNet 开始工作、上下文可能过时或需要整理长期项目记忆时，读取并维护 agent 专用上下文；不实现功能。"
---

# GalNet Agent Context

## 适用场景

在接手新的 GalNet 任务、上下文被压缩、项目架构发生变化，或用户要求整理 agent 记忆时使用。它是知识入口，不是功能实现流程的硬性前置条件。

## 工作方式

1. 从项目根目录读取 `.agents/agent-knowledge.md`。
2. 根据当前任务的关键词，只读取相关的 `.agents/lessons/` 和 `.agents/skills/`，不要默认加载全部经验。
3. 阅读对应的 `docs/spec/`、`docs/design/` 和 feature 文档；正式文档中的当前事实优先于旧 lesson。
4. 只把已经验证、未来仍有复用价值的事实写回 `agent-knowledge.md`。
5. 如果规则已经失效，标记关联 lesson 或 skill 为 `superseded`，不要继续保留为当前规则。

## 维护内容

- 架构和模块边界。
- 不应违反的不变量。
- 已验证的构建、测试和运行方式。
- 非显然的项目约定。
- 正式文档索引。
- 当前活动 feature 的指针。

## 边界

- 不记录秘密、完整对话、未经确认的猜测或一次性临时状态。
- 不把正式文档整段复制进 agent 上下文。
- 不因为上下文缺少就擅自扩大任务；缺失事实应通过仓库检查或向用户提问确认。

