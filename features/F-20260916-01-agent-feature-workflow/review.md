# Review：Feature 工作流与 Agent 知识系统

## 审核范围

- `.agents/` 下的 agent 上下文、lessons、skill index 和 11 个项目 skill。
- `features/README.md`、本 feature 文档和 `docs/design/feature-workflow.md`。
- Codex 用户级 `galnet-*` skill 安装副本。

## 检查结果

- 每个 skill 都有合法的 lowercase-hyphen 名称和非空 description。
- 每个 skill 都包含 frontmatter 和明确边界。
- 源文件与用户级安装副本通过 SHA-256 内容核对。
- `git status` 确认没有业务代码修改。
- `git diff --check` 没有发现已跟踪 diff 的空白错误；新增文件另进行了结构检查。

## 验证限制

skill-creator 的 `quick_validate.py` 未能执行，因为当前环境没有可用的 Python。已使用人工 frontmatter、目录、占位符和源/目标哈希检查替代；后续环境具备 Python 时应再次运行官方校验器。

## 结论

`pass-with-follow-up`：设计和文件结构没有发现阻塞问题；后续应在具备 Python 的环境中补跑 skill-creator 校验器，并在实际 feature 中验证自动触发质量。

