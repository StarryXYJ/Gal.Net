# P5：作者内容比较与克隆契约

## 最终改动

测试从实际序列化文档生成单字段变更用例，验证 dirty、还原后 clean、序列化往返及克隆保真；另用序列化元数据检查夹具填充了所有可选字段和集合，避免新增字段默认 null/空集合而没有进入用例。新字段会自动获得测试，或明确要求补充夹具/对应数据变更规则。测试辅助逻辑集中在 ProjectContentContractTests，不进入生产调用路径。

覆盖 Graph、节点/边、选项/条件、两个变量作用域、Entry 参数、配置节和受管理文件；明确显示行号、已删除文件的旧载荷、映射插入顺序不影响 dirty。序列顺序、集合成员/键、未知 JSON 的 null/缺失、类型、数组顺序和大整数精度均有验证。配置 envelope 版本属于格式门禁；变量类型与载荷有四种类型的独立测试，不能当作任意独立 JSON 数值修改。

六类局部发布测试分别修改 Graph、StoryPackage、Entry 未知参数、变量说明、未发布配置节和文本字节，再仅发布另一配置节；断言 working/saved 基线分别保留、仍 dirty，Graph/Group/文本磁盘不被顺带保存，settings 只包含已发布内容。克隆容器隔离测试覆盖嵌套列表、参数字典、变量外壳、文件数组与配置节。

唯一生产修正：ProjectVariableDefinition.Clone 复用 Variable.Clone，原样保留声明及默认值的名称，不在快照内做隐式规范化。VariableValue 是封闭不可变值，可共享；可变 Variable 外壳仍独立。删除重复 CloneValue 分支，生产代码净减少 16 行。dirty 比较与其他运行时机制保持原样；不增加反射、generator 或全量序列化成本。

## 发现与边界

原克隆把 DefaultValue.Name 改为声明 Name，导致四个单字段克隆测试失败；两项真实保存实验确认名称不一致时旧代码保存成功但仍 dirty。深入检查发现作者校验本应拒绝名称不一致，旧克隆的规范化绕过了此校验。

最终行为是：克隆保留无效草稿，保存明确拒绝、磁盘不变、恢复文件保留准确的两个名称；显式修正后保存成功且 clean。正常 GUI/CLI 的重命名入口已有名称同步，本阶段没有改变重命名策略。初始实验中“允许不一致名称保存后 clean”的假设已纠正，没有放宽作者校验。相关经验见 `.agents/lessons/L-20261030-08-content-clone-normalization.md`。

## 验证证据

- 首轮契约：66 项中 4 项克隆保真失败，62 项通过；初始保存实验 2 项均复现成功返回后仍 dirty。
- 修正并补齐最终断言后的定向检查：76 项通过；随后增加集合顺序与键规则，最终新增合计 87 项测试。
- 反向验证一：临时给 EditorGraphNodeDto 添加带 WhenWritingNull 的可选 ContractProbe，夹具覆盖测试按预期失败，指向未填充字段。
- 反向验证二：临时移除 StoryPackage 的比较，自动生成的该字段用例按预期失败。两个探针都已撤销，没有进入最终生产代码。
- Editor.Shared 全量 608 项、Core 全量 139 项、Architecture 53 项通过。原有 dirty 分配上限回归仍通过，本阶段不宣称性能提升，也不重复运行 BenchmarkDotNet。
- 变量面板定向 4 项通过：重命名及历史、表达式引用、无效输入/冲突/补偿、挂载控件与作用域切换。使用 Avalonia Headless，设置 `AVALONIA_TELEMETRY_OPTOUT=1`；日志 `out/project-quality-p5-variable-ui.log`，其中未闭合字符串解析错误是非法输入用例的预期输出。未做原生窗口手工验收。

采用 Release、`--no-restore -m:1 -nr:false -p:UseSharedCompilation=false`。日志保存在忽略目录 out：`project-quality-p5-contract-before.log`、`project-quality-p5-save-before.log`、`project-quality-p5-contract-fixed.log`（校验规则揭示的中间失败）、`project-quality-p5-contract-final.log`、`project-quality-p5-new-field-probe.log`、`project-quality-p5-comparison-probe.log`、`project-quality-p5-editor.log`、`project-quality-p5-core.log`、`project-quality-p5-architecture.log`。

契约夹具包含用于检查比较/克隆的合成值，不承诺每个逐字段变更都是可发布的合法作者工程；实际发布边界由真实保存/局部发布测试验证。自定义 JSON 转换器新增载荷字段后仍应扩展实际 wire 夹具；本阶段不把任意私有状态计为作者内容。正式 docs 同步仍按用户要求留后。
