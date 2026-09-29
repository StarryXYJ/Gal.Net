---
feature: F-20260929-03-maintainability-roadmap
status: planned
updated: 2026-09-29
---

# 实施计划

本路线图本身不直接实施。每个 Phase 应创建独立 feature；其中边界清晰的 Phase 0、2b、5、6 可使用 fast-track，其余使用普通设计、实施和独立 review 流程。

## Phase 0 - 恢复绿色基线与工程门禁

**状态：verified**

实施 feature：[F-20260929-04-quality-baseline](../F-20260929-04-quality-baseline/feature.md)

完成证据：[F-20260929-04 实现总结](../F-20260929-04-quality-baseline/summary.md)。格式门禁因既有格式债务延期到独立纯格式 feature，其余退出条件已验证。

**前置条件：** 与粒子 feature 无文件冲突，当前失败可在干净基线上复现。

**范围：** 测试基线、`.editorconfig`、CI、依赖卫生、agent 路径。

**任务：**

- 明确 `LastDockLayout` 保存 JSON 语义还是原始文本，并修复对应实现或测试。
- 增加仓库级 `.editorconfig`；在测试目录定点处理行为式测试名的 `CA1707`。
- 为 CI 增加 pull request 和主分支 push 触发，拆分核心/Desktop 与平台 workload 验证。
- 加入格式检查，但记录受限本地环境的 named-pipe 限制。
- 删除确认未使用的包，评估并升级稳定 DI 包；集中重复 MSBuild 属性。
- 修正 `.agents/agent-knowledge.md` 和活动开发文档中的失效绝对路径。

**验证：** 普通 .NET/Desktop 项目构建；全部非平台测试通过；CI 配置语法有效；构建不再产生已知命名告警。

**退出条件：** 本地和 CI 都有明确、可重复的绿色基线；环境限制与业务失败可区分。

## Phase 1 - 契约归属 ADR 与依赖规则

**状态：verified**

实施 feature：[F-20260929-05-contract-ownership](../F-20260929-05-contract-ownership/feature.md)

完成证据：[F-20260929-05 实现总结](../F-20260929-05-contract-ownership/summary.md)；用户已接受 ADR-0001 的方案 C，首批项目依赖架构测试已建立。

**依赖：** Phase 0。

**范围：** Core、Runtime、Storage、Assets、Presentation 的长期边界。

**任务：**

- 生成完整公开类型和项目引用清单，标记真实消费者。
- 提出 ADR，比较保留、拆分和删除 `Storage.Abstractions` 三种方案。
- 明确 Runtime host/persistence ports、资源契约、Gallery 契约和 UI 宿主契约的归属。
- 定义允许的项目引用矩阵和命名空间规则。
- 为项目依赖方向添加首批架构测试。

**验证：** ADR 中每类契约都有目标归属；引用矩阵可由自动化测试表达；不修改生产依赖。

**退出条件：** 用户接受 ADR，或明确选择替代方案；未决定前不进入迁移。

## Phase 2a - 存储与运行时契约迁移

**状态：verified**

已完成切片：[F-20260929-06-storage-runtime-contract-migration](../F-20260929-06-storage-runtime-contract-migration/feature.md)，完成资源与 Runtime 端口迁移并删除 `GalNet.Storage.Abstractions`。

完成切片：[F-20260929-07-runtime-port-cleanup](../F-20260929-07-runtime-port-cleanup/feature.md)，完成 Core 历史宿主服务清理、Editor/Runtime 端口归位和 `ISaveService` API 收敛。

**依赖：** Phase 1 ADR accepted。

**范围：** `Storage.Abstractions`、Runtime ports、Core 中历史 services、FileSystem 实现。

**任务：**

- 按 ADR 移动内容、存档、玩家变量、变量桥接、资源和 Gallery 契约。
- 删除无消费者的 `IGameDataProvider`、`INavigationHost`、`IInputService` 等历史接口；音频接口交由音频 feature 决定。
- 收敛 `ISaveService` 到一套异步、可取消、支持 `SaveRequest` 的 API。
- 更新所有宿主、Editor、Sample、Headless 和测试引用。
- 删除或重命名空出的程序集，并同步 solution 与架构文档。

**测试：** 存档、玩家变量、内容加载、Gallery、Headless 和 Sample 组合测试；项目依赖架构测试。

**退出条件：** Runtime 的实际依赖与架构文档一致；不存在旧接口或双套保存 API；全仓库无旧程序集引用。

## Phase 2b - Presentation 与 Builtins 命名空间归位

**状态：planned**

**依赖：** Phase 1；可在不与 Phase 2a 修改同一文件时并行。

**范围：** Presentation.Abstractions、Primitives.Builtins 及其仓库内消费者。

**任务：**

- 将 `GalNet.Core.View` 迁到 `GalNet.Presentation.*`。
- 将内置 entry/schema/primitive 类型迁到 `GalNet.Primitives.Builtins.*`。
- 更新 using、文档、示例 JSON/schema 注册和测试。
- 根据已确认的外部兼容需求决定直接迁移或短期 obsolete/type-forwarding。

**验证：** API/namespace 清单、编译、entry catalog/primitive/runtime/presentation 测试和示例 smoke。

**退出条件：** 程序集与根命名空间一致；Core namespace 不再包含由扩展程序集定义的类型。

## Phase 3 - 测试套件按边界重组

**状态：planned**

**依赖：** Phase 2a 和 2b，避免测试文件重复搬迁。

**范围：** `GeneralTest`、`GalNet.Assets.Tests`、CI test jobs。

**任务：**

- 按生产边界和运行环境拆分 Core、Runtime、Builtins、Storage、Editor.Shared、Rendering/GameView 测试。
- 把跨 Editor/Runtime/FileSystem/Sample 的场景移入 `GalNet.IntegrationTests`。
- 每个测试项目只保留必要 ProjectReference；UI 测试与纯逻辑测试分开执行。
- 增加架构测试、测试分类和覆盖率合并配置。
- 删除无内容或没有独立价值的测试程序集，避免反向过度拆分。

**验证：** 所有测试数目有迁移对账；CI 可分别运行纯逻辑、Desktop UI 和集成测试；单个 Core 改动不编译 Sample/Editor UI。

**退出条件：** 测试项目名称、依赖和测试内容一致，`GeneralTest` 不再承担全仓库聚合职责。

## Phase 4a - Editor 职责拆分

**状态：planned**

**依赖：** Phase 3 提供 Editor.Shared 与 Editor 测试边界。

**范围：** `EditorWorkspaceViewModel`、Graph editing/history/persistence、`BuiltInEditorCommandHandler`。

**任务：**

- 先为现有选择、图编辑、undo/redo、保存和变量行为补 characterization tests。
- 将 command handler 按 Graph、Entry、Variable、Project domain 拆文件或独立 handler。
- 从 Workspace VM 逐个抽取 selection、editing、history、persistence 协作者。
- 保持 ViewModel 的 observable UI 状态和命令表面稳定，不顺便重做编辑器交互。

**验证：** Editor command、graph editing、history、document mapping、project lifecycle 测试；Editor 与 Headless 构建。

**退出条件：** Workspace VM 不再直接实现领域编辑和持久化细节；各协作者可独立测试；没有行为回归。

## Phase 4b - 游戏宿主与展示职责拆分

**状态：planned**

**依赖：** Phase 3；避开粒子、动画和音频功能 feature 正在修改的文件。

**范围：** `SampleGameSessionService`、`AvaloniaGamePageView`、`CompositeGameView`。

**任务：**

- 为会话初始化、停止、读档重放、资源释放和 UI 调度补 characterization tests。
- 从 Sample session 抽取资源 scope、runner、save session 和 persistent scene restorer。
- 把对话/选择、图层、动画和粒子 presenter 拆为独立实现，由 `CompositeGameView` 组合。
- 提取共享 Avalonia dispatcher adapter；保持具体资源和媒体策略位于 Sample/Avalonia 外层。
- 为 `AssetManager` 补并发/取消/释放测试，仅在一致性边界可保持时再考虑内部拆分。

**验证：** GameView、Presentation、Runtime replay、Sample session 和资源生命周期测试；Avalonia Sample smoke。

**退出条件：** Session 只负责编排，PageView 不再实现所有 presenter；读档和资源释放语义保持不变。

## Phase 5 - Editor 扩展 API 类型安全评估

**状态：planned**

**依赖：** Phase 4a 后能看清真实扩展边界。

**范围：** `Editor.Abstraction.Extensibility` 的 `IServiceProvider + object` API。

**任务：**

- 盘点仓库内外插件消费者和实际类型组合。
- 比较保留 service provider、引入强类型 contribution context、使用泛型 contribution 三种方案。
- 若需要不可逆公共 API 调整，提出独立 ADR；没有真实收益则维持现状并补文档约束。

**验证：** 至少一个内置 dock/inspector 端到端注册测试；插件 API 编译示例。

**退出条件：** 扩展 API 的类型安全边界被明确决定，不以 `object` 数量本身作为重构理由。

## Phase 6 - 物理目录与开发者文档整理

**状态：planned**

**依赖：** Phase 2-5 完成，工作树干净，避免 rename 与逻辑修改混杂。

**范围：** `src/` 物理目录、`GalNet.slnx`、README、spec/design 索引。

**任务：**

- 让 Editor、Presentation、Infrastructure、Samples、Launcher 的物理目录匹配 solution 分组。
- 保持程序集名和命名空间不变，只做可追踪移动和项目引用路径更新。
- 补充根 README 的仓库地图、构建测试命令、平台限制和 feature 工作流入口。
- 清理当前文档中的过时描述；历史 feature 保留历史事实。
- 复核 package lock、脚本、CI、示例路径和 agent knowledge。

**验证：** fresh clone 等价的 locked restore、普通 .NET/Desktop 全量构建测试、Headless/Avalonia Sample 脚本、无旧路径搜索结果。

**退出条件：** 物理目录、solution 分组、程序集名称和开发者文档表达同一结构；纯移动 diff 不混入行为修改。

## Phase 7 - 独立审核与路线图收尾

**状态：planned**

**依赖：** 所有已接受 Phase 完成；允许明确跳过未获收益证明的 Phase 5。

**任务：**

- 使用独立 feature review 检查依赖图、公共 API、测试隔离、文档和迁移残留。
- 对照本 feature 的每个问题项记录完成、替代方案或保留理由。
- 将稳定事实同步到 `docs/spec`，把长期决策链接到 ADR。
- 生成 `summary.md`，记录实际子 feature、验证证据、偏差和剩余工作。

**验证：** 架构测试和全部适用测试通过；仓库搜索无旧命名空间、旧项目路径或失效活动文档链接。

**退出条件：** 路线图中的所有项目均有已完成证据、明确跳过理由或新的独立 backlog 归属。
