# P6b：内容会话与磁盘基线职责

## 所有权与同步边界

| 所有者 | 状态与职责 | 同步边界 |
| --- | --- | --- |
| ProjectContentSession | 工作文档、文件身份、发布/重载/补偿顺序、错误与通知、关闭 | 原有 history capture guard 和 `_saveGate` |
| ProjectContentBaseline | 已保存作者快照及完整字段比较；发布中的捕获内容用于识别晚到修改 | 会话捕获与更新；不改用历史游标推断 dirty |
| ProjectDiskBaseline（新增内部类） | 已接受的外部文件字节、settings 检查字节、打开时契约指纹；登记、比较、重载与成功发布后的更新 | 由会话按原有顺序调用，无自有锁、任务或会话反向引用 |
| EditorDocumentRepository | Graph/Group 原始源字节、作者计划、事务发布与源文件校验 | 原有发布事务；会话在重载时显式采纳新源 |
| ProjectDraftRegistry | 草稿提交、取消与未完成输入 | 会话按保存/重载流程调用 |
| ProjectRecoveryWriter | 脱离工作版的快照持久化、排空与后台错误 | 沿用 P4c 串行 worker；会话拥有捕获、定时请求与关闭 |

历史捕获门先于发布门；等待发布时释放历史租约，保持结构编辑所需的锁顺序。Save All 捕获/准备后释放历史门，后台只发布分离字节；成功只推进本次捕获版本。重载与结构发布保留原有同步安装、文档身份、输入过渡及失败补偿，不提取回调式“发布控制器”。

## 实现与取舍

会话中分散的 settings 检查、外部字节比较与写入/复制/删除采纳规则，集中到 ProjectDiskBaseline。会话只决定何时检查、何时接受；协作者负责如何比较和更新。初始化仍以磁盘扫描覆盖范围，并以 Repository 已加载的源字节覆盖对应 Graph/Group，避免把尚未采纳的源变化误认成当前版本。初始磁盘读取集中在协作者构造中，早于创建本地化工作文档和作者基线。

作者基线与磁盘基线保持不同职责：前者按完整内容判断 dirty，后者按原始字节识别外部修改，不能将 JSON 空白差异等同于作者修改。Repository 的事务源校验及受管文件的预期 hash 也保留原有来源，不新建第二套权威状态。路径大小写比较及文件筛选沿用原规则。

外部采纳和文件登记复制输入字节；发布采纳复制计划字节，并读取文件复制/写入计划实际落盘结果。settings 的保存校验字节与整批外部观察字节仍分开存储：结构安装失败后，补偿可重新序列化 settings，原实现仅推进 settings 校验，未接受失败结构计划的外部观察结果。本阶段明确保留这一顺序，没有将补偿伪装成成功发布。

没有新增公共 API、依赖注入注册、锁、后台任务或跨对象回调。新类增加少量封装代码，目的是将状态与更新策略放在同一所有者中；不以总行数减少或性能提升作为结果。没有修改正式 docs，也不扩展到 10 GB 包/流式处理。

## 验证

新增 5 项公共会话行为测试，在重构前运行 ExternalProjectReloadTests，13/13 通过：

- 普通受管文本与本地化文本保存后，直接修改工作数组仍 dirty；自有发布返回 Unchanged，保存快照保持原字节（2 项）。
- 外部快照采纳后，调用方修改文本和 settings 字节不污染基线，后续读取与保存仍正常。
- settings 仅空白变化也阻止 Save All；拒绝保存不推进基线，本地修改被保留，随后可正常采纳外部版本。
- 真实 ProjectFileOperation 创建、撤销、重做本地化文件，均不误触外部重载，且不顺带保存无关作者改动。

重构后 Editor.Shared 全量 613/613、Architecture 53/53 通过，退出码均为 0。包含保存期间继续编辑、结构安装失败补偿、事务回滚失败封锁、恢复快照、关闭排空、逐字段完整性与外部契约变化等既有回归。修改文件没有新增编译/分析器警告；其他文件既有警告保留。

界面层：ExternalProjectHotReloadTests.cs 与 ProjectFileLifecycleTests.cs 内全部 20 项测试通过，退出码 0。覆盖真实 FileSystemWatcher 通知、挂载 Group 的页面/选择/scope 保持、设置/文本输入冲突与明确丢弃、文件改名/导入/删除/撤销/重做以及发布失败的历史恢复。

命令统一使用 `--no-restore -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false`。界面测试设置 `AVALONIA_TELEMETRY_OPTOUT=1`，按两个源文件的方法名构造 OR filter。证据：

- `out/project-quality-p6b-before.log`
- `out/project-quality-p6b-editor.log`
- `out/project-quality-p6b-architecture.log`
- `out/project-quality-p6b-ui.log`、`out/project-quality-p6b-ui-filter.txt`

未运行原生窗口人工验收、跨进程并发压力或性能基准；本阶段只确认职责拆分及所列行为回归。
