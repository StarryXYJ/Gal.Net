---
id: F-20260929-08-presentation-builtins-namespaces
title: Presentation 与 Builtins 命名空间归位
type: refactor
status: done
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# Presentation 与 Builtins 命名空间归位

## 目标

完成维护性路线图 Phase 2b，使公开类型的命名空间反映其程序集所有权，消除扩展程序集在 `GalNet.Core.*` 下声明类型造成的职责混淆。

## 范围

- 将 Presentation view 契约从 `GalNet.Core.View` 迁入 `GalNet.Presentation.Abstractions.View`。
- 将 Builtins 定义的 entry 类型从 `GalNet.Core.Entry` 迁入 `GalNet.Primitives.Builtins`。
- 更新生产代码、测试、示例和正式架构文档中的引用。
- 增加源代码级命名空间所有权测试，防止扩展程序集再次声明 Core 命名空间。

## 非目标

- 不移动 Core 自己拥有的 entry 基类、schema 和 catalog。
- 不改变 entry `TypeId`、作者 JSON、编译产物或存档格式。
- 不调整程序集引用方向、物理项目目录或展示行为。
- 不提供旧命名空间兼容类型或 type forwarding；仓库没有已证明的外部发布兼容要求。

## 验收标准

- Presentation.Abstractions 中所有源码命名空间均以 `GalNet.Presentation.Abstractions` 开头。
- Primitives.Builtins 中所有源码命名空间均以 `GalNet.Primitives.Builtins` 开头。
- 全仓库生产代码和测试不再引用 `GalNet.Core.View`，Builtins 项目不再声明 `GalNet.Core.Entry`。
- entry catalog、primitive、runtime、presentation 测试及 Headless、完整 solution 构建通过。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [实现总结](summary.md)
- [路线图 Phase 2b](../F-20260929-03-maintainability-roadmap/phase-plan.md)
