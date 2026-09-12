# 资源文件管理

参考 Unity，每个资源文件都有对应描述文件（同名 `.meta` 文件），包含元数据及唯一 ID（GUID）。

## 核心概念

### 三种包格式

| 格式 | 说明 |
|---|---|
| `.galpak` | 游戏分发 ZIP；当前含 JSON manifest、`Assets/content.pak` 与 `Assets/assets.pak` |
| `.galnet` | 当前 `.galpak` 内 manifest 的文件名，不是独立逻辑二进制 |
| `.pak` | 资源归档文件，内部包含寻址表和资源数据块 |

当前导出器将 `Assets/**` 打为 `Assets/assets.pak`，并将 `settings.json`、`Graph/**`、`I18n/**` 打为 `Assets/content.pak`；完整发布布局见[文件格式](file-formats.md)。

## 资源描述文件

每个资源配同名 `.meta` 文件（JSON），示例：

```json
{
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "type": "sprite",
    "path": "characters/alice.png",
    "filter": "bilinear",
    "compress": "brotli"
}
```

字段说明：
- **id**：GUID，全局唯一，IGameView 中作为 `assetId` 传递
- **type**：资源类型（`sprite` / `audio` / `video` / `font` / `unknown`）
- **path**：相对于 Assets 目录的路径
- **filter**：滤波模式（`point` / `bilinear` / `trilinear`）
- **compress**：压缩格式（`none` / `deflate` / `gzip` / `brotli`）

## 架构分层

所有层均为接口化设计，方便替换实现。

```
IGameFile          资源文件 + 元数据（包含 ID / 路径 / 类型 / 压缩 / Hash）
    ↓
IArchive          一组可寻址文件，支持 id 和路径加载
                   结构：寻址表 | 资源数据1(元数据+内容)、资源数据2、...
                   （实际打包为 .pak 文件）
    ↓
IAssetProvider    负责提供 Archive 实例
                   目前实现：
                     - LocalFileProvider（开发模式：读原始文件 + .meta）
                     - PakFileProvider（打包模式：从 .pak 解析读取）
                   未来可扩展 HTTP 等
    ↓
IAssetManager      资源管理器：加载/缓存（引用计数）/释放
                    支持两种查找方式（共享缓存）：
                      按 ID 加载：  LoadAsync<T>(assetId)
                      按路径加载：  LoadByPathAsync<T>(path)
```

### 接口定义（GalNet.Core/Assets）

```csharp
interface IAssetManager : IDisposable
{
    int CachedCount { get; }

    void RegisterProvider(IAssetProvider provider);
    Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class;
    Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class;
    void Release(string assetId);
    bool IsLoaded(string assetId);
    void ClearCache();
}

interface IAssetProvider
{
    string Name { get; }
    bool Exists(string name);
    IArchive OpenArchive(string archiveName);
    Task<IArchive> OpenArchiveAsync(string name, CancellationToken ct = default);
}

interface IArchive : IDisposable
{
    string Name { get; }
    IEnumerable<string> AssetIds { get; }
    bool Contains(string assetId);
    IGameFile? GetAsset(string assetId);
    IGameFile? GetAssetByPath(string path);
}

interface IGameFile
{
    string Id { get; }
    string Path { get; }
    ResourceType Type { get; }
    long Length { get; }
    string? Hash { get; }
    Stream OpenRead();
    byte[] ReadAllBytes();
    Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default);
}
```

`GalNet.Assets` 项目负责所有实现。

### 双加载模式

AssetManager 通过注册不同 Provider 切换数据源：

- **开发模式**：注册 `LocalFileProvider`，直接从 Assets 目录读取原始资源 + .meta 描述文件
- **打包模式**：注册 `PakFileProvider`，从 .pak 文件解析寻址表，按 ID 加载资源数据块
- **混合使用**：两者可同时注册，优先遍历先注册的 Provider

## 实现层（GalNet.Assets）

### .pak 二进制格式

```
┌─────────────────────────────────────┐
│ Magic: "GPAK" (4 bytes)            │
│ Version: int32                      │
│ EntryCount: int32                   │
├─────────────────────────────────────┤
│ Entry Table (EntryCount 项)         │
│ ┌─────────────────────────────────┐ │
│ │ IdLen     (int32)              │ │
│ │ Id        (UTF-8, IdLen bytes) │ │
│ │ PathLen   (int32)              │ │
│ │ Path      (UTF-8, PathLen)     │ │
│ │ Type      (int32)              │ │
│ │ Offset    (int64, 数据段偏移)   │ │
│ │ OrigLength(int64, 未压缩大小)   │ │
│ │ StoredLen (int64, 存储大小)     │ │
│ │ Compress  (int32)              │ │
│ │ Hash      (32 bytes, SHA256)   │ │
│ └─────────────────────────────────┘ │
├─────────────────────────────────────┤
│ Data Section                        │
│ 压缩或原始数据块，按 Offset 定位    │
└─────────────────────────────────────┘
```

### 压缩（CompressionHelper）

| 模式 | 说明 |
|---|---|
| `None` | 不压缩，直接存储 |
| `Deflate` | System.IO.Compression.DeflateStream |
| `GZip` | System.IO.Compression.GZipStream |
| `Brotli` | System.IO.Compression.BrotliStream（.NET 内置，默认） |

提供同步/异步压缩解压，流式 API 和字节数组 API。

### 加密

当前 `GamePackageExporter` 不对 `.galpak` 或其内部 `.pak` 加密；它使用 SHA-256 校验导出包中的两个 pak。任何 `CryptoHelper` 能力均不构成当前发布格式的兼容性承诺。

### 资源缓存与引用计数

AssetManager 内部使用 `Dictionary<string, CacheEntry>`，每个 CacheEntry 包含：
- **Data**：已转换类型的资源对象
- **RawData**：原始字节
- **GameFile**：元数据引用
- **RefCount**：引用计数

加载时 RefCount++，释放时 RefCount--，归零时从缓存移除。

### PakBuilder

提供将文件列表打包为 .pak 的工具：
- 自动读取文件内容
- 可选压缩（指定压缩模式）
- 自动计算 SHA256 Hash
- 支持从元数据字典设置压缩模式

### 目录结构

```
GalNet.Core/Assets/        ← 接口定义
├── ResourceType.cs
├── CompressionMode.cs
├── AssetMeta.cs
├── IGameFile.cs
├── IArchive.cs
├── IAssetProvider.cs
└── IAssetManager.cs

GalNet.Assets/             ← 实现
├── GameFile.cs
├── Archive.cs
├── LocalFileProvider.cs
├── PakFileProvider.cs
├── PakBuilder.cs
├── AssetManager.cs
├── CompressionHelper.cs
└── CryptoHelper.cs
```

## Scene 场景状态

`SceneState` 是 `GameRuntime` 拥有的可序列化场景快照；`SceneInstanceManager` 以它为基础维护活跃实例。`IGameView` 只呈现 Handler 发出的请求，不是场景状态的唯一来源。

```
SceneState (可序列化的状态数据)
├── Layers (图层列表：图片资源、位置等)
├── ActiveControlIds (活跃控件 ID 列表)
└── ActiveEffectIds (活跃特效 ID 列表)
```

SceneState 持有场景运行中需要持久化的数据字段（图层信息、控件/特效状态等），通过 `SaveManager` 序列化为存档。

游戏运行时，Handler 先更新 Runtime 的场景实例/状态，再向 `IGameView` 发送呈现请求。无头测试使用 `NullGameView`。

### 存档数据结构

SaveManager 统一调度存档：

```
GameSnapshot
├── NodeId
├── EntryIndex
├── Variables (VariableStore 快照)
└── SceneState
```
