---
id: F-20261013-02-parameter-and-sample-simplification
title: 参数契约共用与 Sample 扩展简化
type: feature
status: in-progress
created: 2026-10-13
updated: 2026-10-15
---

# 参数契约共用与 Sample 扩展简化

2026-10-15 后续计划已按当前代码整理，实施入口见 [Phase Plan](phase-plan.md#当前后续计划2026-10-15-整理)：先明确两项 UI Flush 验证缺口，再实施 4b2 清理不短路、4b3 文本推进状态机复用，最后 Phase 6 作者路径/收益复核与收尾。全量 DTO 和 generator 不再作为待实施工作；剩余 request/重放胶水按证据决定，不追加通用框架。本轮仅修改计划文档。

2026-10-15 Phase 4b1 已完成可重载异步默认实例、Async<T> 工厂和模块任务 scope，迁移截图与五个 Effect/Particle 实例。607 个不重复用例和全解决方案编译通过；完整集成中的两项其他任务设置 UI Flush 测试挂起，限制与证据见 [phase-4b1.md](phase-4b1.md)。Phase 4b 仍在进行，后续为 Session 清理与 Dialogue/NVL 状态机。

2026-10-14 Phase 4 已验证：PrimitiveImplementation.Immediate / Immediate<T> 简化同步工厂，轨道操作与 wait 使用唯一 DTO 参数声明，复杂校验/Composite/异步实例保留手写扩展。307 个不重复回归用例通过；五条指令迁移前后 CLI 描述相同，独立契约部署与实际项目编译通过。证据见 [phase-4.md](phase-4.md)。当前最新阶段为 Phase 5a：全量 DTO 基础与 audio.stop 试迁移已验证，456 项回归与 CLI 格式对照通过，见 [phase-5a.md](phase-5a.md)；Phase 5b 的 11 条简单 Primitive 也已迁移，235 项回归与 CLI 对照通过，见 [phase-5b.md](phase-5b.md)；Phase 5c 也已迁移 12 条复杂 Primitive，338 项回归与 CLI 对照通过，见 [phase-5c.md](phase-5c.md)；Phase 5d 已完成 8 条 Composite 与旧类壳清理，393 项回归和实际编译对照通过，见 [phase-5d.md](phase-5d.md)；下一阶段为 Phase 4b。

2026-10-14 最新收敛决定：DTO 成员特性生成有序 schema 与成员访问表，公共 DTO 适配器连接表单和宿主提交；新增 Phase 2d 位于 Phase 2c 最小验证之后。源码生成器已确认为实施目标，替代下文旧的候选/条件表述。范围、通知/默认值边界及退出条件以 [DTO 收敛计划](dto-convergence-plan.md) 为准；Phase 2c 最小宿主接入已验证（241 项回归），见 [阶段记录](phase-2c.md)；Phase 2d 单 Entry DTO 生成/绑定试点已验证（348 项回归、独立契约部署与实际 CLI 编译），见 [phase-2d.md](phase-2d.md)。Phase 4、5a–5d 已验证，下一步为 Phase 4b；全量设置 DTO 化仍暂缓，Entry 全量 DTO 按新增批次实施。

2026-10-14 用户确认实施全量 DTO 和执行实例简化。新增 5a–5d 验证批次；先补 JSON 默认值、字符串值编码和 Composite 适配，再迁移参数与清除旧壳。异步实例/任务 scope 独立由 Phase 4b 收敛。

## 目标

抽取动态值描述、序列化/反序列化和校验工具，让 Entry、设置及需要的 CLI/变量消费者按职责组合使用；共享基于 schema 的无样式 Avalonia 字段编辑器，集中 Sample 的业务接入点，减少机械样板和重复清理逻辑。以实际收益约束工作量；允许保留少量清晰重复，不为形式统一增加复杂框架。

用户追加范围：明确纳入 Sample 各 service 的职责、转发链和重复逻辑优化，以及跨 Core、契约、执行模块和平台 Sample 分散的 Entry 实现基础；不仅整理截图或集中注册位置。先审查实际成本，再迁移有收益的部分。

目标优先级澄清（2026-10-14）：Entry 部分首先要减少开发者新增常见 Entry 时的样板步骤与重复事实，包括参数声明/读取和常见工厂接入；异步完成/清理属于并行的运行时改进。后续工具化不限于源码生成器，需比较更顺手的 authoring API、typed reader/factory、DTO/JSON 元数据、诊断/IDE 辅助与模板/示例，按端到端净收益组合采用。

用户已确认按本计划推进。Phase 0/1/2a/2b/2c/2d/2e/3/4 已验证；中立值工具、有序字段与开放编辑提示、选定消费者迁移及共享 SchemaForm/默认工厂已完成。Phase 2b 的 290 项回归与示例契约部署/编译通过；Editor/Sample 最小宿主适配已验证；DTO 生成和 Sample 组合已验证，代表作者路径已验证，异步/清理及进一步工具化仍待后续阶段。承接已完成的 [Editor/CLI feature](../F-20261013-01-editor-cli-operations/summary.md)，不改写其历史范围和状态。

## 已确认约束

- 公共参数基础先放在 GalNet.Core 的独立命名空间，不新建 Helper 或 Parameters 项目。
- 各消费者通过组合使用中立 ValueDescriptor、可选 EditorHints 与公共值处理工具，不让 CLI 引用 Entry 的完整参数类；不强制设置、变量、CLI 共用父类/字段模型。
- 值类型/约束与编辑提示分离；EditorId 使用可扩展字符串，内置提供常量而非封闭 enum。Numeric/Slider 是编辑方式，不是值类型；Step 默认仅为编辑步长。
- 共享 Avalonia SchemaForm 按 schema 有序字段生成子编辑器；服务为 EditorId 到工厂方法的注册表，工厂创建控件和属性绑定并返回独立释放句柄。Editor/Sample 组合默认集合和自定义工厂；数据提交适配独立承担撤销/异步应用/保存等行为。
- 游戏直接引用契约项目；构建出的 Contracts DLL 用于 Editor/CLI 独立加载，游戏不必动态加载自己的契约。
- 源码生成器属于独立构建工具项目，运行时及契约程序集不依赖 Roslyn。接入前先证明重复量足以抵消引入成本。
- 不考虑向后兼容，但不借机无目的地改动内容格式、命令行为或所有公共 API。
- 不合并游戏变量与参数的完整模型，不创建第二套重复的标量值容器。
- 不强制跨平台接口；单平台业务实现仍可直接操作具体页面。
- 开发者集中入口覆盖 schema 来源的读取/选择、执行 module 注册及相关 decoder/项目配置。游戏默认消费项目引用的静态契约入口；Editor/CLI 继续加载当前项目 DLL。二者使用同一声明，不增加静默 fallback 或第二份 schema。
- 暂不实施 Core 领域去耦、Audio 迁层、Layer/SceneState 移出、通用模块快照及 animation/plan 执行体系重构；这些留待未来实际跨平台需求。
- 每个完成且验证的实施 Phase 独立提交，仅包含本 feature 的改动。
- 工作量大而收益不明确的步骤可缩小或 skipped，记录原因；不能静默扩大范围。

## 改进清单

| 改进 | 收益 | 相对工作量 | 决策 |
|---|---|---|---|
| 参数描述、规范化和基础校验共用 | 减少重复规则，帮助描述与实际执行一致 | 中；全面迁移则大 | 先做垂直试点，按收益迁移 |
| 有序字段描述与可扩展编辑提示 | 设置与 schema 使用相同元数据，支持开发者自定义 EditorId | 小～中 | Phase 1/2a 定义最小模型 |
| 无样式 Avalonia 表单与工厂注册表 | 共用控件选择/绑定/释放，消除设置与 Editor 的重复编辑器分支 | 中 | Phase 2b/2c 独立验证实施 |
| Sample 契约与平台接入点集中 | 新业务不再修改生命周期样板 | 小～中 | 计划实施 |
| Sample service 职责与调用链收敛 | 减少无价值转发、重复状态及耦合修改 | 中；全面重写则大 | Phase 3 专项审查和局部实施 |
| 分散的 Entry 实现基础收敛 | 参数读取、工厂注册、完成/取消等只维护必要的公共机制 | 中；全量迁移则大 | Phase 2a/4 按执行类型验证迁移 |
| 清理异常策略一致 | 失败后继续释放资源，保留完整错误 | 小～中 | 计划实施 |
| 异步 entry 公共机制 | 减少取消、任务完成、异常和释放样板 | 中 | 有限抽取，截图验证 |
| 参数与模块胶水生成 | 减少成批机械声明和读取代码 | 中～大 | 条件实施 |
| 重复 catalog 构造、默认适配代码 | 在已有职责内消除实质重复 | 视调用链而定 | 只处理有证据的局部重复 |
| 游戏变量和参数模型完全统一 | 收益尚不清楚，牵涉身份/作用域/持久化 | 大 | 暂缓 |
| 全部动态注册合并到项目 DLL | 支持更多项目自定义类型 | 大 | 暂缓，另立需求 |
| 全面合并文件/搬迁宿主代码 | 收益主要是表面整理，存在边界风险 | 中～大 | 不实施 |

工作量为相对规模，非工期承诺。每个收益检查点重新核对范围。

## 验收

- Entry JSON 和 CLI 文本保留各自的输入绑定，但实际校验消费同一参数规则；不只是共用类型名称。
- 必填、默认值、省略、显式 null、范围、枚举与对象参数有明确且可验证的行为。
- 动态值基础覆盖标量、对象、数组与枚举；截图和分页是试点，不是公共工具的适用范围限制。设置/变量只在必要时使用工具，不合并完整模型。
- 同一有序字段 schema 在设置和 Editor 使用同一组默认 Avalonia 工厂；自定义 EditorId 可扩展，草稿/提交/失败/释放边界有实际验证，平台类型不进入 Core。
- 截图的业务声明和执行可接入集中入口，无需修改 App、Session、Runner 或资源释放代码。
- Sample service 与 Entry 基础有职责/重复清单及处理结论；删除无价值包装，保留确有不同生命周期或语义的对象，不把目录集中视为完成优化。
- 清理失败不导致后续独立资源漏释放，释放顺序和所有权保持明确。
- 代码简化有迁移前后证据，包括手写代码、生成代码、删除的重复实现和新增抽象成本。
- 契约可独立构建/部署供工具使用；游戏仍通过项目引用消费，Core/Runtime 不新增平台或 Roslyn 依赖。
- 既有行为通过适当回归验证；所有条件阶段的实施或跳过结论可追溯。

## 文档

- [全量 Entry options DTO 化候选评估](dto-migration-assessment.md)

- [设计边界](design.md)
- [Phase Plan](phase-plan.md)
- [Phase 0 基线与收益判断](baseline.md)
- [Phase 1 实现与验证](phase-1.md)
- [Phase 2a 实现与验证](phase-2a.md)
- [Phase 2b 实现与验证](phase-2b.md)
- 当前事实参考 [开发指南](../../docs/development.md)、[架构](../../docs/spec/architecture.md)、[Entry 注册 ADR](../../docs/adr/0002-entry-schema-execution-registration.md)。
