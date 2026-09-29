---
feature: F-20260929-08-presentation-builtins-namespaces
status: done
updated: 2026-09-29
---

# Presentation 与 Builtins 命名空间归位：实现总结

## 结果

维护性路线图 Phase 2b 已完成。Presentation view 契约与 Builtins entry 类型不再伪装成 Core 类型，程序集、目录和公开命名空间现在表达一致的所有权。

## 命名空间调整

- `IGameView`、细分 presenter、展示 request DTO 和 `CompositeGameView` 从 `GalNet.Core.View` 迁入 `GalNet.Presentation.Abstractions.View`。
- Builtins 定义的 dialogue、layer、animation、media、effect、particle、flow 和 gallery entry 类型从 `GalNet.Core.Entry` 迁入 `GalNet.Primitives.Builtins`。
- Core 继续拥有通用 entry 基类、schema 和 catalog；所有仓库内生产、示例和测试消费者已更新。
- 采用直接迁移，不保留旧命名空间兼容层；稳定 entry `TypeId` 和文件格式未改变。

## 防回归

架构测试现在扫描 Presentation.Abstractions 和 Primitives.Builtins 的源码 namespace，要求每个声明都位于对应程序集拥有的根命名空间下。

## 验证

- GeneralTest Release：277/277 通过。
- GalNet.Assets.Tests Release：34/34 通过。
- Editor Headless、Sample Headless：构建成功。
- 完整 Android、Browser、Desktop、iOS solution：0 错误；保留 1 条既有 WebAssembly native reference 警告。
- 搜索确认生产代码和测试无 `GalNet.Core.View` 引用，Builtins 无 `GalNet.Core.Entry` namespace 声明。

## 文档

- [架构规范](../../docs/spec/architecture.md)记录程序集根命名空间所有权。
- [条目类型规范](../../docs/spec/entry-types.md)记录 Builtins 与 Core entry 基础设施边界。
