---
feature: F-20260926-01-gallery-meta-pages
status: implementation
updated: 2026-09-27
---

# Gallery 元数据聚合与可扩展页面注册：实现总结

## 结果

Feature 的 Gallery、资源 metadata、PAK、发行安装、Preview 与 Avalonia 页面注册主路径已经实现。旧 `ResourceType` enum、集中式 Gallery authoring、renderer fallback 和旧资源所有权 API 已删除；不提供旧项目或旧包兼容读取。

## 关键决定

- 资源 `.meta.gallery[]` 是 Gallery 唯一作者真源。由冻结 resource/Gallery catalog 编译为版本 2 的运行时 `gallery.json`；发行运行会校验其类型快照。
- `IAssetProvider`/`IArchive`/`IGameFile` 仅定位和读取来源。`AssetManager` 以 `(GUID, CLR 类型)` 管理 decoder、缓存与引用计数；调用者以独立 `AssetHandle<T>` 获取和释放资源。
- `.galpak` 是经过 manifest 校验后解压的 ZIP 运输容器。安装运行只扫描 `Assets/Paks/**/*.pak`，按相对路径倒序覆盖；Graph、I18n、settings、Gallery JSON 和 manifest 是特殊内容。
- 页面按精确 Gallery `typeId` 注册，CG、视频和音频维持独立页面；未注册类型进入诊断页，不按资源类型回退。
- Editor、Sample、Headless 和 exporter 的每个组合根只创建一对冻结 catalog，并把同一实例传给内容加载、资源 provider、Preview 与导出，避免插件类型在路径间丢失。

## 主要模块

- `GalNet.Core`：类型化 `AssetMeta`、资源/Gallery registry、Gallery compiler、数字解锁 ID。
- `GalNet.Assets`：目录与 PAK provider、PAK v2 metadata、`AssetManager` 和 `AssetHandle<T>`。
- `GalNet.Storage.FileSystem`：项目/安装内容 provider、`.galpak` 安装器与 PAK provider 组合。
- `GalNet.Editor.Shared` / `GalNet.Editor`：导出、Preview 和统一 catalog composition。
- `GalNet.Avalonia.GameView`：Gallery 页面 registry，以及从 manager acquire 后临时实体化媒体的 resolver。

## 验证

- `dotnet test test/GalNet.Assets.Tests/GalNet.Assets.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v:q`：34/34 通过。
- `dotnet build src/GalNet.Editor/GalNet.Editor.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:AVALONIA_TELEMETRY_OPTOUT=1 -v:q`：通过。
- `dotnet build src/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:AVALONIA_TELEMETRY_OPTOUT=1 -v:q`：通过。
- Headless Sample、Editor Headless 与 Gallery resolver 定向测试通过。

## 当前限制与后续工作

- `GeneralTest` 全量仍有一项既有失败：`EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 的 JSON 换行/缩进快照差异。本 feature 未修改该路径，因此 feature 维持 `implementation`，不宣称全量验证完成。
- `.galgroup` 仍是 Graph 特殊内容，不以 GUID 资源查询。将 Group 资源化、定义缺失语义和增量剧情补丁是后续独立 feature。

## 同步的正式文档

- [资源](../../docs/spec/assets.md)
- [项目与发布格式](../../docs/spec/file-formats.md)
- [架构](../../docs/spec/architecture.md)
- [Avalonia 页面](../../docs/spec/control.md)
- [条目类型](../../docs/spec/entry-types.md)
- [术语表](../../docs/glossary.md)
