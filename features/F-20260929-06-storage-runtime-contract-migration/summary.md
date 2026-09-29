---
feature: F-20260929-06-storage-runtime-contract-migration
status: done
updated: 2026-09-29
---

# 存储与运行时契约迁移：实现总结

## 结果

ADR-0001 的核心项目迁移已完成。`GalNet.Storage.Abstractions` 已从源码、solution、项目引用和锁文件中删除；资源协议由 Core.Assets 拥有，内容、保存、变量与 Gallery 组合端口由 Runtime 拥有。运行时行为和接口成员未改变。

## 类型归属

- Core.Assets：`AssetHandle<T>`、`IArchive`、`IAssetManager`、`IAssetDecoder<T>`、`IAssetProvider`、`IGameFile`。
- Runtime.Content：`IGameContentProvider`、`GameContent`。
- Runtime.Persistence：`ISaveService`、`SaveRequest`、`SaveSlotInfo`、`IPlayerVariableStore`。
- Runtime.Variables：`IVariableService`。
- Runtime.Gallery：`GalleryDataSource`。

## 项目图

- Runtime 现在只直接引用 Core 与 Presentation.Abstractions。
- Assets 与 Avalonia.Rendering 现在只直接引用 Core。
- Storage.FileSystem 直接引用 Core、Runtime 与 Assets。
- Editor.Shared 不再引用已删除的横向 Storage 抽象项目。
- 架构测试要求旧项目及其 ProjectReference 保持不存在。

## 验证

- `dotnet restore GalNet.slnx --locked-mode`：成功。
- GeneralTest Release：270/270 通过。
- GalNet.Assets.Tests Release：34/34 通过。
- Editor Headless Release：成功，0 错误。
- Sample Headless Release：成功，0 错误。
- 完整 `GalNet.slnx` Release 构建：非沙箱环境成功，覆盖 Android、Browser、Desktop 与 iOS；0 错误，1 条既有 WebAssembly native reference 警告。
- 沙箱内完整构建仍会因 Android/Browser x64 MSBuild task host 通信失败中止；相同命令在非沙箱环境通过。

## 未包含事项

- `ISaveService` 保留迁移前后的重复重载，本切片未改变 API 语义。
- Core 中 `IGameProgressService`、`ITextResolver`、`IGameSession` 等历史宿主契约尚未迁移；无消费者接口也尚未删除。
- 上述事项继续属于路线图 Phase 2a，应以独立 characterization-test 切片完成。
