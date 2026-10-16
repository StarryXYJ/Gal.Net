# 参数契约共用与 Sample 扩展简化：实现总结

2026-10-15。状态：completed；收尾自审结论 pass，见 feature.md、review.md。

## 结果

Entry 字段只维护 DTO 中的类型、默认值、约束和编辑提示，编译、Editor 和执行共用同一事实。42 个内置 Entry 和示例截图共 43 条：38 条有参数的 Entry 全部 DTO 化，5 条无参数保持空 schema。契约程序集只依赖 Core，generator 作为构建期 Analyzer；游戏显式引用契约，工具独立加载部署的 DLL。

Core 中立 ValueDescriptor/FieldDescriptor、ValueCodec/ValueValidation 供 Entry、CLI 和设置按职责组合。开放 EditorId、无皮肤 SchemaForm、EditorRegistry 与 FieldEditorLease 由宿主/主题组合；Editor 直接绑定参数文档，设置保留异步应用/保存，资源/表达式等继续使用专属编辑器。省略、默认和 null 不混淆，打开面板不写默认值。默认 JSON、严格字符串 bool/enum、源基类字段和自定义 converter 均有实际回归。

SampleCustomization 集中服务/主题/本地化/字段工厂/资源选择/执行注册。Session、Runner、Presentation、存档和资源 scope 保持不同生命周期；不靠机械合并文件减少数量。

新增原语的推荐路径：

| 行为 | 作者入口 | 保留的业务职责 |
| --- | --- | --- |
| 无参数同步 | 空 schema + Immediate | 业务动作、CatchUp 策略 |
| DTO 同步 | ForDto<T> + Immediate<T> | 业务校验与动作 |
| 单次异步 | Async<T> 或 AsyncPrimitiveInstance | 操作/清理、错误策略、模式 |
| 打字后等待推进 | TextAdvancePrimitiveInstance + For<T> | 展示/skip/历史/诊断操作 |
| 复杂交互、视频、动画 | For<T> + PrimitiveInstance | 独立阶段、资源身份和推进规则 |
| Composite | ForDto<T> + typed Compile | 子 DTO、条件/批次与业务展开 |

同步/异步便捷工厂自动传 BatchId，DTO 每次调用独立。AsyncPrimitiveInstance 默认传播失败；Builtin 展示默认实现明确保留警告策略。文本展示结束与逻辑完成分离，Quick 不吞掉玩家推进等待。Task scope 只跟踪运行任务、完成即移除，错误仍能在释放时观察。Sample 按显式顺序尝试全部独立清理，最后聚合错误，关闭/flush 失败不再阻止下属资源释放。

## 阶段与提交

| 阶段 | 主要提交 | 证据 |
| --- | --- | --- |
| 0/1/2a | eafbd96、92b9453、bc1adf0 | baseline.md、phase-1.md、phase-2a.md |
| 2b/2c | de63815、04d3c33 | phase-2b.md、phase-2c.md |
| 2d/2e | 34bb349、eac5483 | phase-2d.md、phase-2e.md |
| 3 | f545d11 | phase-3.md |
| 4 | ffeac99 | phase-4.md |
| 5a/5b/5c/5d | 99dfe32、456026a、7d574b6、df06617 | phase-5a.md 至 phase-5d.md |
| 4b1 | 302d965 | phase-4b1.md |
| 验证基线 | 967ba7e | phase-4b-baseline.md |
| 4b2 | 7073c2e | phase-4b2.md |
| 4b3 | e02e886 | phase-4b3.md |

按实际依赖先完成全量 DTO，再处理异步/清理；Phase 5 原候选比较已形成落地方案，不重新建设 generator/DSL。4b1 的两项 UI Flush 挂起经真实 Headless/UI pump 补齐，后续完整集成不再排除它们。4b2 的 UI 异常断言阻塞修正为直接 await + try/catch，原始失败证据保留。

## 收益与成本

- 删除 34 个 Primitive 作者类壳、旧取 ID 工厂、普通字符串参数 reader、重复参数表和 Editor DTO 副本；Composite 使用类型化参数与子 DTO writer。5d 含公共工具/生成器成本后生产净减 155 行。
- Effect/Particle 五实例从 200 行降至 53 行。4b1 含公共实例、工厂、任务 scope 后生产净增 29 行；不能只报派生类删行。
- 清理正确性批次 4b2 生产净增 25 行，覆盖五层拥有者、错误聚合和重复 Session 关闭。
- Dialogue/NVL 两实例加公共状态机从 258 行降至 153 行，净减 105 行；4b 合计生产净减 51 行（+29 +25 -105）。统计含注释/空行，排除测试、生成物、文档和其他任务改动。
- 中立值工具、表单和 generator 是新增能力，早期阶段多处净增代码。整个 feature 的价值是减少维护事实、作者修改点和重复状态机，不宣称全仓总代码量下降；各阶段成本独立列出。

## 最终验证

11 个测试项目顺序回归共 898 个不重复用例通过，无 filter 排除；覆盖实际 Editor 输入/撤销/保存、CLI/契约加载、截图 PNG、Engine 模式/错误、清理故障和文本竞态。日志为 Temp/F-20261013-02/phase6-*.log、phase6-*.trx；Editor 使用独立输出保护运行中的程序。

开发指南的契约 DTO/schema、执行 module 和可重载文本实例示例已从 Markdown 提取，用实际 Core/执行/Builtins 程序集和 generator 编译为 phase6-examples.dll，0 错误。

完整 GalNet.slnx 独立输出编译 0 错误、46 个现有/输出目录/审计源警告，日志 phase6-build.log；输出仅作为编译验证，不作为发布包。Sample.Contracts 独立构建并部署至 GameTestCase/Contracts，CLI 独立构建后 schema show sample.screenshot 成功，实际项目编译 4 个 Group、diagnostics=[]，证据 phase6-contracts.log、phase6-cli-build.log、phase6-schema.json、phase6-game-build.json。复用原 16 条 Composite fixture，再次实际编译为 20 条 Primitive；按对象键规范化比较后完整文档与 phase5d-after.galgroup 相同，含参数/条件/批次/阅读边界/source map，证据 phase6-composites-comparison.json。临时游戏项目 fixture 已精确清除。

正式事实见 [开发指南](../../docs/development.md)、[条目契约](../../docs/spec/entry-types.md)、[共享控件](../../docs/spec/control.md)、[运行时](../../docs/spec/runtime.md)。

## 保留与暂缓

执行 DTO 与持久快照是不同输入协议；动画 request 的 DTO 适配和存档重放适配继续明确区分，不把旧 string 参数快照强行转换为作者 DTO。开放动画事件/嵌套业务 payload 保留 JsonElement。视频/动画的播放身份、分段推进和后台所有权继续专门实现。

全量设置 DTO、递归表单、自动 module 扫描、生成业务/撤销/持久化、Core 领域/快照/Audio 迁层和通用 Runner 均未实施，属于明确暂缓，不是本次漏做迁移。原生媒体完整人工播放/完整游戏人工 walkthrough 未执行；自动化和真实截图不替代这些 opt-in 验证。工作区其他任务的设置预览、README/统计脚本和其 lessons 不混入本 feature 提交；Dialogue 已有 Lock 语义保留在公共状态机。
