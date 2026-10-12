# 面向 agent 的 CLI 与共享操作契约

状态：2026-10-13 需求复核后的目标设计；除已存在入口外均不代表已实现。用户要求重新 review 命令面，补充 help、定向校验、单 Group 编译和文件筛选；该修订适用于后续 Phase。

## 命令发现

```text
help
help <command-path>
help --json
help <command-path> --json
help --search <keyword> --json
```

help 是唯一正式命令发现入口；原有 commands 保留为 help --json 的兼容别名。普通 help 返回简明分组列表，具体命令返回用法、参数、约束、示例和退出码。--json 返回同一操作定义的机器描述；不要求打开项目。--search 在稳定 commandId、命令路径、关键词和描述中检索，匹配结果包含完整参数描述，不限于命令名。采用明确选项 --search，而不将任意 -关键词解释成合法 flag。无匹配返回空列表及匹配计数；未知命令路径返回用法错误。

每项描述包含稳定 operationId、CLI commandPath、说明、只读/写入属性、参数 JSON 类型与 required/default/enum/约束、输入输出结构、示例、退出码、项目/契约依赖、dry-run 和批次支持边界。以操作注册表为单一来源，CLI help/路由和 Editor 可绑定操作消费同一份定义；Editor 的强类型操作接口不依赖命令行字符串。复杂输入的机器描述使用 JSON 结构，required 判定考虑 nullable 引用类型，不只看构造函数默认值。不输出内部 CLR 类型名冒充 JSON schema。

## 本期命令面

表中嵌套路径是统一的新主入口；已存在的扁平命令保留兼容别名，帮助信息标注对应主入口。所有项目参数显式传入，不依赖当前工作目录猜测项目。

| 命令族 | 主入口 | 用途 |
| --- | --- | --- |
| 发现 | help | 全量/定向帮助、关键词检索、完整 JSON 描述 |
| 项目 | project info/create/rename/delete | 概况、创建、显示名改名、系统回收站删除；目录搬迁不纳入本期 |
| 文件 | files | 按 root/glob/extension/kind 等条件列文件，返回相对路径、种类、大小及适用的身份信息 |
| 身份 | find | 按稳定 ID 查询资源、节点、条目全部匹配，可限定 kind/group |
| 引用 | references | 查询对象引用来源和位置，供删除/改名前评估影响 |
| 契约 | schema list/show | 当前项目的 entry 集合、参数、默认值、约束、Primitive/Composite 及契约来源 |
| 图查询 | graph show/nodes/node/edges | 原始图、筛选节点、单节点、连接；保留未知数据 |
| 图校验 | graph validate | 校验版本、字段、节点/边 ID、根节点、端口和连接不变量，以及 Group 文件引用 |
| Group 查询 | group list/show/entries | 列 Group（含未被 Graph 引用的源文件标识）、原始文档、条目筛选（ID/type） |
| Group 校验 | group validate | 单个源文件或 Group ID 的结构、schema、参数、条件及 Composite 展开校验 |
| Group 编译 | group compile | 单个 Raw Group → Compiled Group，不触发整个项目 build/export |
| 项目校验 | validate | 全项目检查，聚合 Graph/Group/schema/引用及已有项目规则 |
| 产物 | build/export | 全项目编译、galpak 导出，使用同一项目 catalog |
| 基础写操作 | group create/rename/delete | Graph 节点、Group 源文件和引用影响同步处理 |
| 变量查询 | variables | 列变量定义，可按 scope/name 筛选；复用现有定义 |
| 写操作适配 | execute | 从文件或标准输入接收单个操作/支持范围内的批次，调用共享操作层 |

project rename 的语义只改变显示名。Group rename 改显示名，不改变稳定 ID、文件名或目录；目录/文件重命名另按资源/文件操作处理，不混用身份语义。group create/rename/delete 和 graph.node.create/rename/delete 对 Group 节点应走同一共享生命周期规则，不能形成第二份持久化实现。

### 定向校验与单文件编译

```text
graph validate <project> [--file <relative-graph-path>] [--structure-only]
group validate <project> (--id <group-id> | --file <relative-source-path>) [--structure-only]
group compile <project> (--id <group-id> | --file <relative-source-path>) [--output <path>] [--dry-run]
validate <project> [--structure-only]
```

--id 与 --file 必须二选一，不根据字符串外观猜测。默认 Graph 是 Graph/graph.json；--file 目标位于项目范围，相关相对路径基于目标图所在目录解析并检查仍在项目内。单 Group 校验无需其他 Group 通过，也可校验未被图引用的作者源；诊断明确检查范围。结构校验不要求契约，完整 Group 语义校验/编译必须有项目契约，失败不能静默降级。Graph 校验不伪造它没有执行的全部 Group 参数/跨项目校验，--structure-only 不遍历所引用 Group 的内容。

group compile 只编译目标并验证输出 primitive 均在选定 catalog。按 ID 编译时默认输出 Output/Graph/groups/<id>.galgroup；独立 --file 未对应唯一 Group 时要求显式 --output，避免猜测文件归属。输出不得覆盖 Raw/Graph/资源作者源；临时写入成功后再发布，失败保留旧产物，dry-run 返回输出目标与编译摘要而不写文件。不会修改 Graph 或重建其他 Group。

### 文件和身份查询

```text
files <project> [--root <relative-directory>] [--glob <pattern>] [--extension <extension>] [--kind <kind>]
find <project> <id> [--kind resource|node|entry] [--group <group-id>]
references <project> <id-or-name> --kind resource|node|group|entry|variable
```

files 默认递归扫描作者文件范围，排除 Output/Temp/.galnet、构建中间目录和默认工具契约输入；显式 --root 可检查 Contracts。多个不同条件取交集；重复同类过滤条件的组合规则在参数描述中说明，不偷偷解释成另一种含义。默认不跨目录链接/reparse point 逃离项目；跳过或失败进入诊断。文件路径规范化后排序，metadata 无效不能使对应文件悄悄消失；资源 GUID 依赖有效 .meta，未识别/未索引文件仍可列出。

find 返回所有匹配和所属文件/Group/对象路径；不假定 entry ID 跨 Group 唯一。基础读取无需 schema，不通过会生成 ID 或丢弃未知字段的 repository。查询选项先保持 ID/type/kind/路径等静态匹配，不执行游戏条件表达式，不引入任意表达式查询语言。

references 返回已识别引用及提取能力/未能分析的诊断；自定义 validator/Composite/C# 内部逻辑不一定可静态推断。不得因零匹配就对未知引用范围宣称安全删除。写操作复用相同引用分析，必要时拒绝不能证明安全的修改，并精确说明检查范围。

## 保留的 Editor 单元操作

现有稳定 operationId 继续存在，通过 execute 和 help JSON 暴露；CLI 不需要为每个动作再写一套业务逻辑。

- graph.node：create/delete/rename/move/setRoot；graph.edge：connect/delete。
- group.entry：add/delete/move/setType/setCondition/setParameters/patchParameters。
- branch.option：add/delete/move/setText/setCondition；branch.condition：add/delete/move/setExpression。
- variable.definition：add/delete/move/rename/setType/setDefault。
- project.rename、project.settings.patch。
- asset.import、asset.directory.create、asset.move/rename/delete、asset.meta.patch。

大型/嵌套参数通过 JSON 文件或 stdin 输入，避免 shell 转义影响值。查询/校验/编译也是独立强类型操作，未来 Editor 可直接调用共享服务或操作适配器，不必运行外部 CLI。

## Agent 输出与执行边界

除普通文本 help 外，默认 stdout 是一个完整 JSON 结果，日志/进度进入 stderr。统一结果含协议版本、operationId、success、data、diagnostics；诊断含 code/severity/message/file/objectId/JSON Pointer，JSON 解析失败可补行列。结果明确 scope、检查完成情况、契约来源及指纹；没有执行的检查不计为通过。失败同样返回 JSON，并以非零退出码表意。

列表默认返回全部；--offset/--limit 分页时返回 total/offset/returned/hasMore，不暗中截断。排序确定，参数冲突与未知 flag 明确失败。操作定义描述支持的 dry-run：只计算目标/影响、不写文件；只读操作无需 dry-run。execute 的内存文档批次与独立文件操作沿用实际支持范围，不承诺任意混合批次或跨文件原子性。

直接写文件后执行 validate 是首要 agent 工作流，不默认自动修复、补 ID、重排文档或删除未知字段。契约 DLL 缺失时 help、files、基础查询及结构校验仍可用；schema/语义校验/编译必须明确失败。

## 本次 review 的取舍

新增 help 契约、files、references、定向校验与单 Group 编译；保留查询和单元写操作。资源内容搜索、文本/本地化批量修复、目录搬迁、常驻 watch/server、任意跨文件事务、运行预览/截图 CLI、自动修复命令暂不加入本期。截图仍是平台游戏 entry 示例。后续实现按 phase-plan 分段验证提交，不将此设计当作已经交付。
