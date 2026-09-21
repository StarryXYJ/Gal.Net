# Agent Lessons

这里保存给 agent 使用的原子经验。经验可以在 feature 实现过程中随时追加，不需要等到收尾。

每条经验建议使用文件名：

```text
L-YYYYMMDD-NN-short-slug.md
```

内容至少包括：

```text
# 标题

状态：candidate | confirmed | promoted | superseded
来源 Feature：F-...

## 现象
## 根因
## 如何发现
## 正确做法
## 适用范围
## 不要做什么
## 证据和关联文档
```

一次性、低风险的临时问题可以只记录在对应 feature 的过程文档中；非显然、代价高或可能重复出现的问题应记录在这里。被确认可复用的经验由 `galnet-promote-lesson` 晋升为独立 skill，但原始 lesson 保留不删除。

