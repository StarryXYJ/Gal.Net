---
name: galnet-feature-design
description: "为已有 GalNet feature 形成或修订设计文档，分析现有架构、候选方案、取舍和边界；不实现代码。"
---

# Design a GalNet Feature

## 适用场景

feature 已创建，或用户要求为某个 GalNet 改动设计方案、评估架构方向时使用。没有 feature 目录时可以独立输出设计，但应明确记录假设和归属。

## 工作方式

1. 读取 `.agents/agent-knowledge.md`、对应 `feature.md`、相关 `docs/spec/` 和 `docs/design/`。
2. 检查真实代码和项目依赖，区分当前事实、目标状态和推测。
3. 在 `features/<feature-id>/design.md` 中记录：问题、目标、设计边界、数据流、接口、模块职责、候选方案、取舍、兼容性、迁移、风险和验收影响。
4. 只在架构决定确实跨模块、难以逆转或需要长期引用时提出 ADR；ADR 接受前保持 Proposed，不替用户宣布 Accepted。
5. 更新 `feature.md` 的链接和设计状态，但不伪造用户尚未确认的决定。

## 边界

- `design.md` 解释“为什么这样设计”，不要变成逐文件施工清单。
- 不把未来设想写进 `docs/spec/` 的当前事实。
- 不修改业务代码或运行配置。

