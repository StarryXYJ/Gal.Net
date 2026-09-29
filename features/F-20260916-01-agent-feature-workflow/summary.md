# Summary：Feature 工作流与 Agent 知识系统

## 实现结果

已建立 GalNet 的推荐 feature 工作流、agent 长期上下文、即时 lesson、lesson 晋升和正式文档同步机制。2026-09-29 增补 `galnet-feature-fast-track`，为边界清晰的小到中等改动提供设计规划、实现验证、总结收尾三段快速通道。

## 实现概要

- `features/` 保存一次实现的需求、设计、Phase Plan、review 和开发者可读总结。
- `.agents/agent-knowledge.md` 保存精简项目上下文。
- `.agents/lessons/` 支持开发过程中即时追加踩坑。
- `.agents/skills/` 保存可版本化的 skill 源文件。
- `galnet-feature-fast-track` 编排既有 feature 阶段 skill，在风险升高或范围不清时退回普通流程。
- `galnet-sync-skills` 负责同步仓库源文件和 Codex 用户级运行时副本。
- `docs/design/feature-workflow.md` 记录推荐流程，但不把它作为硬性状态机。

## 关键决定

- 经验先作为 lesson 记录，确认可复用后再成为独立 skill。
- 仓库 skill 源文件是唯一真源，用户级目录只是安装副本。
- agent 经验、feature 历史和开发者正式文档分层保存。
- 快速通道只压缩流程切换成本，不降低设计、测试、文档和用户授权要求。

## 正式文档

- 已将可复用的推荐流程和文档职责边界同步至 [Feature 工作流与 Agent 知识维护](../../docs/design/feature-workflow.md)。
- 已在同一文档补充快速通道的适用范围、产物和降级条件。

## 验证

- 12 个仓库源 skill package 的 frontmatter、名称、描述和占位符通过人工检查。
- 2026-09-16 的 11 个初始 skill 曾完成源文件与用户级副本 SHA-256 一致性检查；本次新增的 `galnet-feature-fast-track` 尚未写入用户级副本。
- 未修改 GalNet 业务代码。
- `quick_validate.py` 因当前 Python 环境缺少 `PyYAML` 未执行；已用 PowerShell 结构检查替代。

## Agent 经验

已记录：`G:\program\GalDotNet\.agents\lessons\L-20260916-01-skill-source-install-sync.md`。

## 后续工作

- 在具备 Python 的环境中补跑 skill-creator 官方校验器。
- 通过实际 feature 观察 skill 的自动触发和经验晋升效果，再按真实问题做窄化调整。
