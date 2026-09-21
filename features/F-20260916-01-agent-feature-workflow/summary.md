# Summary：Feature 工作流与 Agent 知识系统

## 实现结果

已建立 GalNet 的推荐 feature 工作流、agent 长期上下文、即时 lesson、lesson 晋升和正式文档同步机制，并安装当前 11 个项目 skill 到 Codex 用户级发现目录。

## 实现概要

- `features/` 保存一次实现的需求、设计、Phase Plan、review 和开发者可读总结。
- `.agents/agent-knowledge.md` 保存精简项目上下文。
- `.agents/lessons/` 支持开发过程中即时追加踩坑。
- `.agents/skills/` 保存可版本化的 skill 源文件。
- `galnet-sync-skills` 负责同步仓库源文件和 Codex 用户级运行时副本。
- `docs/design/feature-workflow.md` 记录推荐流程，但不把它作为硬性状态机。

## 关键决定

- 经验先作为 lesson 记录，确认可复用后再成为独立 skill。
- 仓库 skill 源文件是唯一真源，用户级目录只是安装副本。
- agent 经验、feature 历史和开发者正式文档分层保存。

## 正式文档

- 已将可复用的推荐流程和文档职责边界同步至 [Feature 工作流与 Agent 知识维护](../../docs/design/feature-workflow.md)。

## 验证

- 11 个 skill package 的 frontmatter、名称、描述和占位符通过人工检查。
- 源文件与用户级副本 SHA-256 一致。
- 未修改 GalNet 业务代码。
- `quick_validate.py` 因环境缺少 Python 未执行，已在 `review.md` 记录。

## Agent 经验

已记录：`G:\program\GalDotNet\.agents\lessons\L-20260916-01-skill-source-install-sync.md`。

## 后续工作

- 在具备 Python 的环境中补跑 skill-creator 官方校验器。
- 通过实际 feature 观察 skill 的自动触发和经验晋升效果，再按真实问题做窄化调整。
