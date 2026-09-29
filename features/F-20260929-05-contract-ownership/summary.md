---
feature: F-20260929-05-contract-ownership
status: done
updated: 2026-09-29
---

# 契约归属 ADR 与依赖规则：实现总结

## 结果

维护性路线图 Phase 1 已完成。用户接受方案 C，运行时/存储契约的长期归属已通过 ADR-0001 固化；当前项目引用边界由首批自动化架构测试保护。此 feature 未移动生产类型或修改运行时行为。

## 关键决定

- 资源访问协议进入 `GalNet.Core.Assets`；Assets 与 Storage.FileSystem 保持为实现层。
- 内容、保存和变量端口由直接消费者 `GalNet.Runtime` 拥有，不新增 `Runtime.Abstractions`。
- `GalleryDataSource` 进入 Runtime，Gallery 领域接口、catalog 与 DTO 留在 Core。
- 仓库内消费者迁移完成后删除 `GalNet.Storage.Abstractions`。
- `IGameProgressService`、`ITextResolver`、`IGameSession` 后续归入 Runtime；无消费者的旧接口删除；宿主/Editor 接口移出 Core。音频接口由独立音频 feature 决定。

## 架构测试

新增 `ProjectDependencyTests`，使用 BCL `XDocument` 读取生产 `.csproj`：

- 12 个内层或共享项目只能引用各自 allowlist 中的项目，Core 因此保持零项目引用。
- `Storage.Abstractions` 只能由当前六个已记录迁移项目直接引用，防止例外继续扩散。
- 测试不冻结 Editor、Sample、Launcher 等外层组合根的完整引用图。

## 验证

- 定向 `ProjectDependencyTests`：13/13 通过。
- `GeneralTest` Release 全量：271/271 通过。
- 新增测试不产生编译器或分析器告警；构建中显示的其他告警均为既有项目告警。
- `git diff --check` 在收尾检查中执行。

## 正式文档

- [ADR-0001：运行时与存储契约归属](../../docs/adr/0001-runtime-storage-contract-ownership.md)
- [当前架构](../../docs/spec/architecture.md) 已记录 Runtime 对 Storage.Abstractions 的迁移期例外和架构测试规则。

## 后续工作

- 路线图 Phase 2a 按 ADR 迁移 Core.Assets 与 Runtime ports，更新消费者并删除 `GalNet.Storage.Abstractions`。
- Phase 2a 完成时删除架构测试中的 Storage 迁移例外，要求 Runtime、Assets、Rendering 与 Editor.Shared 使用目标依赖矩阵。
- Phase 2b 独立迁移 Presentation 与 Builtins 命名空间，避免与存储/运行时公共 API 迁移混合。
