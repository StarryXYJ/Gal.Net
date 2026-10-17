# 项目与发布格式

## 项目目录

```text
Project/
  settings.json
  Contracts/
    GalNet.Project.Contracts.dll  工具使用的项目 entry 契约及依赖
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

`Contracts/` 不进入 galpak。游戏通过自己的契约项目引用取得同一集合；CLI/Editor 外层加载固定的 `GalNet.Project.Contracts.ProjectSchemas.Modules` 静态只读模块列表。缺少 DLL 时原始查询和结构校验仍可用，语义校验/编译/Editor 打开明确失败。Editor 新建项目会自动部署随程序分发的默认契约，无需用户准备 DLL，也可选择自定义构建目录。CLI 项目创建仍可先生成缺契约骨架；自定义部署方法见 [Sample 契约](../../src/Samples/GalNet.Sample.Contracts/README.md)。项目显示名改名不移动目录，不改变节点/entry ID；整项目删除采用系统回收站。

## Graph 与 Group

`Graph/graph.json` 是版本 2 的编辑图，包含稳定节点/边 ID、坐标、变量和 Group 的 `groups/<id>.rawgalgroup` 文件引用。`.rawgalgroup` 是版本 2、`kind: Raw` 的编辑源，可包含 primitive 和 composite；`.galgroup` 是版本 3、`kind: Compiled` 的运行产物，只能包含 primitive。旧分号文本、旧版本 Group 格式和运行时加载 Raw 文档均不受支持。

图允许多个前驱汇合到同一目标；每个输出口最多一条边，禁止自连接和连接到 Entry。GUI 与 CLI 新建连接只替换同一输出口的旧边。

变量声明的字段固定为 `name`、`defaultValue`、`type`，不随 JSON serializer 的命名策略变化；`defaultValue` 使用变量 converter 的 `Uid`、`Name`、`Type`、`Value`。编辑工具只接受当前源格式，不补稳定 ID、不从 settings 迁移变量。原始查询保留未知数据，当前类型化加载/保存仍拒绝未知字段/参数或不符合完整契约的内容；空字符串不会作为缺失参数删除。Editor 作者参数直接保存 JSON 值，保留复杂值、大整数、缺省与显式 null，不要求所有值先通过字符串表示往返。Group 源路径按 Graph 中的 file 原样保留，显示名改名不重建文件名。GUI 打开另要求显式且唯一的 Entry 根节点，结构/CLI 工具不因此统一限制为 GUI 入口规则。

共享保存先准备并校验 Graph/Group 写入计划，再暂存全部目标文件后发布。删除 Group 节点并删除其 entries 时同步删除该源文件；保留 entries 时源文件成为孤立作者文件。同一 Group 变更 source path 时，新路径写入计划并删除旧源文件。GUI 保存全部将 settings 与 editor-state 合入同一计划，失败不更新作者基线。作者文件事务在 `.galnet/editor/transactions/` 持久化 manifest、前后哈希和备份，后续发布失败逆序恢复；恢复失败明确报告并保留材料。GUI 打开项目核对未完成计划，外部内容不匹配时停止恢复，不承诺断电或跨进程修改下的整体原子事务。周期作者恢复快照另存于 `.galnet/editor/recovery/`，不进入游戏内容或跨重启 Undo 栈。普通文件事务也记录目录状态与大小写改名的中间路径；会话历史字节材料另存 `.galnet/editor/history/`，不进入发布包或跨重启 Undo 栈。详见 [Editor 项目历史与保存](editor-history-and-save.md) 和 [项目文件操作](editor-files.md)。

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
