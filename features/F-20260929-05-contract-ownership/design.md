---
feature: F-20260929-05-contract-ownership
updated: 2026-09-29
---

# 契约归属与项目依赖设计

## 当前事实

`GalNet.Storage.Abstractions` 只引用 `GalNet.Core`，但其中 14 个公开类型分属三个领域，并全部声明在 `GalNet.Core.*` 命名空间。当前有 Assets、Runtime、Storage.FileSystem、Avalonia.Rendering、Editor.Shared 和 Editor 六个生产项目直接引用该程序集。

这使程序集名、命名空间和实际消费者表达三套不同边界；`GalNet.Runtime` 也因此违反当前架构文档中“只依赖 Core 与 Presentation.Abstractions”的描述。

## 公开契约清单与目标归属

| 当前类型 | 当前命名空间 | 主要消费者 | 目标程序集 / 命名空间 |
| --- | --- | --- | --- |
| `AssetHandle<T>` | `GalNet.Core.Assets` | Assets、Editor、Sample | `GalNet.Core` / 保持命名空间 |
| `IArchive` | `GalNet.Core.Assets` | Assets、Storage.FileSystem | `GalNet.Core` / 保持命名空间 |
| `IAssetManager` | `GalNet.Core.Assets` | Assets、Editor、Sample、GameView | `GalNet.Core` / 保持命名空间 |
| `IAssetDecoder<T>` | `GalNet.Core.Assets` | Rendering、Assets | `GalNet.Core` / 保持命名空间 |
| `IAssetProvider` | `GalNet.Core.Assets` | Assets、Storage.FileSystem、宿主 | `GalNet.Core` / 保持命名空间 |
| `IGameFile` | `GalNet.Core.Assets` | Assets、Rendering、Editor | `GalNet.Core` / 保持命名空间 |
| `IGameContentProvider` | `GalNet.Core.Services` | Storage.FileSystem、Editor、Sample | `GalNet.Runtime.Content` |
| `GameContent` | `GalNet.Core.Services` | Storage.FileSystem、Editor、Sample | `GalNet.Runtime.Content` |
| `ISaveService` | `GalNet.Core.Services` | Runtime host、Editor、Sample | `GalNet.Runtime.Persistence` |
| `SaveRequest` | `GalNet.Core.Services` | Runtime host、Editor、Sample | `GalNet.Runtime.Persistence` |
| `SaveSlotInfo` | `GalNet.Core.Services` | Runtime host、Editor、Sample | `GalNet.Runtime.Persistence` |
| `IPlayerVariableStore` | `GalNet.Core.Services` | Storage.FileSystem | `GalNet.Runtime.Persistence` |
| `IVariableService` | `GalNet.Core.Services` | Runtime、Editor、Storage.FileSystem | `GalNet.Runtime.Variables` |
| `GalleryDataSource` | `GalNet.Core.Gallery` | Editor、Sample | `GalNet.Runtime.Gallery` |

不新增 `GalNet.Runtime.Abstractions`。Runtime 是内容、保存和变量策略的直接消费者，将端口放入 Runtime 能保持依赖反转，也避免产生只有接口、仍需 Runtime/Core 类型的新横向程序集。

## Core 历史宿主契约

本 feature 只决定后续归属，不移动类型：

- `IGameProgressService`、`ITextResolver`：由 Runtime 直接消费，目标归入 Runtime ports。
- `IGameSession`：已有唯一实现 `DefaultGameSession` 位于 Runtime，目标归入 Runtime session API。
- `IGameDataProvider`、`IInputService`、`INavigationHost`：没有有效接口消费者，目标为删除；相关实现或同名概念在迁移时单独核实。
- `IGameExitService`、`ISettingsService`、`KeyGesture`：属于宿主或 Editor API，目标归入对应宿主/Editor 边界，不由 Core 承担。
- `IAudioService`：保留现状，由 `F-20260916-02-audio-system` 决定，不在本路线图中抢先迁移。

## 目标依赖矩阵

表格列出内层与共享生产项目的直接依赖上限；UI 宿主可以组合内层和平台实现，但内层不能反向引用宿主。

| 项目 | 允许的直接项目依赖 |
| --- | --- |
| Core | 无 |
| Presentation.Abstractions | Core |
| Runtime | Core、Presentation.Abstractions |
| Primitives.Builtins | Core、Presentation.Abstractions |
| Assets | Core |
| Storage.FileSystem | Core、Runtime、Assets |
| Editor.Abstraction | Core |
| Editor.Shared | Core、Editor.Abstraction、Primitives.Builtins、Runtime、Assets |
| Avalonia.Controls | Core |
| Avalonia.Rendering | Core |
| Presentation.Defaults | Presentation.Abstractions |

`GalNet.Storage.Abstractions` 是迁移期例外。Phase 2a 完成后，它及所有直接引用必须从 solution 和矩阵中消失。

## 命名空间规则

- 一个程序集不得声明另一个顶层产品程序集的根命名空间。迁移后仅 `GalNet.Core` 可声明 `GalNet.Core.*`，仅 `GalNet.Runtime` 可声明 `GalNet.Runtime.*`。
- 扩展程序集使用自身根命名空间；Presentation 与 Builtins 的现有错位由 Phase 2b 独立迁移。
- 具体文件系统、PAK 安装、目录扫描和文件保存实现保持在 `GalNet.Storage.FileSystem` 或 `GalNet.Assets`，不得进入 Runtime。
- Runtime 端口可以使用 Core 的领域 DTO，但不得引用具体存储或 UI 类型。

## 架构测试策略

首批测试使用 BCL 的 `XDocument` 读取 `src/**/*.csproj`，不引入第三方架构测试包。测试定位仓库根目录并按规范化项目名检查直接 `ProjectReference`：

1. Core 不得有任何项目引用。
2. 内层项目不得直接引用 `GalNet.Avalonia.*`、Editor、Sample、Launcher 或具体 Storage.FileSystem。
3. 当前稳定项目的直接依赖必须属于显式 allowlist；`Storage.Abstractions` 仅作为带说明的迁移例外存在。
4. 增加一个待 Phase 2a 收紧的测试入口，在迁移完成时删除例外并要求解决方案不存在该项目。

测试验证依赖方向，不扫描 C# 文本推断类型依赖；类型和命名空间迁移由 Phase 2a/2b 的编译与 API 清单验证。

## 迁移顺序

1. Phase 2a 先把资源契约移入 Core.Assets，再把 Runtime 内容、保存、变量和 Gallery 类型迁入 Runtime。
2. 更新 Assets、Storage.FileSystem、Editor、Sample、Rendering 和测试消费者，删除多余 ProjectReference。
3. 删除 `GalNet.Storage.Abstractions` 项目并收紧架构测试。
4. Phase 2b 独立迁移 Presentation 与 Builtins 命名空间，避免把两个公共 API 迁移混成一个 diff。
5. Core 历史宿主接口按消费者分批清理；音频契约继续由音频 feature 管理。

## 风险与控制

- **循环依赖：** Runtime 端口只能引用 Core DTO；Core 不引用 Runtime。`GalleryDataSource` 进入 Runtime，`IGalleryDataSource` 与 Gallery DTO 留在 Core。
- **API 破坏：** 当前没有已证明的外部兼容要求，后续迁移一次更新仓库内消费者，不预设 type forwarding。
- **测试过拟合当前图：** allowlist 只覆盖内层稳定边界；外层组合根不做全量冻结。
- **并行 feature 冲突：** Phase 2a 避开粒子、编译管线和音频正在修改的文件；发生交叉时按独立切片迁移。
