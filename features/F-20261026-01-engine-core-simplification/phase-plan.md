# 引擎核心精简与职责收敛

状态：上一轮 P1–P5 verified，P6 保留环境待验证项；后续架构重构 R1–R3 verified，R4 in-progress，R5–R8 planned。

本文件是整个重构的实施与进度入口。P0–P6 保留已提交的历史；R1–R8 整理后续工作，设计依据见 [design.md](design.md)。设计整理完成不代表实现完成。

## 目标与约束

- 功能正确、低耦合、代码精简、职责清晰优先；不保留旧 API、旧数据格式或过渡适配层来维持向后兼容。
- 可恢复的小问题不应终止剧情；失败处理放在能够判断业务后果的模块边界。
- 优先删除重复表示、重复控制流程和无效分支；不为了拆分类而引入接口、服务或通用框架。
- Engine 保留唯一执行门与运行生命周期，协作者不能各自推进剧情；保留已验证的 batch、已读、播放模式和检查点语义。
- 本轮不做 benchmark、profile 或专项性能改造。仅顺手消除有明确不变量保障的重复遍历、排序、转换和分配；不宣称未测量的性能收益。
- 不为性能引入状态 revision、增量快照、缓存失效体系、并行调度或对象池；这些留给后续性能任务。
- 计划中的类型划分是方向，实施时依据实际调用关系收敛，不要求逐项创建新类型。

## 进度维护

按顺序实施；每次只将正在处理的阶段标为 `in-progress`。必要代码、调用方迁移、相关验证和文档完成后，才能勾选并标为 `verified`。

完成阶段的详细清单压缩为：**完成结果一句话 + 验证结果 + 证据链接/提交**。实际缺口不能被压缩掉；延期项标记 `skipped` 并说明原因，不计作完成。重要偏差在收尾 `summary.md` 中保留原因。

用户追加约定：每个阶段验证完成后单独提交 Git，再继续下一阶段；未完成或仅规划的阶段不能标为完成。R1 提交为 `76753fc`，R3 提交为 `0c51c23`；R2 提交为 `669b586`（部分早期修改另包含在独立清理提交 `2a5c678`）。

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| P0 | 核心审查与问题复现 | [x] verified |
| P1 | 收敛错误处理，修正表达式 | [x] verified |
| P2 | 修正推进、通知与恢复生命周期 | [x] verified |
| P3 | 收紧状态访问，隔离运行图 | [x] verified |
| P4 | 统一 Entry 数据，删除旧路径 | [x] verified |
| P5 | 收敛引擎协作者与模块边界 | [x] verified |
| P6 | 集成验证与文档收尾 | [ ] in-progress |

## P0 — verified

已审查 Core、Runtime、执行/呈现抽象并抽查 Builtins/GameHost，复现表达式、检查点观察者、读档位置、可变变量暴露、重复节点和推进取消问题。前次审查验证：Core 149、Runtime 114、Architecture 49 项通过；额外复现位于本地 `artifacts/engine-core-audit/Program.cs`（忽略产物，不是长期回归测试）。关键问题在对应实施阶段转为正式回归。

## 错误处理原则

严格检查集中在编译、内容装载、注册和存档恢复边界。日常执行只检查会变化或不能在入口保证的条件，不在多层重复执行相同校验。

| 情况 | 默认处理方向 |
| --- | --- |
| 重复 stop/hide、已清理的可选目标、预期取消、过期回调 | 幂等完成或忽略；通常不报错，也不反复警告。 |
| 可选音效/特效、可降级的展示后端失败 | 由领域模块定义静音、跳过或替代呈现；释放阻塞和资源，保留必要诊断。 |
| 本地化缺项 | 沿用已定义的文化/默认文本回退；最终缺失采用明确占位与诊断，不无故终止剧情。 |
| 事件观察者、日志接收方失败 | 隔离异常，不改变引擎执行状态；诊断失败不能递归造成新失败。 |
| 条件表达式解析失败 | 修复合法文本解析；非法条件返回 false 并给出定位诊断，作者校验明确报错，不静默吞错。变量赋值表达式失败拒绝该次赋值，不擅自写入默认值。 |
| 存档无效、读档位置不合法 | 拒绝本次恢复并报告原因，保留当前运行；不要把无效节点当正常结束。 |
| 保存/持久化失败 | 报告该次操作失败，保留可重试的数据；不误报保存成功，不无故中断仍可继续的剧情。 |
| 无法判定的控制流、必需指令缺实现、身份冲突、状态不变量破坏 | 入口尽早拒绝；若执行期间发生且不能保持一致性，则明确进入 Faulted。 |

“可选”由指令/模块的业务语义确定，不能依据异常类型或是否非阻塞一概跳过。已部分修改持久状态的操作，必须有一致的提交或补偿策略后才能降级。缺失 custom action 保留当前默认 warning/no-op 策略，不将所有未知指令都视为可选。诊断包含稳定 code 与可用的 Group/Entry/资源上下文，重复诊断应有界；优先复用现有通道，不建设全局错误策略框架。

## P1 — verified

修复字符串表达式、非法条件定位与限量诊断，失败/空值赋值保留原变量，音频诊断观察者不影响清理；现有可选展示降级和关键指令失败规则保留。Runtime 120、Builtins 177 项通过；实现与验证见 ExpressionEvaluatorTests、AudioRuntimeTests、BuiltinPrimitiveInstanceTests 及 [runtime 规范](../../docs/spec/runtime.md)。

## P2 — verified

检查点在锁外隔离发布，推进分批让出且可取消，Engine 改为异步读档并先准备验证候选再提交；宿主、调试和测试调用方已迁移。Runtime 125、Builtins 177、GameHost 5、Presentation 243 项通过；Integration 122 通过、1 项因 Windows 无创建符号链接特权失败（ScreenshotEntryTests:169）。完整解决方案构建另遇 Avalonia 遥测权限/WebAssembly 包哈希问题，最终构建在 P6 复核。行为证据见 GameEngineLifecycleTests 和 [runtime 规范](../../docs/spec/runtime.md)。

## P3 — verified

变量读取/集合快照隔离，运行图使用验证后的私有副本，状态锁改由 Read/Update 封装；执行模块共享的活记录保留，避免全量深拷贝。Runtime 132、Builtins 177、Core 149、Storage 22 项通过；新增外部图修改隔离、非法图入口与变量读取隔离测试，规范见 runtime/architecture。调试路线测试补齐真实的分支出口定义后通过。
## P4 — verified

Entry 统一为 JSON，作者/编译类型分离，删除 IsGeneric、字符串 Values、历史 Layer 特判和无效回退；选项统一为类型化 enum，Editor/CLI 保留输入适配与未知草稿。Core 149、Builtins 177、Generator 18 项通过，Editor 构建通过；Editor.Shared 初轮 417 通过、5 失败、2 跳过，其中选项夹具迁移后定向 5 项通过，余下为 3 项符号链接权限和 1 项文件移动拒绝访问（后者根因尚未确认，P6 重查）。规范见 entry-types。
## P5 — verified

primitive/呈现仅借用 IGameContext，Engine 管理 IGameRuntime 与 IPrimitiveDispatcher；通知统一为 EngineNotifications，删除阻塞镜像和冗余排序，Editor 设置移回 Editor.Abstraction，移除无使用者的 LauncherSettings。另补宿主替换前存档预检，避免上层提前释放旧运行。Runtime 133、Builtins 177、Presentation 243、GameHost 6、Architecture 49 项通过；Editor 构建通过，正式边界见 runtime/architecture。
## P6 — in-progress（环境待验证）

实现、正式文档、经验和自审已收尾；最终 1648 项通过、4 项创建符号链接权限失败、2 项跳过，Editor 308 项全通过。解决方案构建仅余 Browser WebAssembly SDK 包哈希不匹配，本地 locked restore 仍复现。原生桌面/显式外进程未执行。详见 [summary](summary.md) 与 [review](review.md)。

退出条件：在具备符号链接特权、正确 WebAssembly SDK 包和原生桌面条件的环境复核上述限制后，才将本阶段勾选为 verified。

## 可顺手做的优化

仅在触及相应代码时进行：活动实例顺序已经单调时删除多余 OrderBy；复用已经准备好的恢复结果；消除同一边界内无必要的重复查询/转换。保留必要的输入校验、隔离复制和检查点语义；若优化需要额外缓存或改变所有权，移出本轮。

## 后续整体重构

目标：项目策略归高层，通用执行归 Runtime，可选内置领域不进入内核；通过删除多份表示和共享实际重复机制精简代码。

设置模型归高层、内部接收简单配置、轨道/分组可自定义、Sample 保留三个分组是已确认方向。其余拆分位置为实施方向，具体类型/程序集归属依据消费者核实，不为完成表格制造新框架。实施时遇到会改变行为或范围的偏差，先更新设计与本计划。

| 阶段 | 内容 | 依赖 | 状态 |
| --- | --- | --- | --- |
| R1 | 执行协议与管理类型归位 | 已提交基线 | [x] verified |
| R2 | 设置模型与模块配置解耦 | R1 | [x] verified |
| R3 | 自定义音频分组与轨道控制 | R2 | [x] verified |
| R4 | 自定义配置贯穿作者与运行链路 | R2、R3 | [ ] in-progress |
| R5 | 剥离可选领域与场景依赖 | R1–R4 | [ ] planned |
| R6 | 统一参数描述与基础校验 | R5 | [ ] planned |
| R7 | 收敛执行与异步生命周期重复 | R5、R6 | [ ] planned |
| R8 | 最小项目、完整回归与文档收尾 | R1–R7 | [ ] planned |

### R1 — verified

执行/Choice/快进/播放端口归 Core.Execution，音频后端端口归 Core.Audio，IGameRuntime/GameRestorePoint 下移 Runtime.Runtime；Runtime 项目只依赖 Core，调用方与锁图已迁移，无兼容包装或行为变更。音频领域整体迁移仍归 R5。

893 项相关回归通过，1 项集成夹具受既有符号链接权限限制；普通桌面解决方案构建、locked restore、格式和架构门禁通过。34 个锁文件仅有项目依赖变化，第三方版本/哈希和目标框架键保持不变；Browser/R2R/原生平台验证限制见 [summary](summary.md#r1执行协议与管理类型归位)。证据含 Runtime 的 ExecutionBoundaryTests（自定义分发器执行/恢复、编译产物依赖检查）与 ProjectDependencyTests，正式边界见 [architecture](../../docs/spec/architecture.md)。

### R2 — verified

Engine 只接收/更新不可变 PlaybackOptions，音频只接收隔离轨道定义和 AudioMixOptions，文本消费速度值，保存服务接收槽位数；IGameContext/GameRuntime 不再携带设置容器。默认偏好模型上移 Avalonia.GameHost，PlayerSession 不持有完整设置；删除无用设置类型/服务和 SfxChannelCount 的 UI/CLI/复制路径。GameContent.Settings/作者 ProjectSettings 的整体迁移仍归 R4，自定义分组声明与单轨控制仍归 R3。

1645 项相关测试通过，4 项为既有符号链接权限失败，2 项跳过；当前完整解决方案构建、locked restore、格式与 diff 检查通过。覆盖显式配置更新/拒绝、集合隔离、已有/后续 voice、默认偏好清空重绑和存档容量。证据见 [summary](summary.md#r2设置模型与模块配置解耦)、PlaybackModeTests、AudioRuntimeTests、TypedBuiltinAuthoringTests、SampleLocalizationTests、SampleSaveSessionTests；接入示例见 [manual](../../docs/manual/new-presentation-layer.md#从项目设置映射模块配置)。

### R3 — verified

分组/轨道任意声明，允许空配置/未分组；不可变 AudioConfiguration 在入口统一校验，master/group/track 混音影响当前及后续 voice，快进保留为显式轨道策略。删除固定分类与指令默认目标；Sample/模板明确选择三分组，默认 UI 和 Editor 复用同一声明。

1752 项测试通过，4 项既有符号链接权限失败、3 项跳过；完整解决方案构建、空白格式与 diff 检查通过。最终修正后的 Builtins 185 项全通过，Integration 123 通过/1 项权限失败。证据含 AudioConfigurationTests、AudioRuntimeTests、ProjectAudioSettingsTests、SampleLocalizationTests 与 GameInstallationTests；记录见 [summary](summary.md#r3自定义音频分组与轨道)，规范见 [runtime](../../docs/spec/runtime.md) 和[接入手册](../../docs/manual/new-presentation-layer.md)。原生/外进程环境项继续保留，完整自定义 section/领域归位归 R4/R5。

### R4 — 自定义配置贯穿作者与运行链路

实施拆分：R4a、R4b verified；R4b-cleanup verified；R4c planned，完成通用 section 编辑/历史及自定义 DTO 的导出、安装、运行与预览验证。每步独立验证提交，R4 仅在完整链路退出条件满足后勾选。

- **目标/模块**：内容契约、Editor.Abstraction/Shared/UI、Storage.FileSystem、导出、Preview、安装加载与项目模板。
- **任务**：解除 GameContent 和 Editor document 对完整固定 ProjectSettings 的绑定；明确最小内容描述、可选模块配置与项目自有 section 的归属。复用现有 schema/表单能力提供必要 section 适配，未理解的 JSON 保留。迁移默认模板、语言/变量声明等实际消费者，删除重复权威来源；需要派生副本时由一个权威源生成。只保留一套项目自有模型和映射示例，两种 Sample 宿主复用同一项目配置定义。
- **验证/退出**：自定义 section 经打开→编辑→Undo/Redo→保存→导出→安装加载→运行/预览仍保留且生效；未知 section 编辑保存无损。作者校验与运行加载使用相同配置规则；明确发生变更的文件格式版本。补齐项目扩展手册。
- **风险**：只加 JsonExtensionData、任意字典或把类型换成 object 不算完成。纯项目偏好可保留默认值；无效内容不能被默认配置掩盖。

#### R4a — verified

Graph 独自持久化变量声明，删除设置中的副本、同步和无用归一化入口；仓库 Load 只接收路径。868 项相关回归通过，4 项既有符号链接权限失败、2 项跳过；构建、locked restore、格式与 diff 检查通过，提交 `5a08d4f`。详见 [summary](summary.md#r4a变量单一数据源与仓库输入)。

#### R4b — verified

版本化配置 section/schema 取代 Core 固定 ProjectSettings；默认 DTO 归可选 Builtins，作者/加载边界验证所选 schema，未知 section 与未声明字段保留。1770 项通过、4 项既有符号链接权限失败、3 项跳过；解决方案构建、格式和 diff 检查通过。语言名称解析修正另提交 `2a92fb6`。详见 [summary](summary.md#r4b配置协议与默认模型归位)。R4c 及 R4 整体仍未完成。

#### R4b-cleanup — verified

配置统一普通 DTO/schema 路径，删除预留元数据、手写 JSON 处理和重复验证；产品代码净删 88 行，Core 配置 239→166 行。隔离解决方案构建/locked restore、774 项相关回归及格式/diff 检查通过，4 项既有符号链接权限失败、2 项跳过。详见 [summary](summary.md#r4b-精简)，R4c 仍 planned。

### R5 — 剥离可选领域与场景依赖

- **目标/模块**：Core.Scene/Audio/Gallery、Runtime.Audio/Gallery/SceneInstanceManager、Contracts.Builtins、Primitives.Builtins、展示端口与宿主组合。
- **任务**：默认视觉/音频领域的数据、纯算法和校验迁入实际共享的内置契约；音频队列/票据/后端调度等运行实现移出通用 Runtime。SceneInstances 从通用上下文移交场景模块，GameState 不直接维护 ISceneInstance 专用索引。Gallery 的 catalog/解锁/宿主组合成为可选领域，不强塞 primitive 模块；其包装位置按消费者决定。检查默认资源/Gallery 注册与低层 fallback，确保默认工厂由明确的组合入口选择，资源访问/注册协议继续按其复用需要共享。
- **验证/退出**：最小自定义 primitive 可只用通用执行能力运行和恢复，无音频、场景、Gallery 或 Avalonia 要求；默认视觉/音频/Gallery 集成继续通过。句柄跨类型唯一、旧 scope 隔离、临时控制不入档、恢复顺序/drain 均保留。架构图与依赖门禁同步。
- **风险**：移动契约会影响多个消费者，不按目录机械搬迁。优先复用现有程序集，若出现循环依赖再依据真实边界选择专门领域契约；不建立万能 Common，不建立可变持久状态镜像。可拆为连续小提交，每个提交保持可验证。

### R6 — 统一参数描述与基础校验

- **目标/模块**：EntryDefinition、EntryModule、Parameters、视觉/音频效果 schema、SchemaGenerator、Editor 适配。
- **任务**：以 DynamicParameterTable 为 Entry 唯一参数来源，移除独立保存的 Parameters/Defaults/Options；文本和表单投影在外层按需生成，消除无意义的 nullable/fallback。Effect/SEffect 的类型、必填、范围和 enum 校验复用 FieldDescriptor/ValueValidation。目标范围、效果 stage、支持能力等业务检查留在领域模块；Shader 元数据解析器继续负责其语法。
- **验证/退出**：DTO→schema→作者输入→编译→执行及表单/CLI 默认值、类型化 enum、必填/空值行为一致；删除旧表示后 Core/Generator/Editor/内置模块相关测试通过。更新参数规范。
- **风险**：不新增另一套通用 schema；不同领域错误策略和草稿未知字段保留规则不能被统一校验意外覆盖。

### R7 — 收敛执行与异步生命周期重复

- **目标/模块**：GameEngine 各 partial、快进/调试回放、内置异步 primitive 与现有 AsyncPrimitiveInstance/PrimitiveTaskScope。
- **任务**：共享同一引擎内分批推进、阻塞等待、scope 检查和必要收尾机制；玩家快进/调试路线的停止策略、已读与副作用继续分别定义。收敛重复的状态捕获、门禁释放和锁外通知，保留唯一执行门。基于真实重复复用异步任务观察/完成/清理，保留动画、视频、Timeline 的领域状态与失败策略；可读性收益不足的抽取不强做。
- **验证/退出**：长剧情、取消、阻塞自然完成、Choice、自动/快速模式、断点/Step/Seek、读档、观察者异常与呈现失败相关回归通过；通知仍在锁外，取消不误报 Faulted，无第二个剧情 Runner。更新生命周期规范及实现说明。
- **风险**：不使用大量布尔策略参数隐藏不同语义，不统一吞异常；不得以缩短文件为由增加反向回调或通用执行框架。

### R8 — 最小项目、完整回归与文档收尾

- **目标/模块**：架构门禁、单元/集成测试、Sample/模板、正式文档和 feature 收尾。
- **任务**：以“最小无内置领域运行”与“Sample 自有设置 + 三分组”验证最终边界；测试任意自定义组/轨道及自定义配置完整链路。删除旧 API/无用类型/过时命名空间和过渡路径，检查公开契约、模板和包版本。执行与变更相关的构建/完整回归，更新 spec/manual、agent 已验证事实和 summary/review。
- **验证/退出**：仓库调用方全量迁移、架构门禁与可执行测试通过，默认 Sample 可以运行；实现、验证、限制可追溯。上一轮 P6 的符号链接权限、WebAssembly 包哈希、原生桌面/外进程待验证项独立复核，未解决则继续明确保留，不计为已验收。
- **风险**：不因为文档或源码搬迁完成就勾选验收，不将环境失败包装成通过。不开展 benchmark/profile、缓存/对象池/并行执行等专项优化。

后续同样遵循前述进度维护规则：只勾选实际 verified 阶段，完成后压缩为“结果一句话 + 验证 + 证据”。用户已授权开始实施；各阶段状态以本表及实际验证证据为准。

## 参考

- [运行时规范](../../docs/spec/runtime.md)
- [架构规范](../../docs/spec/architecture.md)
- [Entry 契约](../../docs/spec/entry-types.md)
- 本计划依据 2026-10-26 会话中的核心审查及用户修订：功能/低耦合/精简优先，专项性能工作延期。
