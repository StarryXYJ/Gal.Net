# Skill 源文件和运行时副本必须同步

状态：confirmed
来源 Feature：F-20260916-01-agent-feature-workflow

## 现象

仓库中的项目 skill 源文件位于 `.agents/skills/`，Codex 实际发现的用户级副本位于 `C:\Users\Starry\.codex\skills\galnet-*`。只修改源文件不会自动更新运行时副本。

## 根因

项目源码和 Codex 用户级 skill 安装目录是两个独立位置。

## 如何发现

首次安装后通过源文件与目标文件的内容哈希进行核对。

## 正确做法

仓库源文件是唯一真源；新增或修改 skill 后使用 `galnet-sync-skills` 同步，并核对同步结果。

## 适用范围

所有位于 `.agents/skills/` 的 GalNet 项目 skill。

## 不要做什么

不要手工只修改用户级副本，也不要让用户级副本反向覆盖仓库源文件。

## 证据和关联文档

- `G:\program\GalDotNet\.agents\skills\galnet-sync-skills\SKILL.md`
- `G:\program\GalDotNet\.agents\agent-knowledge.md`
