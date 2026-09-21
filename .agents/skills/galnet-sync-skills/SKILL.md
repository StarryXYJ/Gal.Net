---
name: galnet-sync-skills
description: "将 GalNet 仓库中的 skill 源文件同步到当前 Codex 的用户级发现目录，并检查源文件与运行时副本一致；不修改业务代码。"
---

# Sync GalNet Skills

## 适用场景

在 `.agents/skills/` 中新增或修改 skill、经验晋升为 skill，或怀疑 Codex 正在使用旧版本时使用。

## 来源和目标

- 源文件：项目根目录下的 `.agents/skills/<skill-name>/`。
- 当前用户级目标：`C:\Users\Starry\.codex\skills/<skill-name>/`。

如果目标环境或用户不同，不要猜测路径；先确认实际 Codex skills 目录。

## 工作方式

1. 枚举源目录，确认每个 package 有合法的 `SKILL.md`、名称与目录一致，且没有未完成占位符。
2. 检查目标目录；只更新由本项目管理、名称明确匹配的 `galnet-*` package，不覆盖其他 skill。
3. 将源 package 的全部文件同步到目标 package，保持目录结构一致。
4. 比较源文件和目标文件的哈希或内容，确认同步成功。
5. 如果可用，运行 skill-creator 的 `quick_validate.py`；没有 Python 或校验器时记录环境限制并执行人工结构检查。

## 安全边界

- 用户级目录在项目外，写入前需要相应授权。
- 不删除目标目录中不属于本项目的 skill。
- 不用目标副本反向覆盖仓库源文件；仓库源文件是唯一真源。
- 不因为同步 skill 而修改项目代码或业务文档。

