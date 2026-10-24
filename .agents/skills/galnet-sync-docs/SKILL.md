---
name: galnet-sync-docs
description: "将 GalNet 已验证的实现事实同步到正确的开发者文档层级，区分 spec、manual 与 feature 历史；不记录 agent 私人经验。"
---

# Sync GalNet Developer Documentation

## 适用场景

feature 完成、代码行为已经改变，或用户单独要求维护开发者文档时使用。可以独立运行，也可以由 `galnet-feature-closeout` 使用。

## 文档归属

- `docs/spec/`：当前已经实现并应被使用的稳定事实、契约和行为。
- `docs/manual/`：在既有边界内接入新表现层或定制 Sample 的操作指南。
- `docs/README.md`、`docs/philosophy.md`、`docs/glossary.md`：文档索引、架构理念和项目术语。
- `features/<id>/`：一次 feature 的过程和历史。
- `.agents/`：agent 专用上下文、lesson 和 skill 源文件。

## 工作方式

1. 以实际代码、测试和 review 证据为准，确认文档描述的是当前事实而不是计划。
2. 更新已有相关页面，优先保持一个事实来源，避免新建重复说明。
3. 将已验证的架构决定和约束整合进对应 spec；设计取舍、候选方案和实施计划保留在 feature 文档中。
4. 检查 spec、manual 与 feature 历史是否各自只表达其职责范围。
5. 在 feature 的 `summary.md` 中链接更新后的规范。

## 边界

- 不把 agent lesson 原样写进开发者文档。
- 不用 `docs/spec/` 保存尚未实现的未来方案。
- 不为了同步文档而擅自修改代码行为；发现代码和文档冲突时记录冲突并验证。

