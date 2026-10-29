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

项目根不存在可编辑 `gallery.json`。Gallery 从 `Assets/**/*.meta` 的 `gallery[]` 聚合：类型由宿主代码/插件注册，item ID 为全局唯一正整数，资源 ID 是父 metadata 的 GUID。开发运行在内容加载时聚合一次；发行运行读取 `game.pak` 内导出生成的 `gallery.json`。

`I18n/` 的人物表、文本源与默认语言完整性检查见 [国际化](i18n.md)。它是特殊内容，不是资源 provider 输入。

`Contracts/` 不进入 galpak。游戏通过自己的契约项目引用取得同一集合；CLI/Editor 外层加载固定的 `GalNet.Project.Contracts.ProjectSchemas.Modules` 静态只读模块列表。缺少 DLL 时原始查询和结构校验仍可用，语义校验/编译/Editor 打开明确失败。Editor 新建项目会自动部署随程序分发的默认契约，无需用户准备 DLL，也可选择自定义构建目录。CLI 项目创建仍可先生成缺契约骨架；自定义部署方法见 [Sample 契约](../../src/Samples/GalNet.Sample.Contracts/README.md)。项目显示名改名不移动目录，不改变节点/entry ID；整项目删除采用系统回收站。

Editor 新建项目可先选择游戏 App EXE：契约目录为空时，只在 EXE 同级与同级 `lib/` 查找 `GalNet.Project.Contracts.dll`，分别对应普通构建与当前发行布局。唯一结果自动填充目录；已有输入不覆盖，缺失或多份结果提示手动选择。已选 App 时不因契约缺失回退到默认契约。自定义来源在创建前解析静态托管依赖闭包并验证，仅部署契约及其非平台托管依赖，不复制 App、运行时或无关文件；DLL 字节保持不变。动态或原生加载不属于作者契约。未选 App 且契约目录为空时保留默认契约部署流程。所选 EXE 在进入工作区前保存到 `.galnet/editor/debug-launch.json`，供现有外部调试启动流程使用。

## Graph 与 Group

`Graph/graph.json` 是版本 2 的编辑图，包含稳定节点/边 ID、坐标、变量和 Group 的 `groups/<id>.rawgalgroup` 文件引用。`.rawgalgroup` 是版本 2、`kind: Raw` 的编辑源，可包含 primitive 和 composite；`.galgroup` 是版本 3、`kind: Compiled` 的运行产物，只能包含 primitive。旧分号文本、旧版本 Group 格式和运行时加载 Raw 文档均不受支持。

图允许多个前驱汇合到同一目标；每个输出口最多一条边，禁止自连接和连接到 Entry。GUI 与 CLI 新建连接只替换同一输出口的旧边。

变量声明的字段固定为 `name`、`defaultValue`、`type`，以及可选字符串 `description`（作者说明，省略时为空），不随 JSON serializer 的命名策略变化；`defaultValue` 使用变量 converter 的 `Uid`、`Name`、`Type`、`Value`。编辑工具只接受当前源格式，不补稳定 ID、不从 settings 迁移变量。settings.json 不再包含 playerVariables/saveVariables，变量声明只在 Graph 保存。GUI 使用 Authoring 模式保留未知 Entry 类型/参数、不完整参数和条件表达式；原始 JSON 结构、信封字段和稳定身份必须有效，未知 Graph/Group 顶层或 Entry 信封字段仍拒绝。默认 Complete 仓库与 CLI 检查/编译继续要求完整契约有效；空字符串不会作为缺失参数删除。Editor 作者参数直接保存 JSON 值，保留复杂值、大整数、缺省与显式 null，不要求所有值先通过字符串表示往返。Group 源路径按 Graph 中的 file 原样保留，显示名改名不重建文件名。GUI 打开另要求显式且唯一的 Entry 根节点，结构/CLI 工具不因此统一限制为 GUI 入口规则。

共享保存先准备并校验 Graph/Group 写入计划，再暂存全部目标文件后发布。删除 Group 节点并删除其 entries 时同步删除该源文件；保留 entries 时源文件成为孤立作者文件。同一 Group 变更 source path 时，新路径写入计划并删除旧源文件。GUI 保存全部将 settings、editor-state 和已跟踪的普通文本字节合入同一计划，失败不更新作者基线。普通文本描述项的 ID/路径/存在状态/字节属于 Editor 会话与恢复材料，不改变游戏源文件格式；结构操作对 Working/Saved 分别变换，文件页关闭不丢弃缓冲。作者文件事务在 `.galnet/editor/transactions/` 持久化 manifest、前后哈希和备份，后续发布失败逆序恢复；恢复失败明确报告并保留材料。GUI 打开项目核对未完成计划，外部内容不匹配时停止恢复，不承诺断电或跨进程修改下的整体原子事务。周期作者恢复快照另存于 `.galnet/editor/recovery/`，不进入游戏内容或跨重启 Undo 栈。普通文件事务也记录目录状态与大小写改名的中间路径；会话历史字节材料另存 `.galnet/editor/history/`，不进入发布包或跨重启 Undo 栈。详见 [Editor 项目历史与保存](editor-history-and-save.md) 和 [项目文件操作](editor-files.md)。

Compiled primitive 使用 `{ id, typeId, batchId, condition, arguments }` 信封。`arguments` 保存 JSON 值，不保存 CLR 类型名；当前 target profile 的冻结 schema 负责校验。项目保持唯一 Graph；Graph/Group 发布为 PAK 内的 graph/group 资源，开发编译目录由 CompiledStoryAssetProvider 适配同一资源 ID。保留 Graph ID 为 `galnet:graph:main`，Group ID 从稳定节点 ID 派生为 `galnet:group:<id>`；不新增多图列表、默认图选择或跨图存档字段。运行图还保留 playerVariables/saveVariables 声明，宿主创建新 Run 时据此初始化默认值。

Group 节点 DTO 可保存可选字符串 `storyPackage`；空字符串属于本体，同名聚合为剧情包。编译 Graph 保留节点和包名，正文只进入相应 story PAK。GUI 节点检查器可编辑名称，进入项目历史并随 Graph 保存。

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

资源补丁作者数据使用可选 `galnet.resourceOverrides` section（版本 1）：packages 为 ResourceOverrideDefinition 列表（稳定 id、name、priority、mappings、includeResources、declaredCompleteResources），additionalOriginals 为补充的原资源 ID 列表。定义随 settings 保存并进入已有 Undo/Redo、工作版/保存版和作者恢复快照；不创建独立 UI 状态文件作为真源。矩阵名称回车/失焦、数字优先级变更及映射/列操作自动发布该配置节，撤销/重做同样持久化；不提交其他页面输入，不保存其他未发布配置。资源补丁导出只读取保存版定义并捕获资源快照。additionalOriginals 同时成为基础打包根。

## `.galpak` 导出与安装

`.galpak` 是 ZIP 分发容器：

```text
<Project>.galpak
  pak/
    game.pak                固定入口，内部文档 GPAK
    localization.pak        内置文本，每种语言一个逻辑条目
    Assets/Paks/000-base.pak 基础资源首片，后续为 001-base.pak 等
    StoryPatches/<buildId>/  即使无剧情包也保留空目录
    ResourcePatches/         即使无补丁也保留空目录
    I18n/                    空的外置翻译覆盖入口
```

新导出为 galpak v3。`game.pak` 内含六个紧凑 JSON 文档：`content.json`、`installation.json`、`settings.json`、`gallery.json`、`characters.json` 和 `packaging-layout.json`。文档容器复用 GPAK v3 的压缩与逐项校验，采用独立固定的 document 类型目录，不加入普通资源 provider。`localization.pak` 使用同一文档容器，每个文化为 `texts/<culture>.json` 平铺字典；作者文件仍可按多个嵌套 JSON 编辑，导出时按原相对路径 ordinal 顺序合并。

内部 `installation.json` 为版本 3，列出基础资源 PAK 与 localization.pak 的相对路径、大小和 SHA-256；不包含 game.pak 自身，避免循环哈希。入口文档由 GPAK 逐项校验，并交叉校验项目、构建身份及 content/index 一致性。ZIP 条目额外带 `pak/` 前缀，显式保存空扩展目录。v3 拒绝内容根外条目、重复 ZIP 路径和未声明载荷。导出在临时文件中创建，重新验证 ZIP 条目与 PAK 后才替换目标文件。压缩封装不提供加密或防提取保证。

传入 `.galpak` 时，`GameInstallation.OpenAsync` 先读取固定 `pak/game.pak` 入口和内部安装清单，校验声明路径、条目大小与 SHA-256，再原子安装到包文件旁的 `pak/`。`GalpakInstaller.InstallAsync` 显式目标表示实际内容根，目标为 `app/pak` 时不会再追加 `pak`；返回实际内容根。普通解压工具选择解压到应用目录同样得到 `app/pak`。相同安装可复用并保留外置翻译/补丁；不同或损坏内容不会覆盖现有目录。安装器仍兼容 v1 平铺归档与 v2 的 `pak/<Project>.galnet` 布局；v1 默认目标保留包文件同名目录，v2/v3 默认目标为 pak。新导出只生成 v3。

运行时只装载 content 描述中声明并校验的基础分片，额外丢入 Assets/Paks 的文件不贡献覆盖。包内 content.json v3 保存固定 graphId、projectId、buildId、basePaks、stories 和 localizationPak；每个文件描述路径、大小、SHA-256。Graph 和编译 Group 经普通资源 provider 加载，配置/人物/文本从文档包读取。旧 content v2 安装目录仍按散文件读取；发现 game.pak 时优先使用且损坏报错，不回退到散文件。旧 GPAK v2 或 loose Graph 发行内容仍需重新导出。

正常导出还在 galpak 旁生成 `StoryPatches/<buildId>/*.pak`；目录导出则放在目标目录内同一路径。装载同时检查安装根及其父目录下当前 buildId 的剧情目录，以头部而非文件名识别分片。整个剧情包零分片表示未安装；存在部分分片、哈希错误、用途/项目/构建不匹配或依赖缺失都拒绝。缺席 Group 沿正常出口跳过；纯空 Group 推进循环拒绝。剧情目录按构建隔离并整体发布，旧基础包不会采用新构建的剧情片段。

buildId 对资源内容、有效 metadata、特殊内容、格式标记和实际分片布局计算稳定指纹；默认阈值或切片算法改变物理布局时也产生新的构建身份。

导出产生布局报告（galpak 旁 `<Project>.layout.json`，以及 game.pak 内 packaging-layout.json），列出资源归属、直接依赖和未打包项。配置、人物、文本完整留在本体的文档包，不按剧情拆分；未安装剧情不参与正文文本引用校验。Gallery 保持原基础聚合。资源补丁不能替换这些内部文档。

GameSnapshot 当前为 v5，记录 ContentBuildId 和 StoryPackages；恢复拒绝不同构建或缺少记录的剧情包。首版保守记录会话准备时已安装的全部剧情包，因此删除当时尚未播放的包也会阻止恢复；不自动降级到跳过剧情。开发目录构建标识为空。

资源归档格式为 GPAK v3，包含 base/story/override 用途控制描述；旧 v2 PAK 需重新导出。控制字段和 metadata 依赖校验见 [资源规范](assets.md#pak)。

资源覆盖包独立放在安装根或其父目录的 `ResourcePatches/*.pak`。头部 kind=override、projectId 对应项目、buildId 为空，不依赖先导出基础构建。包保存 packageId、priority、原资源 ID→替换资源 ID 的 mappings，携带替换资源完整闭包。准备期校验全部 payload，按 priority 降序、完整文件 SHA-256 ordinal 降序决定各原资源的胜者，同 hash 去重。文件名不参与优先级，无语言条件。

GameInstallation 缓存当前包集合；重启或新建安装会话采用新增/移除包。已持有 handle 保持值和释放约定。未安装剧情的映射不创建原资源，不能使剧情被视为已安装。Graph/Group 禁止通过资源补丁覆盖。

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
