# 全量 Entry options DTO 化评估

日期：2026-10-14。状态：用户于 2026-10-14 确认开始实施。基于 Phase 4 后的代码及实际项目 CLI schema 清单；不是已完成能力或工期承诺。

## 结论与规模

可行，属于中等偏大的跨契约/编译/执行迁移。已有 generator、ForDto<T>、For<T>、Immediate<T> 和 Editor 文档绑定可以复用，主要补齐对象默认值、选项元数据与 Composite 适配，不需要新的宿主或参数框架。

当前项目 43 条 Entry（42 内置 + 截图）：5 条无参数、6 条已用 DTO（四种轨道操作 + wait + 截图）。剩余 32 条有参数 Entry：11 条简单 Primitive、13 条复杂 Primitive、8 条 Composite。剩余 166 个参数出现位置（Primitive 85、Composite 81），包含跨 Entry 复用，不是 166 个独立字段；其中 96 个默认值位置、23 个 choice 提示位置、17 个 JSON 提示位置。按当前完整参数描述去重后是 29 套 schema，可估计新增约 25–30 个 DTO；具体共用只以语义一致为准，不按相似名字强行合并。

无参数 5 条保持空 schema + Immediate，无须制造空 DTO。效果/seffect 的开放 parameters 可作为外层 DTO 的 JsonElement 字段，不要求把插件定义的全部内层数据 DTO 化。

## 当前缺口与边界

1. generator 只接受常量属性初始化值；new LayerTransform()、数组和 JSON 对象默认值不能直接生成。需要显式声明 JSON 默认值或受限、可验证的默认元数据，让 schema 默认和每次独立反序列化继续只有一个来源，不能通过任意执行构造函数推断。
2. Field 当前没有通用 options / enum 约束声明。部分选项来自 Layer/Particle 的公共属性目录，部分是固定字符串列表，需要能组合到同一 schema 的元数据扩展。选择标签、编辑方式和合法值仍分开。
3. 现有部分 bool 在内容中编码为字符串 true/false，枚举存在大小写/命名差异。若保留 JSON 格式，不能只把成员改为 bool/enum；必须验证 codec/converter、valueSchema、编辑文本和旧内容的一致性。也可暂时保留 string，但这样只统一外层 DTO，无法获得全部类型安全收益。
4. Composite 当前从 Entry.Values 的作者字符串字典展开，只有 primitive 有 ForDto/For 工厂。需要轻量 Composite DTO 描述/读取适配，保持纯编译期业务、primitive 检查及 batchId/condition/reading-boundary 传播。不得把 Composite 变成 Runtime 指令。
5. animation.from 等字段需要区分省略和显式 null；CLR nullable、JSON AllowNull 和 required/default 仍需各自明确。batchId 当前含编译信封语义，不能作为普通 DTO 字段机械搬迁。
6. 动画计划事件及存档重放另有 JSON/字符串字典转换路径。Entry options DTO 化不能自动删除这些路径；若要共用，应复用值 codec/DTO 读取而不改变存档格式、SceneState 或对象所有权。

generator 当前也不自动合并继承属性。首选平坦 DTO 与已证明相同的整组 DTO 共用，不为减少文件数引入复杂继承/DSL。

## 工作量估计

建议拆为 4–5 个独立验证批次：公共默认/选项/编码支持 → 简单 Primitive → 复杂 Primitive（可再分媒体与场景）→ Composite 及统一回归。最重的是复杂参数与 Composite；11 条简单 Primitive 大多是声明/工厂迁移，基础框架已经存在。

如果只要求所有 schema 由 DTO 生成、但执行和 Composite 仍手写字符串读取，工作量较低，也能去掉静态参数表；收益有限，仍维护双份字段名/转换。推荐完整做到生成 schema + 类型化执行/展开，保持业务验证与生命周期显式。

## 可简化多少

- 迁移范围内的 ParameterTypes / DefaultValues / ParameterOptions 多套手写表由 DTO 成员和元数据取代；字段类型/default/constraints 的维护来源从分散的 2–3 处收敛为 1 处。
- 当前内置执行有 76 次 Arguments.String/Float/Int/Bool/Enum/Json/RawJson/TryFloat 调用。预计大部分可变成属性访问，消除约 60–75 处手写字符串读取/转换；这是估计，开放 JSON/重放边界可能保留部分调用。
- transition Composite 有 46 次 TransitionEntrySupport 读取调用；接入 typed options 后可删除大部分读取/解析辅助，Voice Composite 的类似读取也能收敛。生成 AnimationPlan、业务校验、状态更新与重放不会因此消失。
- 更直接的收益是新增/修改参数时不再同步 schema 表、默认表和执行 reader；重命名/类型变更可以由编译器发现。
- 不预计全仓行数大幅减少：现有参数表很多压在长单行内，DTO 会展开为可读属性，并增加必要元数据/测试。Phase 4 的小范围迁移已经净增 41 行生产文件，同时消除了真实维护点；全量工作的目标应是维护成本与可读性，而非行数百分比。

## 候选验收

逐模块对照迁移前后的 CLI 描述及实际编译/执行；覆盖默认、null/省略、枚举/字符串布尔、对象/数组、JSON converter、业务校验、Composite 展开和信封传播。独立 Contracts 部署、工具加载、Sample 静态引用与运行时无 Roslyn 依赖继续验证。确认剩余 reader 的消费者后再删除工具，不把快照/开放数据的解析误判成无消费者旧代码。

来源：Temp/F-20261013-02/dto-assessment-schema.json（实际 CLI 清单）；BuiltinSchemas/Entry 定义、FieldSchemaGenerator、PrimitiveImplementation、GalgroupCompiler、BuiltinRuntimeActions 的请求/计划事件/参数路径。当前没有实现阻碍，但上述公共能力需要先补齐。建议独立列为全量 DTO 收敛范围，不混入已计划的 Phase 4b 异步清理。

## 执行实现与实例的进一步候选

用户进一步询问代码量优化。DTO 的成员访问、唯一参数事实和编译期类型检查本身就是简化；执行层仍有以下独立收益点，优先实施 DTO、简单异步实例及模块任务 scope；其余按实际净收益推进。

1. **删除 primitive 契约类壳与类型取 ID 工厂。** 全量 DTO 后，多数旧 PrimitiveEntry 派生类只剩 TypeId 与 Type override，而 catalog 已创建通用 AuthoringPrimitiveEntry。可按领域维护指令 ID 常量，直接 ForDto/For/Immediate 注册，删除仅为取 Type 而 new TEntry 的 BuiltinInstructionFactory。Composite 的 Compile 类仍有实际业务，保留。迁移前需查全所有直接构造/反射消费者，不能因只看到常量就直接删类。
2. **简单异步展示复用完成机制。** Effect 两个实例、Particle 三个实例合计 200 行，均非阻塞/不可跳过，重复保存任务、观察 await、匹配 scope 取消、警告及完成。可试点一个小型公共异步实例/工厂，业务显式写状态与调用 presenter，CatchUp 跳过展示；不再为每条简单展示操作维护实例类。候选净删几十至约一百行，需把新增公共代码计入。截图错误会传播，Effect/Particle 当前警告后完成，错误策略必须显式保留，不能无差别吞错或统一改变行为。
3. **模块任务生命周期集中。** 截图目前有独立 CancellationTokenSource、ConcurrentBag<Task>、DisposeTask 和 drain。提炼可等待/取消、只跟踪进行中任务的局部模块 scope，可让业务 module 聚焦处理器。不得因过早移除任务丢失错误；具体业务仍负责临时文件清理，平台端口仍在 module drain 后释放。此项对应已有 Phase 4b 范围。
4. **对白/NVL 共用推进状态机。** 两个文件合计 258 行，Typing/WaitingAdvance/Completed、skip、模式切换与 auto-blocking 更新高度相同。可限定在文字交互族内共享状态机，保留不同 presenter 调用、CatchUp 历史记录及日志策略。收益中等，验证成本高于简单异步族；不将 Video、Audio ticket 或 AnimationPlan 并入。
5. **共用请求构造，保留不同输入来源。** 普通执行、计划事件、持久重放分别重建 Layer/Animation/Effect/Particle request，并重复部分默认/转换。例如 CreateAnimationRequest 的 context/state 两个版本几乎并行。DTO 可提供共同的强类型输入，集中纯 request 构造和领域状态动作；快照/计划输入适配仍分别存在，不借机统一存档或改变 replay 语义。
6. **无状态 module 外壳可缩小。** 只有注册构造函数、没有资源/共享状态的 module 可用明确的领域工厂返回 PrimitiveModule。Audio 等拥有 Runtime/scope 的模块仍保留。相对收益较低，不优先为少量外壳行数重排全部目录。

目标作者路径：普通同步 Entry 是 DTO + 一个业务回调；简单异步 Entry 是 DTO + 一个 Task 处理器 + 明确的模式/错误策略；复杂交互、视频及动画计划保留专用实例。全量 DTO、简单异步族和文字状态机试点应分别计量手写维护点、删行与新增抽象成本，不能凭文件数或所有类继承同一基类宣称完成简化。
