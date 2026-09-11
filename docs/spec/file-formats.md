# 项目与发布格式

## 项目目录

编辑器新建项目时创建如下目录；`Output`、`Temp` 和 `.galnet` 是工作目录，不是游戏内容源。

```text
Project/
  settings.json
  Graph/
    graph.json
    groups/*.galgroup
  Assets/
    Layer/ Audio/ Video/ ...
  I18n/
  Output/
  Temp/
  .galnet/editor-state.json
```

`settings.json` 存放项目设置；`graph.json` 是节点图和变量定义；每个 Group 的条目存放在单独的 `.galgroup`。编辑器状态（例如项目级编辑器信息）写入 `.galnet/editor-state.json`，不应作为运行时内容处理。

## graph.json

当前作者格式的版本为 2。节点有稳定字符串 ID、类型、名称和编辑器坐标；`Group` 节点以 `file` 指向 Group 条目文件，`Entry` 是图入口节点，`Branch` 节点携带 `branchType` 与 options 或 conditions。边包含稳定 ID、起点、出口索引与终点。

```json
{
  "version": 2,
  "name": "MyGame",
  "rootNodeId": "entry-id",
  "nodes": [
    { "id": "entry-id", "type": "Entry", "name": "Entry", "x": 100, "y": 100 },
    { "id": "group-id", "type": "Group", "name": "Opening", "x": 360, "y": 100,
      "file": "groups/group-id.galgroup" }
  ],
  "edges": [
    { "id": "edge-id", "fromNodeId": "entry-id", "fromOutlet": 0, "toNodeId": "group-id" }
  ],
  "playerVariables": [],
  "saveVariables": []
}
```

Runtime 的 `GraphLoader` 会将 `Entry` 和 `Group` 都转换为运行时 Group；实际条目仅由对应 `Group` 的 `.galgroup` 另行加载。运行时图不保留编辑器坐标、节点/边稳定 ID 或 Group 文件路径。

## .galgroup

`.galgroup` 是版本 1 的 JSON 作者格式，不再支持旧的分号文本格式。条目数组顺序就是执行顺序。每个条目必须拥有在本文件内唯一、非空的稳定 `id`；`type` 必须在 `EntryRegistry` 中注册。

```json
{
  "version": 1,
  "entries": [
    {
      "id": "entry-id",
      "type": "layer.show",
      "condition": "",
      "parameters": {
        "handleId": "layer-id",
        "assetId": "Layer/classroom.png",
        "transform": { "x": 0, "y": 0, "rotationDegrees": 0, "scaleX": 1, "scaleY": 1 },
        "z": 0,
        "displayMode": "Fill"
      }
    }
  ]
}
```

参数在文件中保持结构化 JSON。`GalgroupLoader` 将其编译为 Runtime 的字符串参数表，并验证 Layer 的 transform、展示模式、必填句柄和资源等特例。条目参数的权威清单见 [条目类型](entry-types.md)。

## 导出 .galpak

当前 `.galpak` 是 ZIP 容器，而非历史文档中描述的单一二进制 `.galnet` blob。导出器会创建：

```text
<Project>.galpak
  <Project>.galnet       JSON manifest（格式版本 1）
  Assets/content.pak     settings.json、Graph/**、I18n/**
  Assets/assets.pak      Assets/**
```

两个 `.pak` 均由 `PakBuilder` 构造，默认使用 Brotli；manifest 记录项目 ID、项目名称、导出时间、内容包入口及每个包的 SHA-256 和大小。导出在临时文件中完成，随后重新读取 ZIP、校验每个包哈希并验证 pak 可反序列化，最后才替换目标文件。

`.galnet` 在当前发布格式中只是 manifest 的文件名，不能假定它包含可直接运行的图数据。加密的 `.galnet`、单独 `.galnet` 逻辑包以及旧版 `.galpak` 布局均不是当前导出器承诺的格式。
