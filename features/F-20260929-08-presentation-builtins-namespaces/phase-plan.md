---
feature: F-20260929-08-presentation-builtins-namespaces
status: done
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 命名空间迁移与门禁

**状态：verified**

- 迁移 Presentation view 契约和 Builtins entry 类型。
- 更新所有仓库内消费者及测试。
- 增加两个程序集的 namespace ownership 测试。

退出条件：编译通过；搜索无旧 Presentation namespace，Builtins 不再声明 Core namespace；定向测试通过。

**验证（2026-09-29）：** Presentation.Abstractions、Primitives.Builtins 与 GeneralTest 编译通过；搜索确认生产代码和测试无 `GalNet.Core.View`，Builtins 无 `GalNet.Core.Entry` namespace 声明；namespace ownership、entry、runtime 和 presentation 测试随 GeneralTest 通过。

## Phase 2 - 全量验证与收尾

**状态：verified**

- 运行 GeneralTest、Assets Tests、Headless 与完整 solution 构建。
- 同步架构 spec、路线图、agent knowledge 与 feature summary。
- 检查 diff 并创建独立 Git 提交。

退出条件：路线图 Phase 2b 的退出条件均有验证证据。

**验证（2026-09-29）：** GeneralTest 277/277、Assets Tests 34/34 通过；Editor Headless、Sample Headless 和完整 Android/Browser/Desktop/iOS solution 构建成功。完整构建保留 1 条既有 WebAssembly native reference 警告。
