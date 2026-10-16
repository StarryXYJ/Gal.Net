# Phase 0 调查与基线

日期：2026-10-13。状态：verified（本阶段调查与定向基线通过，不表示整个 feature 已实现）。

## 范围确认

按既有优化计划推进，开发者入口明确包含 schema 来源的读取/选择、catalog 构建及执行 module 注册。游戏消费项目引用的静态契约；Editor/CLI 使用既有项目 DLL 加载路径。没有新增运行期动态 schema-source 框架，也没有改动业务代码。

Core 领域去耦、Audio 迁层、Layer/SceneState 移出、模块化快照及 animation/plan 调度重构全部暂缓。样板优化不能借此改变这些领域的归属。

调查时工作区已有 SampleGameSessionService.cs 四处空行改动及未跟踪 Temp/；均保留，不纳入本阶段提交。

## 参数试点与成本

选择 ScreenshotSchema.fileName 与真实 files 命令的 --offset/--limit。截图覆盖省略、显式 null 和业务校验；分页覆盖整数转换、范围、默认值及帮助描述。现有分页接受非负整数，offset 默认 0，省略 limit 不设置上限；不擅自改成 limit 必须大于 0。

| 当前职责 | 来源 | 判断 |
|---|---|---|
| Entry 类型/默认值/约束 | DynamicParameterDescriptor/Table | 演进既有模型，避免平行 schema |
| Entry 默认填充、未知参数与 options 校验 | PrimitiveArgumentHelper | 共用其中基础规则，保留 compiled batchId 处理 |
| CLI 选项、枚举、必填、重复检查 | CliInvocation + CliParameter | 已消费定义；保留命令行绑定职责 |
| CLI 分页整数与坐标数值读取 | Editor.Headless.Program | 可迁移到通用基础读取/校验；不扩大为业务参数引擎 |
| 操作对象的 JSON schema | CommandSchemaBuilder/ToolOperationCatalog | 复用 DTO 描述能力，不重建对象 schema 系统 |
| 截图便携 PNG 文件名规则 | ScreenshotSchema.ValidateFileName | 业务规则保留，不写入 Core |

用于后续对比的原始行数（含空行，不作为硬指标）：DynamicParameterTable.cs 114，PrimitiveArgumentHelper.cs 79，CliInvocation.cs 56，ScreenshotSchema.cs 26，SampleScreenshotModule.cs 137。Phase 1 必须同时记录新增公共代码和删除的重复规则；少量调用代码减少不足以证明引入大框架合理。

## Sample service 候选结论

| 范围 | 决策与理由 | 成本 |
|---|---|---|
| App 的主题、本地化启动与可选调试配置 | 集中到项目 customization，关闭编排仍在 App | 小～中 |
| ResourceScope 的 decoder/预加载选择 | 外提选择，资源持有和释放仍由 scope 管理 | 小～中 |
| Presentation 的 schema/module 注册 | 集中来源与选择，平台资源仍有明确所有权 | 小～中 |
| Session / Runner | 保留应用会话与单次运行边界，避免把全部控制流合并 | 全面合并成本大，无当前收益依据 |
| SaveSession / Gallery resolver | 局部评估资源所有权；只在简化释放和接入时调整 | 中，条件迁移 |
| PlayerSettings / Startup | 设置投影/持久化与首次启动策略不同；暴露业务选择，暂不机械合并 | 小～中 |
| Localization | 保留 provider 和 LocalizedString 的平台适配，集中 bootstrap 来源 | 小～中 |
| WindowDisplay / Restarter | 有原生窗口/进程职责，保留适配边界 | 不因类小而内联 |
| Debug actions / Session 中调试存档策略 | 检查向已有 Debug 区域归位，保留必要会话协调 | 小～中 |
| Session 清理链与 Runner/Presentation 聚合清理 | 整理失败后继续释放策略；不能隐藏释放顺序 | 小～中 |

## Entry 基础候选结论

- 现有 PrimitiveInstance/ImmediatePrimitiveInstance、PrimitiveModule/PrimitiveImplementation 可作为基础；不新增平行 EntryBase/ModuleBase。
- Arguments、BuiltinInstructionFactory 与 schema 声明在参数试点后检查重复来源；不能把数值 fallback 留成第二份默认规则。
- 截图可作为异步完成/取消/释放等待的首个样例。再选择实际同语义内置实例，证明公共机制可以复用；不要预设所有实例继承同一异步基类。
- Effect/Video 部分展示失败记录警告后继续完成，截图完成任务传播失败；这属于策略差异，当前不以基类整理为由改变。
- 等待、对白、音频、视频、动画的阻塞/skip/replay 和场景状态机制继续各自拥有，不迁移领域模型。
- 参数基础与注册入口值得先试；全面 Entry 合并、全部动态类型 DLL 化或新增共享宿主项目收益不足，维持暂缓。

## 定向验证

所有命令设置 AVALONIA_TELEMETRY_OPTOUT=1，并使用 --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet。

| 项目 | --filter | 结果 |
|---|---|---|
| GalNet.Core.Tests | FullyQualifiedName~DynamicParameterTableTests | 3/3 |
| GalNet.Editor.Shared.Tests | FullyQualifiedName~CliHelpTests\|FullyQualifiedName~ProjectSchemaContextTests | 21/21 |
| GalNet.IntegrationTests | FullyQualifiedName~ScreenshotEntryTests | 14/14 |
| GalNet.Presentation.Tests | FullyQualifiedName~CompositeGameViewTests | 7/7 |

合计 45 项通过；截图覆盖真实 PNG 及相关执行语义，契约测试覆盖 DLL 加载，注册测试覆盖 schema/执行分离。构建输出包含既有分析器警告。未运行全解决方案回归，不据此宣称所有既有行为通过；后续根据实际 diff 扩大验证。

## 下一步

进入 Phase 1 的最小垂直试点。保留 CLI 命令行绑定与 Entry 专属元数据，先证明统一基础约束和执行读取能减少重复；生成器仍为条件阶段。
