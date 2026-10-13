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

项目原始读取校验、项目契约贯穿执行路径和新增写操作按 feature 后续阶段接入，不把现有加载式 validate 当作完整磁盘校验。
