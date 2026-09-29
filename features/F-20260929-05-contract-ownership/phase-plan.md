---
feature: F-20260929-05-contract-ownership
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 契约与消费者盘点

**状态：verified**

目标：用真实代码和项目引用确定当前边界，不从程序集名称推测职责。

- 枚举 `GalNet.Storage.Abstractions` 的公开类型、命名空间和生产消费者。
- 枚举 Core 中历史宿主/UI 服务接口及其真实消费者。
- 记录当前生产项目引用图和架构文档冲突。

**验证（2026-09-29）：** 已确认 Storage.Abstractions 包含 14 个公开类型、跨三个领域，有六个生产项目直接引用；已将逐类型目标归属写入 `design.md`。

退出条件：每类契约均有当前事实和目标归属，不存在仅凭命名做出的迁移决定。

## Phase 2 - ADR 与目标依赖规则

**状态：verified**

目标：把用户接受的方案 C 固化为长期决策与可执行边界。

- 创建 ADR，记录方案 A/B/C、决定、后果和非目标。
- 定义内层与共享项目的目标直接依赖 allowlist。
- 定义程序集与根命名空间一致性规则、迁移顺序和例外退出条件。

**验证（2026-09-29）：** 用户明确选择方案 C；`docs/adr/0001-runtime-storage-contract-ownership.md` 已标记 Accepted。目标矩阵明确 Runtime 不新增 Abstractions 项目，Storage.Abstractions 在 Phase 2a 删除。

退出条件：ADR 可独立解释决定，Phase 2a/2b 不需要重新猜测契约归属。

## Phase 3 - 首批架构测试

**状态：verified**

目标：自动阻止内层项目新增反向平台依赖，同时容纳已记录的迁移期例外。

- 在 `GeneralTest` 中增加基于 `XDocument` 的项目引用测试，不增加第三方包。
- 验证 Core 零项目引用。
- 验证内层/共享项目的直接依赖只属于当前 allowlist。
- 将 `Storage.Abstractions` 标记为 Phase 2a 必须删除的显式例外。
- 运行定向测试和全量 `GeneralTest`。

风险：测试若冻结全部外层组合根会阻碍正常宿主演进，因此只覆盖内层和共享边界。

退出条件：测试在当前仓库通过，故意添加未允许的平台引用时会给出包含项目名和依赖名的失败信息。

**验证（2026-09-29）：** 新增 13 个架构测试，覆盖 12 个内层/共享项目 allowlist 及 Storage.Abstractions 六个迁移消费者。定向测试 13/13、全量 `GeneralTest` 271/271 通过；新增测试无编译器或分析器告警。

## Phase 4 - 文档同步与收尾

**状态：verified**

目标：让路线图、ADR、feature 历史和 agent 上下文互相可追溯。

- 根据测试结果更新 Phase 证据和 feature 状态。
- 在路线图中把 Phase 1 标记 verified，并链接本 feature 总结。
- 同步当前可执行的依赖维护规则；不把尚未迁移的目标结构写成当前 spec。
- 生成 `summary.md`，记录 Phase 2a/2b 的输入和剩余例外。

退出条件：所有验证通过，目标结构与当前例外清楚分层，后续迁移有唯一决策来源。

**验证（2026-09-29）：** ADR、feature 设计、路线图、当前架构 spec 与 agent 上下文已互相链接；目标结构只存在于 ADR/design，spec 明确记录尚未消除的迁移例外。
