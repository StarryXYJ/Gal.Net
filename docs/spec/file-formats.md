# 项目与发布格式

## 项目目录

编辑器新建项目时创建如下目录；`Output`、`Temp` 和 `.galnet` 是工作目录，不是游戏内容源。

```text
Project/
  settings.json
  gallery.json              optional
  Graph/
    graph.json
    groups/*.rawgalgroup
  Assets/
    Layer/ Audio/ Video/ ...
  I18n/
  Output/
  Temp/
  .galnet/editor-state.json
```

`settings.json` 存放项目设置；可选的 `gallery.json` 存放 Gallery 类型与资源条目；`graph.json` 是节点图和变量定义；每个 Group 的编辑源存放在单独的 `.rawgalgroup`。编辑器状态（例如项目级编辑器信息）写入 `.galnet/editor-state.json`，不应作为运行时内容处理。

## gallery.json

当前 Gallery authoring 格式版本为 1。`types` 把 Gallery type ID 关联到一个资源类型字符串；`items` 使用稳定 item ID 引用某个已注册类型和资源 ID。Gallery catalog 加载后不可变，类型和 item ID 会规范化为小写。

```json
{
  "version": 1,
  "types": [
    { "typeId": "cg", "resourceType": "sprite" },
    { "typeId": "audio", "resourceType": "audio" }
  ],
  "items": [
    {
      "id": "opening_cg",
      "typeId": "cg",
      "resourceId": "asset-guid",
      "title": "Opening",
      "sortOrder": 10
    }
  ]
}
```

约束：

- `typeId` 只能使用字母、数字、下划线、短横线和点，忽略首尾空白并规范化为小写；规范化后不能重复。
- `resourceType` 是非空字符串名称，使用同一字符规则并规范化为小写。Gallery 不把它解析成 `ResourceType` enum，也不注册 decoder。
- item `id` 只能使用字母、数字和下划线，规范化为小写并在整个 catalog 内唯一；它不从资源路径、标题或排序生成。
- item 的 `typeId` 必须引用已注册类型，`resourceId` 不能为空；`title` 与 `sortOrder` 可省略。
- 文件不存在时内容提供者使用空 Gallery catalog；文件存在但版本、JSON 或引用无效时加载明确失败。

`DirectoryGameContentProvider` 从内容根目录读取 `gallery.json`；Editor Preview 先把当前内存 catalog 写入预览临时目录，再由 `EditorGameDataProvider` 读取同一格式。结果均通过 `GameContent.Gallery` 暴露。Gallery catalog 只包含静态类型和资源条目；每个 item 在加载后生成一个默认 `false` 的 Player bool `gallery_<item-id>_unlocked`，解锁值保存在 Player variable store 中，不回写 `gallery.json`，也不进入存档槽快照。

Editor 新项目默认注册 `cg -> sprite`、`video -> video` 与 `audio -> audio`。资源检查器按资源 `.meta` 的 `type` 列出全部匹配 registration，标注只修改集中式 `gallery.json`；item 的 `resourceId` 使用 `.meta` 的稳定 asset ID。导出对当前内置媒体类型验证资源存在且类型匹配。

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
      "file": "groups/group-id.rawgalgroup" }
  ],
  "edges": [
    { "id": "edge-id", "fromNodeId": "entry-id", "fromOutlet": 0, "toNodeId": "group-id" }
  ],
  "playerVariables": [],
  "saveVariables": []
}
```

编辑器图的 `file` 始终指向 `.rawgalgroup` 源文件。Runtime 的 `GraphLoader` 会将 `Entry` 和 `Group` 都转换为运行时 Group；实际条目仅由编译后的 `.galgroup` 另行加载。运行时图不保留编辑器坐标、节点/边稳定 ID 或 Group 文件路径。

## .rawgalgroup 与 .galgroup

两种文件均使用版本 2 的 JSON `GroupDocument`，不支持旧的分号文本格式或旧版本。条目数组顺序就是执行顺序；每个条目必须拥有在本文件内唯一、非空的稳定 `id`。Raw 条目的 `type`、参数名、必填性、默认值和 JSON 类型由当前 target profile 的 primitive/composite entry schema 校验，而不是全局 `EntryRegistry`。

- `.rawgalgroup` 是编辑源，`kind` 必须为 `Raw`。其中可包含原语和非原语。
- `.galgroup` 是编译产物，`kind` 必须为 `Compiled`。其中只能包含原语；Runtime 会拒绝 Raw 文档和任何非原语。

原语在 Compiled 文档中是 `{ id, typeId, batchId, condition, arguments }` 的通用信封，Runtime 通过 `IGameView.Dispatch` 按完整 `typeId` 动态路由。`arguments` 只保存 JSON 值，不保存 CLR 类型名；当前 profile 的冻结 `DynamicParameterTable` 负责解释和校验这些值。`batchId` 是可选局部分组字段，由编译器从 authoring 参数中提升出来，运行时只在一次 Group 执行内用它匹配 skip 批次。Composite 没有 Runtime 执行入口，而是根据自身 schema 编译为有序 primitive；展开后每个 primitive 都必须由所选 profile 支持。

`.rawgalgroup` 示例：

```json
{
  "version": 2,
  "kind": "Raw",
  "entries": [
    {
      "id": "entry-id",
      "type": "custom.pulse",
      "condition": "",
      "parameters": {
        "count": 3,
        "enabled": true,
        "payload": { "source": "intro" }
      }
    }
  ]
}
```

`GalgroupCompiler` 会在编辑器预览前将 Raw 文档编译为 Compiled 文档，并返回原始稳定 ID 到生成稳定 ID 的 source map。当前生成 ID 采用 `<source-id>#<ordinal>` 形式。输出保留结构化 JSON 参数；`GalgroupLoader` 验证 envelope，而模块按其 descriptor 解释 arguments。条目参数的权威来源是所选 target profile，见[条目类型](entry-types.md)。

## 导出 .galpak

当前 `.galpak` 是 ZIP 容器，而非历史文档中描述的单一二进制 `.galnet` blob。导出器会创建：

```text
<Project>.galpak
  <Project>.galnet       JSON manifest（格式版本 1）
  Assets/content.pak     settings.json、gallery.json（若存在）、Graph/**、I18n/**
  Assets/assets.pak      Assets/**
```

两个 `.pak` 均由 `PakBuilder` 构造，默认使用 Brotli；manifest 记录项目 ID、项目名称、导出时间、内容包入口及每个包的 SHA-256 和大小。导出在临时文件中完成，随后重新读取 ZIP、校验每个包哈希并验证 pak 可反序列化，最后才替换目标文件。

`.galnet` 在当前发布格式中只是 manifest 的文件名，不能假定它包含可直接运行的图数据。加密的 `.galnet`、单独 `.galnet` 逻辑包以及旧版 `.galpak` 布局均不是当前导出器承诺的格式。

编辑器预览已经执行 Raw→Compiled 编译；正式 `.galpak` 导出接入编译仍是待办，见 [杂项待办](../design/misc-todo.md)。
