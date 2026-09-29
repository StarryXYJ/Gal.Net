# 设计：Feature 工作流与 Agent 知识系统

## 总体设计

将知识分成四层：

```text
Feature 文档       一次实现的过程和人类可读历史
Agent lessons      具体踩坑和修正，服务后续 agent
独立 skills        已确认可复用的操作规则
正式 docs          当前系统事实、长期设计和架构决定
```

Feature 推荐按以下顺序推进：

```text
create → design → phase-plan → implement → review → closeout
```

这个顺序是推荐路径，不是每个 skill 的硬性依赖；review、lesson capture、docs sync 等能力可以单独调用。

对于边界清晰的小到中等改动，新增快速通道：

```text
设计规划 → 实现验证 → 总结收尾
```

快速通道由 `galnet-feature-fast-track` 编排，仍然复用 feature、design、phase-plan 和 summary 文档，只是允许内容更聚焦。遇到需求不清、需要 ADR、大范围迁移、核心分层风险、工作树冲突或无法定义可信验证时，快速通道应退回普通流程。

## 目录边界

- `features/<id>/` 保存一次 feature 的需求、设计、计划、审核和总结。
- `.agents/agent-knowledge.md` 保存精简的长期 agent 上下文。
- `.agents/lessons/` 保存原子化 agent 经验。
- `.agents/skills/` 保存可版本化 skill 源文件。
- `docs/spec/`、`docs/design/` 和 `docs/adr/` 保存开发者可读的正式内容。

## 经验晋升

经验先即时记录为 lesson；当它重复出现、风险较高或跨 feature 复用时，再提炼为独立 skill。原始 lesson 保留，便于追溯来源和判断规则是否过时。

## 运行时安装

仓库 `.agents/skills/` 是唯一真源，Codex 用户级目录只是运行时副本。新增或更新 skill 后由 `galnet-sync-skills` 同步，不允许从用户级副本反向覆盖源文件。

