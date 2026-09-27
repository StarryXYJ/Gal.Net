---
feature: F-20260926-01-gallery-meta-pages
reviewed: 2026-09-27
scope: package-installation-and-resource-loading
result: pass-with-follow-up
---

# Gallery 元数据、资源包与加载闭环审核

## 结论

**pass-with-follow-up**。本轮的资源加载边界与发行包输入路径已经闭合：Provider 只定位目录或 PAK 中的源文件，`AssetManager` 负责 decoder、缓存和 `AssetHandle<T>` 引用计数；开发项目与安装内容分别走各自的 content provider。Gallery 媒体和 Editor Preview 场景贴图也已改为先通过 `AssetManager` 获取资源，未再按项目路径直接读取发行资源。

## 已核对的行为

- `.galpak` 是分发 ZIP；安装器先验证 manifest 的安全路径、大小与 SHA-256，再解压为同级安装目录。运行时不从 ZIP 直接读取。
- 开发项目从 `Assets/` 的 `.meta` 建立目录 Provider；安装内容仅从 `Assets/Paks/**/*.pak` 建立 PAK Provider，按相对路径倒序提供覆盖优先级。`Graph/`、`I18n/`、`settings.json`、`gallery.json` 与 manifest 是内容 loader 的特殊文件，不是资源 Provider 的输入。
- 资源查询和解码以 GUID/type ID 为键。`IGameFile` 是可重读的源描述，不承担引用计数；每一次成功 `AcquireAsync<T>` 都返回独立 `AssetHandle<T>`，由 handle 的幂等 `Dispose` 释放该次引用。
- `AssetGalleryResourceResolver` 仅在平台媒体 API 必须取得 OS 路径时，将经 manager 获取的 bytes 临时实体化；临时目录由 resolver 会话释放。Editor 场景贴图则直接保留 manager 返回的 `AssetHandle<SceneTexture>`，会话结束时统一释放。
- 损坏、未知类型或缺失源文件的 `.meta` 会产生带 metadata 路径的错误，不再静默遗漏资源。

## 验证证据

- `dotnet test test/GalNet.Assets.Tests/GalNet.Assets.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -v:q`：34/34 通过；包括工程 → 导出 → 安装 → 内容 → 资源查询与补丁 PAK 覆盖。
- `dotnet test test/GeneralTest/GeneralTest.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:AVALONIA_TELEMETRY_OPTOUT=1 --filter FullyQualifiedName~AssetGalleryResourceResolverTests -v:q`：1/1 通过。
- `GalNet.Sample.Headless`、`GalNet.Editor.Shared`、`GalNet.Sample.Avalonia` 与 `GalNet.Editor` 已在此实现上构建通过。
- `git diff --check` 通过。

## 后续项（不阻塞当前资源包/加载阶段）

1. Phase 6 尚未将已验证的格式和运行时行为同步到正式 `docs/`；closeout 时应处理，不能把旧文档当作当前格式说明。
2. 未来的剧情补丁可把 `.galgroup` 也纳入 GUID 资源体系；当前 Graph/Group 仍是特殊内容文件，缺失 group 的语义按本 feature 的延后决定处理。
3. `GeneralTest` 全量仍有一项既有失败：`EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 的 JSON 换行/缩进快照差异。它不涉及本 feature；本轮没有改动该路径。
