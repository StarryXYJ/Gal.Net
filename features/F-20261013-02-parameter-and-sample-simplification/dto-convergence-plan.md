# DTO 字段生成与绑定收敛

状态：Phase 2d/2e verified。2d 的 348 项验证见 [phase-2d.md](phase-2d.md)；2e 的强类型执行与文档直接绑定经 344 项回归及实际契约部署/CLI 编译通过，见 [phase-2e.md](phase-2e.md)。2026-10-14 用户确认；本文替代此前把源码生成器仅列为候选的决定。已验证的值工具、字段描述、SchemaForm、工厂和上下文继续复用，不另建平行表单框架。

## 目标和范围

开发者用 DTO 成员和特性声明字段，构建期生成有序 FieldDescriptor 与成员 getter/setter；面板通过公共 DTO 适配器取得描述并绑定成员。目标是替换手写字段投影和重复绑定，而不是在现有适配旁增加长期并存的模型。

生成代码只使用平台无关的字段和值工具。Roslyn 放在独立构建工具项目；特性与最小成员访问协议优先放在现有 Core，不新建运行时 Helper 项目。生成器不生成控件、执行 module、保存队列、撤销命令或业务逻辑。

DTO 可为普通 class。需要自动响应外部更新时使用 INotifyPropertyChanged，ObservableObject 为可选实现，不强制契约 DTO 依赖 CommunityToolkit。无通知 DTO 支持显式刷新。异步应用与撤销通过宿主提交回调，未完成草稿继续由 FieldEditorContext 持有。

## 实施顺序

Phase 2c 先处理已有失败并验证最小宿主接入，不继续增加字段类型包装或扩大迁移。随后新增 Phase 2d，先以一个 Entry DTO 完成生成、面板编辑、撤销与保存，再推进 Phase 3 的组合入口和 Phase 4 的作者路径。Phase 5 依据已实现生成器优化体验，不重复从零评估是否采用生成器；Analyzer、模板和其他自动化仍按收益选择。

顺序修订为：0 → 1 → 2a → 2b → 2c（最小收尾）→ 2d → 2e → 3 → 4 → 4b → 5 → 6。各实现 Phase 验证后独立提交。Phase 2c 的最小收尾已通过 241 项回归验证，见 [phase-2c.md](phase-2c.md)。Phase 2d 的单 Entry 垂直试点及 Phase 2e 的执行/文档绑定收敛已验证，后续扩展仍按收益推进。

## Phase 2d：单个 DTO 的生成与公共绑定垂直试点

依赖：Phase 2c 最小接入验证；相对工作量：中。涉及 Core 字段/值协议、独立源码生成器、Avalonia.Controls 公共适配器及一个真实 Entry 的契约/Editor 消费。

任务：

- 用成员 CLR 类型、JSON 元数据和特性作为唯一字段来源，生成有序 schema 与成员访问表；序列化和校验调用现有 ValueCodec/ValueValidation，不复制规则。具体特性和类型名称经原型验证后确定。
- 默认值只支持可证明的声明来源；不执行任意构造函数/属性初始化表达式来推断 schema。required、允许省略和允许 null 分别表达；DTO CLR 默认值不能抹掉作者 JSON 的 presence。
- 先明确支持的公开可读写属性。验证 ObservableProperty 与自有生成器的协作；生成器不能假定可以读取另一生成器的输出。若支持字段特性转发，必须验证属性名及通知语义，否则给出明确诊断和手写 observable 属性路径。
- 实现一个公共 DTO 绑定适配器，消费生成的描述/访问表、变化通知及可选宿主提交回调，不给每种参数或 DTO 增加专用 ViewModel。宿主仍拥有数据、撤销和 async apply/save；控件拥有草稿、订阅和租约。
- Entry JSON 到 DTO 的适配保留省略/null/默认及自定义 converter，撤销/恢复从正确数据源刷新。不得再维护一份独立参数表；业务跨字段校验仍显式接入。
- 通用字段区域以一个表单承载整组字段；共享基础结构模板，主题按实际需要覆盖布局。资源、表达式、Action/Custom 等专属行为先保留，不强制全部设置 DTO 化。
- 替换试点旧绑定/字段投影并删除无消费者代码。不得同时长期维护手写 schema、生成 schema 和第三份 UI 描述。

验证：

- DTO 声明变化能增量更新生成物；不支持的成员/默认值、重复 ID/顺序等有可定位诊断。
- 代表性数字、文本、bool、选项、JSON converter、默认值、省略/null 和字段顺序从生成物到实际 JSON 一致；DTO 实例拥有独立值与绑定。
- 真实面板输入、无效草稿、外部通知、撤销/重做和保存；无通知 DTO 显式刷新；卸载/换对象退订及取消，旧回调不能写入新对象。
- schema DLL 独立部署/加载，游戏直接项目引用；Core/契约无 Avalonia 和运行时 Roslyn 依赖。不能用生成 getter/setter 的镜像比较替代实际编辑/保存验证。

退出与收益：一个真实 Entry 只维护 DTO 字段事实，编辑/撤销/保存可用；试点旧重复路径已删除。记录作者维护点、生产手写代码、生成器/适配器/测试成本及新增概念，明确哪些减少、哪些增加。达成后才迁移下一类 DTO；收益不足先缩小支持范围，不通过铺开迁移掩盖成本。

文档：通过后更新正式开发示例；当前仅作为计划，不把 DTO 自动绑定写成已实现能力。

## 后续阶段调整

### Phase 2e：执行强类型与文档绑定收敛（2026-10-14）

状态：verified，证据见 [phase-2e.md](phase-2e.md)。用户确认整体设计后授权实施；插入 Phase 3 前，替代 Phase 2d 试点中 Editor 的 DTO 副本路径。

- 执行工厂增加 `PrimitiveImplementation.For<T>`，用现有规范化结果与逐字段 ValueCodec 创建每次调用独立 DTO；注册时检查契约 DTO 类型。保留普通 JSON 工厂，不生成执行或生命周期代码。
- 契约只记录 DTO 类型，不再提供无消费者的数据工厂；Editor 消费字段列表直接绑定 Parameters，删除 JSON 到 DTO 的同步逻辑。文档仍拥有历史/保存，SchemaForm 和 IFieldBinding 不改变。
- 验证强类型工厂的默认值、converter、null/省略、校验失败、实例隔离、批次/取消与契约不匹配；真实编辑器验证输入、撤销/重做、保存、换 schema 后旧绑定不可提交及非法作者值可显示而不改写。
- 本阶段不全面替换 EntryParameterType，不统一设置/变量存储，不扩展生成器、不迁移特殊控件；Sample 集中入口仍由 Phase 3 处理。

- Phase 3：集中 schema/module/工厂注册入口，复用 Phase 2d 的 DTO 适配，不新增另一套成员绑定服务。
- Phase 4：已验证；同步 Immediate / Immediate<T>、内置轨道/Wait DTO 与复杂路径验证见 [phase-4.md](phase-4.md)。307 个不重复用例通过，迁移前后五条指令 CLI 描述相同；后续转 Phase 4b。
- Phase 5：完善生成器诊断、JSON 元数据覆盖、开发示例和作者体验；不预设生成 module/执行/持久化，避免把整个业务流程变成生成框架。

## 暂不扩展

全量设置 DTO 化、所有参数控件重写、递归嵌套表单、自动 module 扫描、自动撤销/持久化生成、复杂默认表达式解释及 Core 领域重构。本次不增加这些工作。
