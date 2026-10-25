# 项目工具 CLI

外部 Agent 创作 Skill 随 CLI 分发在 `Skills/galnet-project-authoring/`，支持文本转 Group、给对白补演出和资源导入。完整使用流程及 Editor 热重载见[外部工具创作](../../../docs/manual/external-project-authoring.md)。

```text
project apply <project> <manifest-file> [--dry-run]
help project.apply --json
```

`project.apply` 将原生作者文件与资源作为一个批次应用。版本 1 manifest 包含 `writes`/`deletes`；write 为 `path`、相对 manifest 目录的 `source`、`contentHash` 与必需的 `expectedHash`，delete 为 `path`/`expectedHash`。哈希使用 SHA-256；`expectedHash: null` 明确要求新目标不存在。target 拒绝链接、越界和 Contracts/Output/Temp/内部目录，Graph/settings 固定入口不可删除。资源与 sidecar 必须配对，替换已有 metadata 保持 ID；被移除资源/Graph 节点和删除路径的剩余静态 JSON 引用需同批更新。

dry-run 在临时候选中运行当前项目契约的完整校验，不写项目。原有媒体只建立存在性占位，不复制或解码载荷；variants 描述与 metadata 保留真实内容供校验。正式发布复核目标原哈希、校验输入和项目文件集合，再用作者文件事务统一复制/删除；失败补偿，成功无持久 Undo。报告包含 `writtenFiles`、`deletedFiles`、`contentHashes` 和 `validation`，沿用完整性及 skippedChecks；自定义代码引用和媒体内容未被检查。

Editor 对任意来源文件修改执行通用热重载：无本地未保存内容/草稿时采纳新基线并刷新打开页面；有冲突时保留当前输入。采纳外部版本清除旧 Undo/Redo，Git 或用户备份负责后续回退。项目契约 DLL 变化仍需要重新打开项目。

```text
help
help --json
help graph nodes
help graph.node.create --json
help --search group --json
```

`help` 无需项目或契约 DLL。完整机器描述与实际路由共用操作注册表，包含 JSON 输入 schema、结果协议、参数、默认值、枚举、能力边界及退出码。复杂 Editor 单元操作通过 `execute <project> <json-file|->` 调用，并可按 operationId 查询帮助。

普通 help 输出文字；其他结果及错误在 stdout 输出协议版本 1 的 JSON `{ protocolVersion, operationId, success, data, diagnostics }`。操作失败返回非零退出码。`help --search` 没有匹配时正常返回空列表。

项目概况和查询采用 `project info`、`graph nodes`、`graph node`、`group entries` 等分组入口。旧 commands、summary、create、nodes、node、entries 扁平命令已删除，不提供兼容别名。可调用的入口以 `help --json` 为准，尚未实现的阶段不提前公开。

execute 支持文档操作批次；独立资源文件操作一次只执行一条，不可与文档操作混合。文档 dry-run 在 JSON 的 `dryRun` 设置，文件操作暂不支持 dry-run，传入时在修改文件前明确拒绝。

```text
project create <directory> --name "Display Name" --contracts <built-contract-directory>
project create <directory> --contracts <built-contract-directory> --dry-run
project create <directory>
```

创建只生成带稳定 ID 的 Entry、空 Opening Group 和基础目录；不虚构可运行剧情。--contracts 接收已经构建好的目录，校验固定入口 DLL 和依赖，再完整复制到新项目 Contracts/。省略该选项时创建空骨架和部署说明，基础查询/结构校验可用，语义校验/编译/Editor 打开仍要求部署契约。目标必须不存在或为空，拒绝链接路径；dry-run 校验契约并返回文件清单，不创建目标或缺失的父目录。结果明确 contractReady 和契约指纹。

Editor 的新建面板调用同一创建服务；未指定自定义契约目录时使用随程序分发的 DefaultContracts，用户无需准备 DLL。也可选择已构建的自定义契约目录。打开时读取 Graph 的显示名，目录名仅用于既有路径身份；已有项目仍加载自己的契约，不静默回退到默认契约。

原始磁盘查询已接入：`files`、`find`、`references`、`project info`、`graph show/nodes/node/edges`、`group list/show/entries` 和 `variables` 不需要契约 DLL，不补 ID、不丢未知字段、不写回。列表返回 total/offset/returned/hasMore/items/complete；可使用 --offset/--limit 显式分页。

```text
files <project> --glob "**/*.rawgalgroup" --kind rawgroup
find <project> <id> --kind entry --group <group-id>
group entries <project> --id <group-id> --type <type-id>
group show <project> --file Graph/groups/example.rawgalgroup
references <project> <id-or-name> --kind resource|node|group|entry|variable
schema list <project> --kind primitive|composite
schema show <project> <type-id>
```

Group 目标 --id 与 --file 必须二选一。files 默认排除输出、工具契约和构建目录；显式 --root 可查看对应目录。glob 支持 *、**、?，相对项目根匹配；不同过滤条件取交集，同一选项重复传入报错。目录/文件链接不跨越读取，跳过会返回诊断。无效 metadata 仍保留对应文件列表结果，并报告错误及不完整状态。

references 返回静态 JSON 候选引用和位置，完整性为 false；不能据此对自定义 C# 或运行表达式声称零引用。schema 查询才加载项目 Contracts DLL，返回选定集合、参数结构、默认值、约束及主 DLL SHA-256；描述不替代自定义 validator/Composite 的执行语义。

```text
validate <project> [--structure-only]
graph validate <project> [--file Other/graph.json] [--structure-only]
group validate <project> (--id <group-id> | --file Graph/groups/example.rawgalgroup) [--structure-only]
```

校验直接读取原始 JSON，不补 ID、不迁移、不写回。仅接受当前 Graph v2 / Raw Group v2，未知字段、重复 JSON 属性、缺失或重复 ID 均报错。structure-only 不加载 DLL，跳过条件语法、契约语义和文件引用检查。完整 Group 校验使用项目契约执行参数类型/required/options/自定义 validator 和 Composite 展开检查；无关 Group 不阻塞定向校验。Graph 检查拓扑、变量定义和源文件引用，不校验 Group 内容；条件只检查语法，不用默认变量值执行表达式。

结果列出 checkedFiles/checks/skippedChecks/semanticStatus/complete，complete 表示所请求检查是否完成，不代表所有可能的检查都执行过。全项目覆盖 Graph、所有作者 Raw Group（包括孤立文件）、资源 metadata 的 GUID/相邻文件和 settings JSON；资源载荷、settings/本地化语义、自定义代码引用和运行时表达式求值明确列为未检查。缺契约仍返回结构诊断及 contract.load，不回退内置集合。

```text
group compile <project> --id <group-id> [--dry-run]
group compile <project> --file Graph/groups/example.rawgalgroup --output Output/example.galgroup
```

单 Group 编译加载项目 DLL，并复用原始校验和 Core 编译器；其他 Group 的错误不会阻塞目标。按 ID 默认输出 Output/Graph/groups/<id>.galgroup；按文件只有唯一 Graph Group 归属时可用该默认值，否则必须指定输出。输出参数相对项目根解析，也可为绝对路径；项目内只允许写 Output/，拒绝链接路径和非 .galgroup 后缀。dry-run 仍执行校验/展开并返回数量、sourceMap、目标及契约 SHA256，不创建目录或文件。临时文件完成后发布，失败/取消保留旧产物，Graph 与 Raw 文件不改写。

build/export 使用项目 DLL 的同一 catalog，先校验原始 Graph 和目标 Group 再编译。共享 ProjectContentBuilder 和 GamePackageExporter 要求显式传入 catalog，没有内置默认值或兼容重载。缺契约或契约未声明的 entry 明确失败；部分组件不会自动补成内置全集。输出拒绝作者目录、Contracts、项目根/祖先和链接路径；导出的 galpak 包含编译内容，不包含工具 DLL。execute 已切换项目 catalog；其无损写入边界与新增生命周期命令在后续阶段完善。

execute 的文档加载和保存只接受当前格式：不补 ID、不迁移变量，不过滤未知参数。未知 Graph/Group/settings 字段、重复属性、缺稳定 ID 或无法无损转换的参数会阻止写入；原始查询仍可用于检查这些文件。空字符串参数保留。dryRun 使用同一待保存内容校验且不创建文件；加载后源文件或 settings 改变会要求重新加载。Graph、Group 和 settings 统一暂存后发布，后续发布失败会恢复此前改动并清理临时文件；不承诺进程崩溃或跨进程整体原子性。删除 Group 并删除 entries 时同步删除作者文件，保留 entries 时保留孤立文件。资产文件操作仍是单独执行，不能混入文档批次；它与 GUI 共用项目文件预检、metadata、引用检查及持久事务，执行前需要有效作者文档。CLI 不保留 GUI Undo 栈，未知引用不自动重写，详见 [项目文件操作](../../../docs/spec/editor-files.md)。

Editor 打开项目时也必须加载 Contracts DLL。ProjectSchemaSession 由项目 DI scope 持有，catalog、文档仓库、Graph 编辑、文件操作和保存协调器均按项目解析，entry 快捷键从该项目集合生成。关闭/切换项目释放契约上下文；契约或打开监听器失败时清理 scope，不能继续沿用旧项目 catalog。预览使用同一 catalog，按选定集合注册内置实现，并在启动前检查实际编译内容的指令。加载不会提供自定义 entry 的平台执行实现，缺少实现时明确失败。

```text
project rename <project> --name "New Display Name" [--dry-run]
group create <project> --name "New Group" [--id stable-id] [--x 0] [--y 0] [--dry-run]
group rename <project> --id <group-id> --name "New Name" [--dry-run]
group delete <project> --id <group-id> [--force] [--dry-run]
```

这些入口由 ProjectEditService 复用 Editor 的单元命令；project rename 只改变显示名，Group rename 保持节点 ID 和文件名。新 Group 未指定 ID 时生成稳定 ID 并在 createdNodeIds 返回。删除同步 Graph 和作者 Raw 文件；Graph 中关联边自动移除，其他静态 JSON 引用候选或扫描错误需要明确 --force，强制删除不修改这些外部引用。dry-run 返回 writtenFiles/deletedFiles/references，检查当前批次新增的引用，不发布文件；引用检查始终明确 referencesComplete=false。

execute 的 JSON 必须符合当前命令参数，未知字段、重复属性、缺少必需参数及不允许的 null 都直接拒绝。project.rename 的帮助同时给出 CLI inputSchema 和 execute 使用的 documentInputSchema，同一 operationId 不重复注册。单独资源文件操作直接调用文件服务，不要求 Graph 文档可以加载；文档操作仍要求无损加载并整体校验后保存。

不提供项目目录删除命令；编辑器最近项目列表仅支持移除记录。

审核阶段补充：共享 JSON 命令默认精确匹配 operationId 和字段名称；required 与构造参数是否有默认值一致，允许 null 与字段可省略是不同语义。CLI execute 参数错误返回 2，文档编辑失败返回 5。build/export 输出包含明确 data schema，导出失败诊断在 diagnostics 中。

资源原始工具读取 id 字段时与现行 AssetMetaCodec 的匹配规则一致，诊断 pointer 使用实际属性名称。按 GUID 值查找可接受不同输入拼写，结果 ID 保持 metadata 原文以供 provider/entry 复用；遇到歧义身份字段明确诊断。该行为不改写 metadata，也不做旧格式迁移。
