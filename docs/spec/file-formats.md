# 文件格式与编译管道

---

## 格式总览

```
graph.json          ← 节点/边元数据（位置、参数等），每个 Group 对应一个 .galgroup 文件
*.galgroup          ← 组条目文件，JSON（版本化、有序 Entry）
.galnet             ← 编译产物：graph.json + 所有 galgroup → 单文件（可二进制化/加密/压缩），纯逻辑不含资源
.galpak             ← 最终分发包：.galnet + 资源文件 → 压缩打包
```

---

## graph.json

每个项目一张主图，存储所有节点和边的元数据（节点类型、位置、显示名称、配置参数等）。Group 节点通过一个 `file` 字段引用对应的 `.galgroup` 文件路径。

```json
{
  "version": 1,
  "name": "MyGame",
  "rootNodeId": "group_intro",
  "nodes": [
    {
      "id": "group_intro",
      "type": "Group",
      "name": "开场",
      "x": 100, "y": 220,
      "file": "groups/intro.galgroup"
    },
    {
      "id": "branch_01",
      "type": "Branch",
      "name": "选择路线",
      "x": 350, "y": 220,
      "branchType": "Choice",
      "options": [
        { "text": "选项A", "condition": "" },
        { "text": "选项B", "condition": "flag == true" }
      ]
    }
  ],
  "edges": [
    { "fromNodeId": "group_intro", "fromOutlet": 0, "toNodeId": "branch_01" }
  ]
}
```

---

## .galgroup 格式

`.galgroup` 是 JSON 权威源；不再接受旧的分号文本格式。文档包含版本号和按数组顺序执行的条目。每个条目都有编辑器稳定 ID、类型、可选条件和结构化参数。运行时加载时会将 JSON 显式编译为 Runtime `Entry`，Runtime 本身不解析编辑器格式。

```json
{
  "version": 1,
  "entries": [
    {
      "id": "8fa123d4-cc1a-4a6a-a2c6-4f03013f2fe6",
      "type": "layer.show",
      "parameters": {
        "handleId": "c6b0b154-2f43-4e1d-bcf1-0dd6936522e4",
        "assetId": "assets/backgrounds/classroom.png",
        "transform": { "x": 0, "y": 0, "rotationDegrees": 0, "scaleX": 1, "scaleY": 1 },
        "z": 0,
        "displayMode": "Fill"
      }
    }
  ]
}
```

`id` 是条目在编辑器中的稳定标识；`handleId` 是场上实例的内部句柄，两者都应为 GUID 字符串。编辑器向开发者展示资源和实例名称/类型，并通过资源定位器写入句柄，不要求开发者输入或识别 GUID。

---

## .galnet 格式

**编译后的游戏逻辑单文件**，内容来源：
- `graph.json`（节点/边结构）
- 所有 `.galgroup`（条目列表）

格式为二进制 blob（可加密、压缩），供 Runtime 直接加载。不含资源文件（资源由 Assets 模块独立管理）。

### 结构

```
[Header]        ← 版本号 + 图数量 + 是否加密 + 是否压缩
[GraphData]     ← 序列化的 Graph 对象（含所有组的 Entry 列表）
[Hash]          ← SHA256 完整性校验
```

---

## .galpak 格式

**最终分发的游戏包**，启动器可导入。内容：
- `.galnet`（游戏逻辑，可加密/压缩）
- 资源文件列表（每个资源可独立设置压缩/加密选项）
- `Manifest` 清单（资源索引、版本、入口图 ID、全局 Hash）

### 结构

```
[Manifest]      ← JSON，资源索引 + 元数据
[.galnet blob]  ← 二进制游戏逻辑块
[Asset_1]       ← 资源数据（可选压缩/加密）
[Asset_2]
...
[Asset_N]
[GlobalHash]    ← 整体 SHA256
```

---

## 数据流

### 开发期

```
图编辑 ──→ graph.json（节点 + 边 + 元数据）
组编辑 ──→ *.galgroup（JSON 条目数组）
条目编辑 ──→ 组文件中的一个 JSON Entry，参数保持结构化
```

### 运行时（编辑器预览 / 启动器运行）

```
Runtime 加载（由 GameEngine 驱动）：
  1. 读取 graph.json → 解析节点/边结构
  2. 按 Group 节点引用的 path 加载对应 .galgroup → 验证 JSON 并编译 Entry
  3. GameEngine 通过 IGameRuntime 接口统一管理运行时状态
     （CurrentNodeId/EntryIndex/VariableStore/SceneState/调用栈/View/I18n）
  4. 定位入口节点，开始状态机循环
  （开发期目录形式或 .galnet 单文件形式，加载路径不同但内部逻辑一致）
```

### 导出（编辑器 → .galpak）

```
                                                                  ┌── 可选加密
                                                                  │
graph.json + *.galgroup ──→ 序列化 ──→ .galnet ──┐
                                                                  │         │
资源文件 ──→ 构建索引（Manifest）─────────────────────────────────→ ────────┤
                                                                  │         │
                                          .galnet + 资源 ──→ 打包 ──→ .galpak
                                                                  │
                                                      ┌── 可选压缩/加密
                                                      └── SHA256 校验
```

### 导入（启动器）

```
.galpak ──→ 校验 Hash ──→ 解压/解密 ──→ 提取 .galnet + 资源
                                            │
                                            └── Runtime 加载 .galnet ──→ 运行
```
