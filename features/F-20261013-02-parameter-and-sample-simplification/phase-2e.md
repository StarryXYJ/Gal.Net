# Phase 2e：强类型执行与文档直接绑定

状态：verified，2026-10-14。此阶段插入 Phase 3 前，落实用户确认的执行 DTO 和编辑绑定分离；不代表整个 feature 完成。

## 职责与改动

- Core 的 ForDto<T> 仍从唯一 T.Schema 建立参数表；FieldDataType 记录 DTO 身份，替代不再需要的 FieldDataFactory。Schema 发现和 Editor 均不执行 DTO 构造函数。
- PrimitiveImplementation.For<T> 是现有工厂的适配入口，不增加泛型 Context、新运行时接口或绑定服务。CompositeGameView 构建时检查契约与执行 DTO 类型；Dispatch 继续负责规范化/校验，适配器逐字段使用 ValueCodec 与生成的成员访问创建独立 DTO。Context 保留 Runtime、BatchId、ScopeCancellation 及规范化 Arguments。
- SampleScreenshotModule 直接读取 ScreenshotOptions.FileName，不再手写 JSON 字段名和转换；完成、取消、CatchUp 和平台资源所有权不变。
- EntryParameterFormBinding 是 Editor 内的文档适配器：Read 从 Parameters 解析，Commit 调用已有文档提交，变更通知跟随 Parameters。表单只消费 FieldDescriptor/IFieldBinding，不构造 DTO。原 DTO 副本、刷新循环和同步逻辑删除；DtoBinding 删除无消费者的 isProvided 回调，保留普通成员编辑、通知和 async 宿主提交。
- 文档仍唯一负责作者值、历史与保存。字段提交校验当前候选及契约自定义规则；其他可解析但无效的值、缺失必填字段不阻止逐步修正。完整文档必填/类型检查留给编译、执行与项目校验；不得对每次字段提交执行整份参数 Normalize。

## 验证

| 测试范围 | 通过 |
|---|---:|
| Presentation（含强类型执行、DTO 与表单） | 163 |
| Editor（含真实控件、历史和保存） | 40 |
| Core | 91 |
| Architecture | 36 |
| Screenshot 实际流程 | 14 |
| 合计 | 344 |

证据：忽略目录 Temp/F-20261013-02/phase2e-{presentation,editor,core,architecture,screenshot}.trx。新增测试验证 converter、默认值、显式 null、DTO 实例隔离、运行时/批次/取消传递、无效参数不进入工厂、错误 DTO 契约在启动时拒绝。真实 EntryListEditorControl 的输入/撤销/重做/保存回归继续通过。

文档适配验证打开面板零 DTO 构造、不生成旧逐字段投影、读取非法类型值用于修正、省略与 null 区分、默认值不写回、自定义业务拒绝不污染文档、旧绑定不可提交。初次新增测试因遗漏 StableId 在保存入口失败，补齐真实文档身份后通过；不修改保存约束。首次受限沙箱 testhost 90 秒连接超时，同一构建在沙箱外运行通过；仅记录观察，不推断产品问题。

改动文件 whitespace 与 IDE0005 检查通过。示例契约重新独立部署至 GameTestCase/Contracts，CLI 重新构建；schema show sample.screenshot 成功，project.build 成功编译 4 个 Galgroup，diagnostics=[]。未进行全解决方案测试或原生媒体人工验证。

## 成本与边界

9 个生产 C#/XAML 文件改动净增 50 行；没有新增生产项目、文件、接口或 Context 类型。新增的是一个强类型工厂入口和一个文档绑定类，删除了 DTO 工厂与双份数据同步。此次收益主要是减少作者解析和同步职责，不宣称总代码量下降。

DTO 缺失且没有 schema 默认的成员保持新实例值；业务若需要判断 presence，应查询规范化 Arguments，其中已包含默认值，并非原始作者 JSON。普通 JSON 工厂仍支持手工动态契约。旧字符串作者文档无法无损表示的值（如显式 null 字符串）保持明确拒绝。

EntryParameterType 全面替换继续暂缓。Galgroup 自身属性的新面板、特殊资源/表达式控件迁移、Sample 集中入口、全量设置 DTO 化均未在此阶段扩张；下一阶段仍为 Phase 3 的 Sample 组合与服务整理。正式事实同步至 development.md、spec/control.md 和 spec/entry-types.md。
