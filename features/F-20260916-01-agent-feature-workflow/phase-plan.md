# Phase Plan：Feature 工作流与 Agent 知识系统

## Phase 1：知识和文档分层

状态：verified

- 建立 `.agents/agent-knowledge.md`、`.agents/lessons/` 和 `.agents/skill-index.md`。
- 建立 `features/README.md` 和 `docs/design/feature-workflow.md`。
- 明确 agent 文档、feature 历史和正式文档的边界。

退出条件：目录职责、推荐流程和维护规则有可读文档。

## Phase 2：Feature 阶段 Skill

状态：verified

- 创建 feature、设计、Phase Plan、实现、review 和 closeout skill。
- 每个 skill 有独立触发条件、输出和边界。

退出条件：每个阶段 skill 都有合法 `SKILL.md`，且不要求完整工作流才能单独使用。

## Phase 3：Agent 知识 Skill

状态：verified

- 创建上下文、即时 lesson、lesson 晋升和开发者文档同步 skill。
- 记录首次确认的 skill 源文件/安装副本同步经验。
- 添加 `galnet-sync-skills`，避免 source/runtime drift。

退出条件：经验可以边做边记，并能通过独立 skill 晋升和安装。

## Phase 4：Review 与收尾

状态：verified

- 检查所有 skill 的 frontmatter、命名、占位符和安装副本。
- 记录验证限制。
- 生成本 feature 的 review 和 summary。

退出条件：仓库结构和用户级 skill 副本一致，没有业务代码改动。

