---
name: galnet-capture-lesson
description: "在 GalNet 开发过程中即时记录非显然、可复现或代价较高的踩坑和修正，供后续 agent 防止重复犯错；不修改业务代码。"
---

# Capture a GalNet Agent Lesson

## 适用场景

当构建、测试、运行、工具操作或架构判断出现非显然问题，当用户纠正了 agent，或发现某个问题很可能在下一个 feature 重复出现时立即使用。

## 工作方式

1. 先确认现象和根因，不把猜测写成事实。
2. 在 `.agents/lessons/` 创建新的 `L-YYYYMMDD-NN-short-slug.md`。
3. 记录现象、根因、发现方式、正确做法、适用范围、禁止做法、来源 feature 和关联文档。
4. 标记状态为 `candidate`；经过复现或用户确认后改为 `confirmed`。
5. 更新 `.agents/agent-knowledge.md` 的简短索引，但不要把完整事故经过复制进去。

## 记录原则

- 边做边写，不等待 feature 收尾。
- 一条 lesson 只描述一个规则或陷阱。
- 保留失败原因和证据，不只记录最终解决方案。
- 一次性、低风险、没有复用价值的细节可以留在 feature 过程文档中。

## 边界

- 不因为一次普通编译错误就生成泛化规则。
- 不写秘密、个人评价或无法验证的归因。
- 不在本 skill 中直接生成独立 skill；需要晋升时使用 `galnet-promote-lesson`。

