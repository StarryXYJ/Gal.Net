# 资源文件管理

参考 Unity，每个资源文件都有对应描述文件（同名 `.meta` 文件），包含元数据及唯一 ID（GUID）。

## 核心概念

### 三种包格式

| 格式 | 说明 |
|---|---|
| `.galpak` | **游戏分发包**，根目录包含一个 `.galnet` + `Assets/` 目录下多个 `.pak` |
| `.galnet` | **编译后的游戏逻辑单文件**（打包了 graph.json + 所有 .galgroup，含二进制化/加密/压缩） |
| `.pak` | **资源归档文件**，内部包含寻址表和资源数据块 |

当前先实现单 pak（`Assets/` 下所有资源打一个包），后续可按组/使用频率拆分。

## 资源描述文件

每个资源配同名 `.meta` 文件（JSON），示例：

```json
{
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "type": "sprite",
    "path": "characters/alice.png",
    "filter": "bilinear",
    "compress": "lz4"
}
```

字段说明：
- **id**：GUID，全局唯一，IGameView 中作为 `assetId` 传递
- **type**：资源类型（`sprite` / `audio` / `video` / `font` / `unknown`）
- **path**：相对于 Assets 目录的路径
- **filter**：滤波模式（`point` / `bilinear` / `trilinear`）
- **compress**：压缩格式（`none` / `lz4` / `zstd`）

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
interface IAssetManager
{
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
    CompressionMode Compression { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
    long Length { get; }
    string? Hash { get; }
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
│ │ Id (GUID string, 36 bytes)     │ │
│ │ Path (length-prefixed string)  │ │
│ │ Type (int32)                   │ │
│ │ Offset (int64, 文件内偏移)      │ │
│ │ OriginalLength (int64)         │ │
│ │ StoredLength (int64)           │ │
│ │ Compression (int32)            │ │
│ │ Hash (SHA256 hex, 64 bytes)    │ │
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
| `Brotli` | System.IO.Compression.BrotliStream（.NET 内置） |

提供同步/异步压缩解压，流式 API 和字节数组 API。

### 加密（CryptoHelper）

- **算法**：AES-256-CBC（带随机 IV）
- **密钥派生**：PBKDF2（密码 + 随机 salt，100000 次迭代，SHA256）
- 每次加密生成随机 salt + IV，同一数据每次密文不同
- Hash 校验：SHA256

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

## Scene 场景系统

Scene 是运行时游戏状态的统一容器，同时作为 IGameView 的实现。

```
Scene (IGameView + 状态字段)
├── ObservableScene (Scene + INotifyPropertyChanged)  ← Control 层用
└── HeadlessScene (Scene)                              ← Headless/测试用
```

Scene 持有 `IAssetManager` 引用（构造注入），在 IGameView 方法内部做 assetId → 实际资源的解析：
- 资源未找到 → 静默忽略，对象留存作为缓存占位符（后续释放即清除）
- 资源正常 → 更新 Scene 内部状态字段

### Scene 的职责

- 存储当前各音频轨道状态
- 存储图层列表（图片资源、位置等）
- 存储当前文本
- 场景开始后的界面抽象——可作为 ViewModel 或其基类
- **自身序列化**：Scene 提供 `SaveState()` / `RestoreState()` 方法

### 存档数据结构

SaveManager 统一调度存档：

```
GameSnapshot
├── CurrentNodeId
├── CurrentEntryIndex
├── Variables
└── SceneData  (Scene 自己序列化的状态字段)
```
