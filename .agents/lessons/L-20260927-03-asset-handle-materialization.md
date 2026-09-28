# 平台媒体路径必须从资源句柄实体化

状态：confirmed
来源 Feature：F-20260926-01-gallery-meta-pages

## 现象

Gallery 和 Editor Preview 曾各自扫描 `Assets` 或按项目路径直接读取媒体/贴图。开发目录可用，但安装后的 PAK、资源覆盖和 `AssetHandle<T>` 引用计数会被绕过。

## 根因

把“平台 API 需要文件路径”误当成了“页面需要理解资源目录”。这混合了 provider 的位置查询、AssetManager 的解码/缓存和 UI 的媒体适配职责。

## 如何发现

对资源读取调用点进行搜索时，发现 Gallery resolver 枚举 `.meta`，Editor Preview layer factory 使用 `SceneTexture.FromFile`。它们没有经过 `IAssetManager.AcquireAsync`。

## 正确做法

Provider 只返回可读取的源描述；页面或媒体适配层先通过 `IAssetManager` 按 GUID acquire。需要 OS 路径时，由窄 resolver 将 handle 的 bytes 写入会话专属临时目录，并在 resolver Dispose 时删除；可直接解码的资源应保留 handle 到会话结束。

## 适用范围

所有支持目录、PAK 或未来补丁 PAK 的 Avalonia/媒体/渲染宿主。

## 不要做什么

不要让 UI 枚举 `.meta`、拼项目路径或直接从 PAK/目录读取资源；不要把 `IGameFile` 当作引用计数 handle。

## 证据和关联文档

- `src/GalNet.Avalonia.GameView/Services/AssetGalleryResourceResolver.cs`
- `src/GalNet.Editor/Services/EditorPreviewHost.cs`
- `test/GeneralTest/Presentation/AssetGalleryResourceResolverTests.cs`
- `docs/spec/assets.md`
