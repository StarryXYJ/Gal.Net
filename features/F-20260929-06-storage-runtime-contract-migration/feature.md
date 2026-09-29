---
id: F-20260929-06-storage-runtime-contract-migration
title: 存储与运行时契约迁移
type: refactor
status: done
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# 存储与运行时契约迁移

## 原始目标

实施维护性路线图 Phase 2a 和 ADR-0001：把资源协议迁入 Core，把内容、保存、变量与 Gallery 组合契约迁入 Runtime，更新仓库内消费者并删除职责含混的 `GalNet.Storage.Abstractions`。

## 范围

- 将资源访问协议移动到 `GalNet.Core.Assets`，保持现有公开命名空间。
- 将内容端口移动到 `GalNet.Runtime.Content`。
- 将保存与玩家变量存储端口移动到 `GalNet.Runtime.Persistence`。
- 将变量桥接端口移动到 `GalNet.Runtime.Variables`。
- 将 `GalleryDataSource` 移动到 `GalNet.Runtime.Gallery`。
- 更新所有生产项目、测试、solution、架构测试和当前架构文档。
- 删除 `GalNet.Storage.Abstractions` 项目及其锁文件。

## 非目标

- 不改变存档、变量、内容加载、资源缓存或 Gallery 解锁行为。
- 不在本 feature 中统一 `ISaveService` 重复 API；先保持二进制表面等价，语义收敛另做可测试切片。
- 不迁移 Presentation/Builtins 命名空间，不重组 Editor API，不处理音频契约。
- 不执行全仓库格式化或目录整理。

## 验收标准

- solution 与仓库中不再存在 `GalNet.Storage.Abstractions` 项目引用。
- Runtime 只直接引用 Core 与 Presentation.Abstractions；Assets 和 Avalonia.Rendering 只直接引用 Core。
- 14 个公开类型均位于 ADR-0001 指定程序集和命名空间，仓库内消费者全部编译。
- 架构测试不再包含 Storage 迁移例外，全部测试通过。
- 存档、变量、内容、资源、Gallery、Editor Headless 与 Sample Headless 验证通过。

## 约束

- 用户已接受 ADR-0001，不保留旧命名空间兼容层或 type forwarding。
- 迁移只改变类型归属和引用，不顺手调整方法签名或实现逻辑。
- 保护并行 feature 的文件；若消费者文件发生并行变化，以最新内容做定点 using 更新。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [实现总结](summary.md)
- [ADR-0001](../../docs/adr/0001-runtime-storage-contract-ownership.md)
- [Phase 1 设计](../F-20260929-05-contract-ownership/design.md)
- [维护性路线图](../F-20260929-03-maintainability-roadmap/phase-plan.md)
