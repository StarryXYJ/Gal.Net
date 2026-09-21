---
name: galnet-feature-create
description: "当用户要求按 GalNet feature 流程开始一次实现时，新建独立 feature 记录并澄清需求；不编写设计或代码。"
---

# Create a GalNet Feature

## 适用场景

用户说“开始一个 feature”“准备实现某功能”，或明确选择推荐的 feature 工作流时使用。纯问答、单独 review 或单独文档编辑不需要触发本 skill。

## 工作方式

1. 读取 `.agents/agent-knowledge.md`、现有 feature 目录和相关正式文档。
2. 检查是否已有表达同一目标的 feature；发现重复时说明差异，不覆盖旧记录。
3. 在 `features/` 下创建新的 `F-YYYYMMDD-NN-short-slug` 目录。
4. 创建 `feature.md`，记录用户原始目标、问题、范围、非目标、验收标准、约束、假设、开放问题和相关链接。
5. 将状态设为 `discovery`，然后只提出会影响方案或范围的高价值问题。
6. 用户补充信息后更新 `feature.md`；需求未明确时停留在 discovery，不提前写代码。

## 输出

至少产生：

```text
features/<feature-id>/feature.md
```

## 边界

- 不创建设计方案、Phase Plan 或实现代码。
- 不因模板完整而替用户决定产品范围。
- 如果用户没有选择 feature 工作流，单独的实现请求可以由 `galnet-feature-implement` 处理，但应避免虚构 feature 记录。

