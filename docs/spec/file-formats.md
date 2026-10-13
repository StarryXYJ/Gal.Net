# 项目与发布格式

## 项目目录

```text
Project/
  settings.json
  Graph/
    graph.json
    groups/*.rawgalgroup
  Assets/
    **/*                 原始资源
    **/*.meta            资源 metadata 与 Gallery 标注
  I18n/
    catalog/characters.json 人物 ID 与姓名 key
    <culture>/**/*.json     游戏文本
  Output/ Temp/ .galnet/  工作文件，不是游戏内容
```

项目根不存在可编辑 `gallery.json`。Gallery 从 `Assets/**/*.meta` 的 `gallery[]` 聚合：类型由宿主代码/插件注册，item ID 为全局唯一正整数，资源 ID 是父 metadata 的 GUID。开发运行在内容加载时聚合一次；发行运行只读取导出生成的 `gallery.json`。

`I18n/` 的人物表、文本源与默认语言完整性检查见 [国际化](i18n.md)。它是特殊内容，不是资源 provider 输入。

## Graph 与 Group

`Graph/graph.json` 是版本 2 的编辑图，包含稳定节点/边 ID、坐标、变量和 Group 的 `groups/<id>.rawgalgroup` 文件引用。`.rawgalgroup` 是版本 2、`kind: Raw` 的编辑源，可包含 primitive 和 composite；`.galgroup` 是 `kind: Compiled` 的运行产物，只能包含 primitive。旧分号文本、旧版本 Group 格式和运行时加载 Raw 文档均不受支持。

变量声明的字段固定为 `name`、`defaultValue`、`type`，不随 JSON serializer 的命名策略变化；`defaultValue` 使用变量 converter 的 `Uid`、`Name`、`Type`、`Value`。编辑工具只接受当前源格式，不补稳定 ID、不从 settings 迁移变量。原始查询保留未知数据，类型化保存则拒绝未知字段/参数及不能无损转换到编辑值的参数；空字符串不会作为缺失参数删除。

共享保存先准备并校验 Graph/Group 写入计划，再暂存全部目标文件后发布。删除 Group 节点并删除其 entries 时同步删除该源文件；保留 entries 时源文件成为孤立作者文件。后续发布失败会恢复此前发布的文件；恢复失败明确报告。该机制只提供单进程失败恢复，不承诺进程崩溃或跨进程修改下的整体原子事务。

Compiled primitive 使用 `{ id, typeId, batchId, condition, arguments }` 信封。`arguments` 保存 JSON 值，不保存 CLR 类型名；当前 target profile 的冻结 schema 负责校验。Graph 与 Group 目前是特殊内容文件，不通过资源 GUID provider 查询。

## `.galpak` 导出与安装

`.galpak` 是 ZIP 分发容器：

```text
<Project>.galpak
  <Project>.galnet          JSON manifest，版本 1
  settings.json
  Graph/**
  I18n/**
  gallery.json              从 metadata 派生
  Assets/Paks/000-base.pak  资源 PAK
```

manifest 列出每个 ZIP 条目的路径、大小和 SHA-256。导出在临时文件中创建，重新验证 ZIP 条目与 PAK 后才替换目标文件。

传入 `.galpak` 时，`GameInstallation.OpenAsync` 先校验 ZIP 根目录唯一 manifest、声明路径、条目大小与 SHA-256，再原子解压到包文件旁的同名目录。已存在且与 manifest 一致的安装目录可复用；不同或损坏内容不会覆盖现有目录。

运行时从解压目录读取特殊内容，并且只扫描 `Assets/Paks/**/*.pak` 作为资源源。PAK 按相对路径倒序覆盖：`000-base.pak` 是基础包，后续/更高排序路径的包可作为补丁覆盖同 GUID 资源。ZIP、manifest、Graph、I18n、settings 和 gallery 不进入 `IAssetProvider`。

## 生成的 `gallery.json`

生成文件是版本 2 的只读运行时数据，含冻结的 Gallery type 快照和聚合后的 item：

```json
{
  "version": 2,
  "types": [{ "typeId": "cg", "resourceType": "sprite" }],
  "items": [{ "id": 100, "typeId": "cg", "resourceId": "asset-guid", "title": "Opening" }]
}
```

加载时会与当前冻结 `IGalleryTypeCatalog` 校验；生成文件不会反向注册类型。每项对应 Player bool `gallery_<id>_unlocked`，解锁状态不写回 metadata 或该 JSON。
