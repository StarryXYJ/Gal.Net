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

变量声明的字段固定为 `name`、`defaultValue`、`type`，以及可选字符串 `description`（作者说明，省略时为空），不随 JSON serializer 的命名策略变化；`defaultValue` 使用变量 converter 的 `Uid`、`Name`、`Type`、`Value`。编辑工具只接受当前源格式，不补稳定 ID、不从 settings 迁移变量。settings.json 不再包含 playerVariables/saveVariables，变量声明只在 Graph 保存。GUI 使用 Authoring 模式保留未知 Entry 类型/参数、不完整参数和条件表达式；原始 JSON 结构、信封字段和稳定身份必须有效，未知 Graph/Group 顶层或 Entry 信封字段仍拒绝。默认 Complete 仓库与 CLI 检查/编译继续要求完整契约有效；空字符串不会作为缺失参数删除。Editor 作者参数直接保存 JSON 值，保留复杂值、大整数、缺省与显式 null，不要求所有值先通过字符串表示往返。Group 源路径按 Graph 中的 file 原样保留，显示名改名不重建文件名。GUI 打开另要求显式且唯一的 Entry 根节点，结构/CLI 工具不因此统一限制为 GUI 入口规则。

共享保存先准备并校验 Graph/Group 写入计划，再暂存全部目标文件后发布。删除 Group 节点并删除其 entries 时同步删除该源文件；保留 entries 时源文件成为孤立作者文件。同一 Group 变更 source path 时，新路径写入计划并删除旧源文件。GUI 保存全部将 settings、editor-state 和已跟踪的普通文本字节合入同一计划，失败不更新作者基线。普通文本描述项的 ID/路径/存在状态/字节属于 Editor 会话与恢复材料，不改变游戏源文件格式；结构操作对 Working/Saved 分别变换，文件页关闭不丢弃缓冲。作者文件事务在 `.galnet/editor/transactions/` 持久化 manifest、前后哈希和备份，后续发布失败逆序恢复；恢复失败明确报告并保留材料。GUI 打开项目核对未完成计划，外部内容不匹配时停止恢复，不承诺断电或跨进程修改下的整体原子事务。周期作者恢复快照另存于 `.galnet/editor/recovery/`，不进入游戏内容或跨重启 Undo 栈。普通文件事务也记录目录状态与大小写改名的中间路径；会话历史字节材料另存 `.galnet/editor/history/`，不进入发布包或跨重启 Undo 栈。详见 [Editor 项目历史与保存](editor-history-and-save.md) 和 [项目文件操作](editor-files.md)。

Compiled primitive 使用 `{ id, typeId, batchId, condition, arguments }` 信封。`arguments` 保存 JSON 值，不保存 CLR 类型名；当前 target profile 的冻结 schema 负责校验。项目保持唯一 Graph；Graph/Group 发布为 PAK 内的 graph/group 资源，开发编译目录由 CompiledStoryAssetProvider 适配同一资源 ID。保留 Graph ID 为 `galnet:graph:main`，Group ID 从稳定节点 ID 派生为 `galnet:group:<id>`；不新增多图列表、默认图选择或跨图存档字段。运行图还保留 playerVariables/saveVariables 声明，宿主创建新 Run 时据此初始化默认值。

Group 节点 DTO 可保存可选字符串 `storyPackage`；空字符串属于本体，同名聚合为剧情包。编译 Graph 保留节点和包名，正文只进入相应 story PAK。GUI 编辑入口见后续作者工具阶段，现阶段可在作者 JSON 中填写。

## 项目配置 `settings.json`

配置文档版本为 1，替代旧的平铺设置格式，不提供兼容迁移：

```json
{
  "version": 1,
  "sections": {
    "galnet.project": { "version": 1, "data": { "defaultWidth": 1920 } },
    "developer.options": { "version": 1, "data": { "enabled": true } }
  }
}
```

根信封固定为 version/sections；section 信封固定为正整数 version 和对象 data。重复 JSON 属性拒绝。未知 section 的版本和 data 原样保留；已声明 section 的未知字段在类型化修改时保留。缺省 section 按调用方声明读取新的默认 DTO，不写入文件。类型化读取是隔离快照，修改后须调用 `configuration.Set(section, dto)` 显式发布。项目契约可公开静态 `ProjectConfigurationSchema Configurations`，缺省为空；schema 复用 FieldDescriptor/ValueValidation，负责已声明字段和领域规则。默认模板显式选择可选 `DefaultProjectConfiguration.Schema`，不由 Core/Storage 自动注入。

通用作者表单通过 section.Resolve 读取隔离的有效值快照（含默认值），不会把默认值写回原始配置；原始 section 仍是唯一持久源。项目设置窗口的字段草稿通过所选 schema 校验后批量进入项目历史，Undo/Redo 保留 section 的缺省状态及无关 section。

配置补丁命令为 `project.configuration.patch`，接收 `sectionId`、必需的 `values` 对象及可选 `remove` 字段 ID 列表。先删除、后覆盖，显式 null 与缺省分开；仅允许修改声明的字段，完整候选验证成功才替换 section，其他字段/section 保留。构建与导出使用调用方所选 schema，在发布产物前验证已存储配置。

## `.galpak` 导出与安装

`.galpak` 是 ZIP 分发容器：

```text
<Project>.galpak
  <Project>.galnet          JSON manifest，版本 1
  settings.json
  content.json             唯一 Graph 与分包描述，版本 2
  I18n/**
  gallery.json              从 metadata 派生
  Assets/Paks/000-base.pak  资源 PAK
```

manifest 列出每个 ZIP 条目的路径、大小和 SHA-256。导出在临时文件中创建，重新验证 ZIP 条目与 PAK 后才替换目标文件。

传入 `.galpak` 时，`GameInstallation.OpenAsync` 先校验 ZIP 根目录唯一 manifest、声明路径、条目大小与 SHA-256，再原子解压到包文件旁的同名目录。已存在且与 manifest 一致的安装目录可复用；不同或损坏内容不会覆盖现有目录。

运行时只装载 content.json 中声明并校验的基础分片，额外丢入 Assets/Paks 的文件不贡献覆盖。content.json v2 保存固定 graphId、projectId、buildId、basePaks 和 stories；每个分片描述路径、大小、SHA-256。Graph 和编译 Group 经资源 provider 加载，特殊 JSON/I18n 文件仍走内容加载入口。旧内容需重新导出，不回退到 loose Graph。

正常导出还在 galpak 旁生成 `StoryPatches/<buildId>/*.pak`；目录导出则放在目标目录内同一路径。装载同时检查安装根及其父目录下当前 buildId 的剧情目录，以头部而非文件名识别分片。整个剧情包零分片表示未安装；存在部分分片、哈希错误、用途/项目/构建不匹配或依赖缺失都拒绝。缺席 Group 沿正常出口跳过；纯空 Group 推进循环拒绝。剧情目录按构建隔离并整体发布，旧基础包不会采用新构建的剧情片段。

导出产生布局报告（galpak 旁 `<Project>.layout.json` 或目录内 packaging-layout.json），列出资源归属、直接依赖和未打包项。settings/I18n/人物数据仍完整留在基础内容，不按剧情拆分；未安装剧情不参与正文文本引用校验。Gallery 保持原基础聚合。

GameSnapshot 当前为 v5，记录 ContentBuildId 和 StoryPackages；恢复拒绝不同构建或缺少记录的剧情包。首版保守记录会话准备时已安装的全部剧情包，因此删除当时尚未播放的包也会阻止恢复；不自动降级到跳过剧情。开发目录构建标识为空。

资源归档格式为 GPAK v3，包含 base/story/override 用途控制描述；旧 v2 PAK 需重新导出。控制字段和 metadata 依赖校验见 [资源规范](assets.md#pak)。override 装载在独立资源补丁阶段接入。

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
