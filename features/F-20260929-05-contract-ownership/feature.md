---
id: F-20260929-05-contract-ownership
title: 契约归属 ADR 与依赖规则
type: architecture
status: done
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# 契约归属 ADR 与依赖规则

## 原始目标

实施维护性路线图 Phase 1。用户已选择方案 C：契约由策略消费者拥有，并以删除 `GalNet.Storage.Abstractions` 为目标；在生产契约迁移前，把该决定固化为 ADR、明确目标依赖矩阵并建立可自动验证的架构规则。

## 范围

- 盘点 `GalNet.Storage.Abstractions` 的公开类型、真实消费者和项目引用。
- 明确 Runtime host/persistence ports、资源契约、Gallery 契约及 UI 宿主契约的目标归属。
- 创建并接受“运行时/存储端口归属”ADR，记录方案 A/B/C 与选择方案 C 的原因和后果。
- 定义生产项目允许的引用方向和根命名空间规则。
- 增加首批架构测试，锁定已确认且当前可以执行的依赖规则；对迁移前必然存在的例外显式记录，不制造天然失败测试。

## 非目标

- 本 feature 不移动公开类型、不删除 `GalNet.Storage.Abstractions`，不修改其仓库内消费者。
- 不迁移 Presentation/Builtins 命名空间，不拆测试项目或大型协调类。
- 不决定音频系统契约，不修改进行中的粒子或编译内容管线实现。
- 不把尚未完成的目标结构写成 `docs/spec/` 当前事实。

## 验收标准

- ADR 状态反映用户已经接受方案 C，并包含上下文、候选方案、决定、后果和迁移边界。
- 每个 `Storage.Abstractions` 公开契约都有当前消费者和目标归属记录。
- 允许引用矩阵能够区分当前例外与目标状态，并可作为后续迁移的验收依据。
- 至少一组架构测试自动检查当前已经成立的关键依赖边界。
- 路线图 Phase 1 有可追溯的完成证据，Phase 2a/2b 能据此拆分迁移。

## 约束与假设

- Core、Runtime 与 Editor.Shared 继续不依赖 Avalonia、Skia、LibVLC 或具体文件系统实现。
- 资源读取与缓存契约保留在资源领域；文件系统与 PAK 类型属于外层实现。
- 项目仍处快速迭代期，仓库内公共 API 可以在后续迁移 feature 中一次性调整；未证明需要为外部消费者保留类型转发。
- 用户于 2026-09-29 明确选择方案 C，可将 ADR 标记为 Accepted。

## 开放问题

- `IGameContentProvider` 等端口应直接进入 `GalNet.Runtime`，还是建立单独 Runtime abstractions 程序集；优先根据实际依赖图避免新增程序集。
- 资源接口中哪些已经由 `GalNet.Core.Assets` 所有，哪些仍需从 Storage 迁出。
- 首批架构测试采用现有测试技术还是轻量项目文件解析，以避免增加只用于测试的依赖。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [实现总结](summary.md)
- [ADR-0001：运行时与存储契约归属](../../docs/adr/0001-runtime-storage-contract-ownership.md)
- [维护性路线图](../F-20260929-03-maintainability-roadmap/feature.md)
- [路线图设计](../F-20260929-03-maintainability-roadmap/design.md)
- [当前架构](../../docs/spec/architecture.md)
