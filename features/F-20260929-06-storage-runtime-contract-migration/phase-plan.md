---
feature: F-20260929-06-storage-runtime-contract-migration
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 移动契约定义

**状态：verified**

- 把资源协议移入 Core.Assets。
- 把内容、持久化、变量与 Gallery 组合类型移入 Runtime 对应命名空间。
- 保持类型成员与实现逻辑不变。

退出条件：新定义完整，旧项目中不再保留源码副本。

**验证（2026-09-29）：** 资源协议已进入 Core.Assets；内容、持久化、变量与 Gallery 组合类型已进入 Runtime 对应命名空间，14 个类型的成员与实现逻辑未改变。

## Phase 2 - 迁移消费者与项目图

**状态：verified**

- 更新生产代码和测试 using。
- 删除生产项目对 Storage.Abstractions 的 ProjectReference。
- 从 solution 删除旧项目，更新 lock file。
- 收紧架构测试到 ADR-0001 目标矩阵。

退出条件：仓库搜索无旧项目引用，locked restore 成功，架构测试通过。

**验证（2026-09-29）：** solution、六个直接消费者及所有 lock file 已移除旧项目；`dotnet restore GalNet.slnx --locked-mode` 成功。架构测试已改为要求旧项目和引用完全不存在。

## Phase 3 - 行为验证与收尾

**状态：verified**

- 运行 GeneralTest、Assets Tests、Editor Headless、Sample Headless 和适用解决方案构建。
- 同步 spec、路线图、feature 总结与 agent 上下文。

退出条件：迁移相关验证全绿，旧程序集完全删除，未混入接口语义变化。

**验证（2026-09-29）：** GeneralTest 270/270、Assets Tests 34/34 通过；Editor Headless 与 Sample Headless 构建成功。完整 solution 在非沙箱环境覆盖 Android、Browser、Desktop、iOS 并以 0 错误、1 条既有 WebAssembly native reference 警告完成。
