---
feature: F-20260926-01-gallery-meta-pages
reviewed: 2026-09-26
scope: implementation-and-design
result: blocked
---

# Gallery 元数据聚合与可扩展页面注册实现审核

## 结论

**blocked**。当前改动已完成新 Meta DTO、PAK v2、Gallery 编译和 Avalonia 页面注册的主要骨架，相关 Assets 与 Avalonia 项目能构建，Assets 测试也通过；但尚未达到 Phase 6 的退出条件，不能作为完成的 feature 提交。

## Findings

### [P1] 内容组合根没有接收共享 registry，插件类型无法贯通目录运行和导出

`DirectoryGameContentProvider` 与 `GamePackageExporter` 都在方法内部调用 `BuiltinResourceTypes.CreateCatalog()` 和 `BuiltinGalleryTypes.CreateCatalog(...)`。调用方无法把组合后的 `IResourceTypeCatalog` / `IGalleryTypeCatalog` 注入进来，因此已注册的插件 Meta DTO 或 Gallery type 会在目录运行、Preview 和导出时变成未知类型。该行为直接违背 design 的“宿主注入冻结 registry”和 Phase 6 的“所有组合根使用共享 registry”要求。

建议：将两个 composition root 改为接收同一组已冻结 registry，并为一个自定义资源类型和一个自定义 Gallery 页面走完目录加载与导出测试。

### [P1] 损坏或未知 `.meta` 被静默跳过，导致 Gallery 和导出结果不完整

`LocalFileProvider.ScanAsync` 对 `AssetMetaCodec.Deserialize` 的所有异常执行 `continue`。因此未知 type ID、无效 JSON 或无效 DTO 不会在 Preview、目录运行或导出中报告；资产会从 archive 消失，相关 Gallery annotation 也随之消失。设计要求三条路径使用一致、可诊断的错误，导出尤其不能把损坏输入变成成功但缺资源的包。

建议：保留 source `.meta` 路径并抛出带路径的 `InvalidDataException`（或汇总所有损坏 metadata 后失败），同时补充 invalid JSON、未知 type ID 和缺资源文件的测试。

### [P1] 发行包生成的 `gallery.json` 没有实际加载和 registry 快照校验路径

`GamePackageExporter.BuildContent` 会把生成的 `gallery.json` 写入 `content.pak`，`GalleryFileLoader.LoadGenerated` 也实现了快照校验，但仓库搜索不到该方法的调用方；`PakFileProvider` 只负责资产 PAK，目录内容提供者则重新扫描 `.meta`。所以发行运行尚未读取生成内容，也没有执行 required snapshot validation。

建议：提供包内容加载器，从 `content.pak` 读取 `gallery.json` 并调用 `GalleryFileLoader`（或抽取同等的 stream-based API）；添加 “Meta → assets.pak / generated gallery.json → 发行加载” 的端到端测试。

### [P1] 核心行为测试被删除而未等价替换

本次删除了 `ArchiveTests`、`AssetManagerTests`、`LocalFileProviderTests`、`PakBuilderTests`、`PakFileProviderTests`、`GalleryPresentationTests` 和 `GalleryContentLoadingTests`，新增的 `AssetPipelineTests` 只有三项测试。新测试没有覆盖 registry 的 Add/Replace/extension 冲突、无效 metadata、PAK header/边界、AssetManager 缓存/取消/释放、生成 Gallery snapshot、页面 Add/Replace/缺页诊断，以及 Phase 5 要求的零/单/多类型导航行为。

建议：以新 API 更新这些测试，而不是降低覆盖面；至少覆盖 phase plan 列出的边界和上述三个缺陷后再提交。

### [P1] 正式文档仍描述已删除的旧模型

`docs/spec/file-formats.md`、`architecture.md`、`entry-types.md` 与 `docs/glossary.md` 仍把根目录手写 `gallery.json`、字符串 Gallery ID、`ResourceTypeName` 和 renderer fallback 描述为当前事实。Phase 6 明确要求同步这些文档及 `assets.md`、`runtime.md`、`control.md`；当前 diff 未更新它们。feature 文档中的“当前正式文档”链接也仍指向已删除的 source 文件。

建议：在上述运行时行为真正完成并验证后，同步正式文档并将 Phase 6 状态/验证记录回写为实际结果。

## 已验证内容

- `dotnet build src/GalNet.Assets/GalNet.Assets.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v:q` 通过。
- `dotnet build src/GalNet.Core/GalNet.Core.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v:q` 通过。
- `dotnet build src/GalNet.Primitives.Builtins/GalNet.Primitives.Builtins.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v:q` 通过。
- `dotnet build src/GalNet.Storage.FileSystem/GalNet.Storage.FileSystem.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v:q` 通过。
- `dotnet build src/GalNet.Editor.Shared/GalNet.Editor.Shared.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v:q` 通过。
- `dotnet build src/GalNet.Avalonia.GameView/GalNet.Avalonia.GameView.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:AVALONIA_TELEMETRY_OPTOUT=1 -v:q` 通过（仅既有/非阻断 warning）。
- `dotnet test test/GalNet.Assets.Tests/GalNet.Assets.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false`：28/28 通过。
- `git diff --check` 通过；只报告仓库现有的 CRLF 工作区提示。
- `GeneralTest` 为 228/229：`EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 因 JSON 行尾/缩进差异失败，与本 feature 无关，但仍意味着不能将其报告为全绿。

## 审核范围

审阅了 feature、design、phase plan、当前实现 diff、测试替换情况，以及目录、导出和 PAK 的运行时调用关系。本次审核未修改业务代码。
