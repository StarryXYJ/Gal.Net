# 项目工具 CLI

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

Editor 打开项目时也必须加载 Contracts DLL。ProjectSchemaSession 由项目 DI scope 持有，catalog、文档仓库、Graph 编辑、文件操作和保存协调器均按项目解析，entry 快捷键从该项目集合生成。关闭/切换项目释放契约上下文；契约或打开监听器失败时清理 scope，不能继续沿用旧项目 catalog。加载不会提供自定义 entry 的平台执行实现，预览执行覆盖在后续接入。
