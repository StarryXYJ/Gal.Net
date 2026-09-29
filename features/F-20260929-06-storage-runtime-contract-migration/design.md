---
feature: F-20260929-06-storage-runtime-contract-migration
updated: 2026-09-29
---

# 存储与运行时契约迁移设计

## 迁移映射

- `Assets/*.cs` 原样移动到 `GalNet.Core/Assets/`；命名空间不变，因此多数消费者只需移除项目引用。
- `IGameContentProvider` 与 `GameContent` 移到 `GalNet.Runtime.Content`。
- `ISaveService`、`SaveRequest`、`SaveSlotInfo` 与 `IPlayerVariableStore` 移到 `GalNet.Runtime.Persistence`。
- `IVariableService` 移到 `GalNet.Runtime.Variables`。
- `GalleryDataSource` 移到 `GalNet.Runtime.Gallery`，并引用 Runtime.Variables。

类型实现和签名保持不变。新目录与命名空间表达端口用途，不引入额外 Abstractions 项目。

## 项目引用变化

- Core 吸收资源协议，仍保持零项目引用。
- Runtime 删除 Storage.Abstractions 引用，继续只引用 Core 与 Presentation.Abstractions。
- Assets、Avalonia.Rendering 删除 Storage.Abstractions 引用，只保留 Core。
- Editor.Shared 删除 Storage.Abstractions 引用；Storage.FileSystem 通过 Runtime 获得 host/persistence ports。
- Editor 删除 Storage.Abstractions 引用；外层 Editor、Sample 直接引用 Runtime 已符合组合根职责。
- solution 删除 Storage.Abstractions 项目。

## 验证策略

先运行架构测试和编译，让编译器定位 namespace 遗漏；随后运行 GeneralTest、Assets Tests、Editor Headless 与 Sample Headless。最终搜索旧项目名和旧 `GalNet.Core.Services` 端口引用，并收紧 allowlist。

## 风险

- 多个文件同时使用 Core 中仍合法的其他 Services 类型，不能机械删除整个 using；按编译错误和类型用途定点增加 Runtime using。
- lock file 会因项目依赖删除而变化，只接受 restore 生成的直接变化。
- `GalleryDataSource` 是实现而非纯 DTO，进入 Runtime 后外层宿主必须已有 Runtime 引用；当前 Editor 与 Sample 均满足。
