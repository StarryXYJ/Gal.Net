---
name: galnet-promote-lesson
description: "将已确认、重复出现或高风险的 GalNet agent lesson 提炼成独立、可触发、可验证的 skill，并维护其来源和生命周期。"
---

# Promote a GalNet Lesson to a Skill

## 适用场景

用户要求把经验做成 skill，或 feature 收尾时发现某条 lesson 已经具有跨 feature 的稳定复用价值时使用。

## 晋升条件

至少满足以下一项，并且规则边界清楚：

- 同类问题已经重复出现。
- 问题风险高或返工代价大。
- 规则适用于多个模块或多个 feature。
- 能写出明确的触发条件、检查项、正确做法和验证方式。

## 工作方式

1. 阅读原始 lesson、来源 feature、当前正式文档和已有相近 skill。
2. 选择短小、动作导向、不会与现有 skill 冲突的 lowercase-hyphen 名称。
3. 在 `.agents/skills/<skill-name>/SKILL.md` 创建或更新独立 skill，包含触发条件、关键规则、操作步骤、边界和验证方式。
4. 保留原始 lesson，并将状态改为 `promoted`，记录生成的 skill 路径。
5. 更新 `.agents/skill-index.md`，登记 skill 的用途、状态和来源。
6. 如果可用，运行 skill-creator 的 `quick_validate.py`；同时检查描述是否会误触发、指令是否过度限制无关任务。
7. 使用 `galnet-sync-skills` 将通过检查的源文件同步到当前 Codex 的用户级发现目录；如果需要新的外部写权限，先请求授权。

## 生命周期

```text
candidate → confirmed → promoted → superseded
```

架构变化导致规则失效时，标记旧 skill 为 `superseded`，不要静默改写历史经验。

## 边界

- 不把完整事故叙述复制进 skill。
- 不为一次性、低风险问题批量生成 skill。
- 不创建含糊的“万能项目 skill”；每个 skill 必须有清晰触发范围。
