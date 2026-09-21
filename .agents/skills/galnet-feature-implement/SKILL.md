---
name: galnet-feature-implement
description: "实现或继续实现 GalNet feature 的代码、测试和必要文档，按可验证的 Phase 推进并即时记录踩坑；需要用户明确授权修改代码。"
---

# Implement a GalNet Feature

## 适用场景

用户明确要求开始、继续或完成实现时使用。已有 feature 时读取它的文档；没有采用 feature 工作流时可以独立处理，但不要凭空创建历史记录。

## 开始前

1. 读取 `.agents/agent-knowledge.md` 和相关 lessons。
2. 检查 `git status`，识别并保护用户已有改动。
3. 如果存在 feature，读取 `feature.md`、`design.md` 和 `phase-plan.md`，确定当前范围与 Phase。
4. 确认用户确实授权修改代码；设计或计划中的“建议”不等于实现授权。

## 实现方式

- 一次推进一个可验证的 Phase，完成后更新 Phase 状态和证据。
- 遵守当前项目的架构边界，不为了局部方便破坏 Core/Runtime 与平台实现的分层。
- 同步补充相关测试；根据风险运行合适的构建、单元、集成或手工验证。
- 发现非显然的失败原因、用户纠正或可复用陷阱时，立即使用 `galnet-capture-lesson`。
- 需求、设计或架构发生实质变化时，先记录偏差并回到设计/计划，不静默扩大范围。

## 完成条件

只有在实现、测试和相关文档都有证据时，才把当前 Phase 标记为 `verified`。不要仅因代码能编译就宣称 feature 完成。

## 边界

- 不擅自重置、覆盖或删除用户改动。
- 不把无关重构、格式化和清理混入 feature。
- 不把失败测试隐藏起来；失败原因和临时绕过方式应进入 lesson 或 feature 记录。

