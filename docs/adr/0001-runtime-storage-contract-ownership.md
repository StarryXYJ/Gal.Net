# ADR-0001：运行时与存储契约归属

- 状态：Accepted
- 日期：2026-09-29
- 决策者：项目维护者
- 关联 feature：`F-20260929-05-contract-ownership`

## 上下文

`GalNet.Storage.Abstractions` 同时定义资源访问、内容加载、存档、变量和 Gallery 组合类型。它只引用 Core，但公开类型位于 `GalNet.Core.Assets`、`GalNet.Core.Services` 和 `GalNet.Core.Gallery` 命名空间；Runtime、Assets、Storage.FileSystem、Rendering 与 Editor 均直接引用它。

该结构造成三个问题：程序集名称不能说明职责；Runtime 的真实依赖与架构文档不一致；新增契约容易继续堆入一个横向的 Abstractions 项目。

## 候选方案

### A. 保留 Storage.Abstractions

迁移最少，但资源、运行时端口和 Gallery 继续共处，职责与命名错位不会消失。

### B. 全部并入 Core

依赖图最简单，但会让 Core 持有存档、宿主和运行时编排协议，持续扩大通用协议仓库。

### C. 契约由策略消费者拥有，并删除 Storage.Abstractions

资源契约归 Core.Assets；内容、存档和变量端口归 Runtime；具体文件系统、PAK 和资源管理实现留在外层项目。Gallery 的领域接口与 DTO 留在 Core，依赖运行时变量端口的组合实现进入 Runtime。

## 决定

采用方案 C。用户于 2026-09-29 明确接受该方案。

- `IAssetProvider`、`IArchive`、`IGameFile`、`IAssetManager`、`IAssetDecoder<T>`、`AssetHandle<T>` 移入 `GalNet.Core`，保持 `GalNet.Core.Assets` 命名空间。
- `IGameContentProvider` 与 `GameContent` 移入 `GalNet.Runtime.Content`。
- `ISaveService`、`SaveRequest`、`SaveSlotInfo`、`IPlayerVariableStore` 移入 `GalNet.Runtime.Persistence`。
- `IVariableService` 移入 `GalNet.Runtime.Variables`。
- `GalleryDataSource` 移入 `GalNet.Runtime.Gallery`；`IGalleryDataSource`、Gallery catalog 与 DTO 保持在 Core。
- 不创建 `GalNet.Runtime.Abstractions`；Runtime 自己拥有其策略端口。
- 完成仓库内消费者迁移后删除 `GalNet.Storage.Abstractions`。

## 后果

正面影响：

- 程序集、命名空间和策略消费者表达一致的职责。
- Runtime 恢复为只依赖 Core 与 Presentation.Abstractions。
- Assets 与 Storage.FileSystem 成为实现层，不再拥有 Runtime 使用的端口。
- 新持久化能力有明确入口，不再默认加入通用 Storage 抽象程序集。

代价与风险：

- 后续迁移会修改公开命名空间，并需要一次更新所有仓库内消费者。
- Core.Assets 会包含资源访问协议，但不包含目录、PAK、缓存实现或平台解码资源。
- 未证明外部消费者兼容需求，因此本次决定不要求 type forwarding；若迁移前发现真实外部消费者，应另行决定过渡版本。

## 边界

- 本 ADR 不决定音频接口；`IAudioService` 由音频 feature 管理。
- 本 ADR 不迁移 Presentation/Builtins 命名空间，也不决定 Editor 插件 API。
- 本 ADR 描述目标结构；迁移完成前，`GalNet.Storage.Abstractions` 仍是当前实现的一部分。
