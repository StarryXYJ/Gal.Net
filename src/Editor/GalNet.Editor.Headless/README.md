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

当前阶段完成的是命令发现与结果协议；项目原始读取校验、项目契约贯穿执行路径和新增操作按 feature 后续阶段接入，不把现有加载式 validate 当作完整磁盘校验。
