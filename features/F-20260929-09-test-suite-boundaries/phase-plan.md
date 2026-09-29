---
feature: F-20260929-09-test-suite-boundaries
status: implementing
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 纯逻辑与架构测试拆分

**状态：verified**

- 建立共享测试 MSBuild 配置。
- 新建 Architecture、Core、Runtime、Builtins 和 Storage.FileSystem 测试项目。
- 从 GeneralTest 移动对应测试，更新 solution，生成 lock files。

测试：新项目逐个通过；剩余 GeneralTest 与 Assets Tests 通过；总测试数仍为 311。

退出条件：Core 测试只引用 Core；首批项目名称、内容和依赖一致；创建独立 Git 提交。

**验证（2026-09-29）：** Architecture 14、Core 55、Runtime 32、Builtins 40、Storage 11、剩余 GeneralTest 125、Assets 34，合计 311/311 通过。Core.Tests 仅直接引用 Core；新项目均生成独立 lock file。

## Phase 2 - Editor、Presentation 与 Integration 拆分

**状态：planned**

- 新建 Editor.Shared、Editor、Presentation 与 Integration 测试项目。
- 从 GeneralTest 和 Assets Tests 迁移剩余测试，删除空的 GeneralTest。
- 收敛 Assets Tests 只测试 Assets，Storage 测试只测试文件系统实现。

测试：所有项目通过；总测试数与 311 基线一致；GeneralTest 不存在。

退出条件：测试项目边界完整，solution 无聚合测试项目；创建独立 Git 提交。

## Phase 3 - CI、文档与全量验证

**状态：planned**

- CI 分组 restore/test 并保留 trx 与 coverage artifacts。
- 更新架构 spec、路线图、agent knowledge 和 feature summary。
- 运行全部测试、Headless、完整 solution 与 diff 检查。

退出条件：路线图 Phase 3 的全部退出条件有验证证据；创建独立 Git 提交。
