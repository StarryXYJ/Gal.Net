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

所有层均为接口化设计，方便替换实现（如 AES、SHA256 留待后续）。

```
IGameFile          资源文件 + 元数据（.meta 单独存储）
    ↓
IArchive          一组可寻址文件，支持 id 和路径加载
                   结构：寻址表 | 资源数据1(元数据+内容)、资源数据2、...
                   （实际打包为 .pak 文件）
    ↓
IAssetProvider    负责提供 Archive 实例
                   目前只实现本地文件提供（LocalFileProvider）
                   未来可扩展 HTTP 等
    ↓
AssetManager      资源管理器：加载、缓存（引用计数）、提供、卸载
                   提供全局 ID/路径寻址
                   支持两套模式：
                     - 开发模式：直接加载原始多文件（.meta + 资源文件）
                     - 打包模式：从 .pak 内加载编译后的资源块
    ↓
LoadAsync<T>(assetId)
Release(assetId)
```

### 接口示意

```csharp
// GalNet.Core/Assets 中定义
interface IAssetManager
{
    Task<T?> LoadAsync<T>(string assetId) where T : class;
    void Release(string assetId);
    bool IsLoaded(string assetId);
}
```

`GalNet.Assets` 项目负责所有实现（压缩、加密、打包、解包、寻址、缓存、引用计数等）。

### 双加载模式

AssetManager 内部根据运行环境切换数据源：

- **开发模式**（编辑器/调试）：直接读取 Assets 目录下的原始资源 + .meta 描述文件
- **打包模式**（发布/启动器）：从 .pak 文件解析寻址表，按 ID 加载资源数据块

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
