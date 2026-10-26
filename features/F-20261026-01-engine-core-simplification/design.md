# 后续架构提案：缩小内核，分离默认领域与项目策略

状态：实施中，R1–R3 verified，R4 in-progress，R5–R8 planned。用户已确认“轨道与分组均可自定义、支持两个层级的控制、设置模型归高层、内部接收简单配置、Sample 保留三个默认分组”的方向，并授权按整体计划开始实施；具体拆分仍依实际消费者收敛。不改变上一轮已完成阶段的实现事实，也不将 P6 环境待验证项标为完成。

依据：2026-10-26 用户提出 Core 中的游戏设置、固定音频分类和 Core/Runtime 领域分布需要重新审视；用户允许破坏性调整，以功能、低耦合、精简和职责清晰为先。本文记录设计与取舍，实施和验收进度以 phase-plan.md 为准。

## 问题判断

上轮解决了执行正确性、重复数据和一部分接口边界，但没有完成“可替换默认玩法”的架构收敛。当前 Core 同时承担扩展协议、默认视觉/音频领域、项目配置和玩家偏好；Runtime 同时承担通用执行机制与可选音频/Gallery 实现。无 UI 依赖不等于职责归属正确。

Core 与 Runtime 同时出现同一领域本身合理：供契约、模块、工具共同使用的模型/协议在内层，运行实现放外层。判断标准是消费者和依赖方向，而不是类型名中是否含 Runtime，也不是只有接口才能进 Core。

## 设计评估时的证据（R1 实施前）

| 当前代码 | 问题与影响 |
| --- | --- |
| `Core/Settings/GameSettings.cs` | sealed 类型混合自动/快速推进、文本速度、四套音量/静音及全屏。Engine、音频和默认宿主读取同一具体模型；用户 DTO 不能直接替代。 |
| `Core/Settings/ProjectSettings.cs` | 语言、存档槽、窗口大小、轨道、变量声明混合。`Runtime.Content.GameContent.Settings`、文件加载器和 Editor document 都绑定它。单纯把文件迁出 Core 不能解决端到端绑定。 |
| `Core/Settings/SettingsContainer.cs` | `Get<T>()` 读取即构造、写入默认值，要求继承与无参构造；同 key 不同类型会被静默替换。它不是已完成的自定义设置扩展协议。 |
| `Core/Audio/AudioTrackDefinition.cs`、`Runtime/Audio/AudioRuntime.cs` | track ID 可自定义，但 bus 被 `AudioBuses.IsValid` 限为 bgm/sfx/vocal；增益 switch 直接读取 GameSettings 固定字段。轨道默认集合、中文名称和默认增益也在 Core。 |
| `Core/Settings/SaveSettings.cs`、`ProjectSettings.SfxChannelCount` | 生产代码搜索未发现 SaveSettings 的实际消费者；SfxChannelCount 仍被编辑/复制，但音频并发采用每轨 MaxVoices。这是可删除的旧表示候选，不应再围绕它们设计扩展。 |
| `Core/Runtime/GameState.cs`、`Core/Runtime/IGameContext.cs` | 通用状态表内维护 ISceneInstance 索引，执行上下文强制提供场景管理器；纯逻辑项目也携带默认视觉领域。 |
| `Runtime/Runtime/SceneInstanceManager.cs` | 已是持久表的派生查询加临时控制对象，不是第二份持久状态；迁移时不能重新引入状态镜像。 |
| `Presentation.Abstractions/Execution/IPrimitiveDispatcher.cs` | 签名只使用 Core 类型，却归展示程序集，导致 Runtime 为执行端口依赖整个展示抽象。 |
| `Core/Runtime/IGameRuntime.cs`、`GameRestorePoint.cs` | 属于游标和恢复管理；Core 中的 primitive 创建契约只需要 IGameContext。具备下移到 Runtime 的条件。 |
| `Core/Scene`、`Core/Audio`、`Core/Gallery` | 包含 Layer、Timeline、粒子、Shader 元数据解析、默认效果和 Gallery 规则；其中很多是可复用的默认领域，但并非通用剧情执行所必需。 |
| `Core/Entry/EntryDefinition`、`Scene/EffectCatalog`、`Audio/SEffectCatalog` | Entry 保留多份参数投影；效果和音频效果各有参数描述与基础校验。存在进一步复用既有参数体系的空间，但领域约束不能被通用校验替代。 |

## 建议的归属

| 边界 | 应拥有 | 不应拥有 |
| --- | --- | --- |
| Core | 指令/参数/执行上下文契约、控制流模型、变量值、通用状态注册与快照数据、必要资源访问协议 | 默认窗口/音量/语言策略、固定轨道分类、具体视觉模块规则、仅供 Engine 管理的内部恢复流程 |
| Runtime | Engine、唯一执行门、scope/取消、导航、播放调度、变量求值、恢复协调 | 全量玩家设置、必须安装的内置音频或视觉系统、默认 Gallery 解锁策略 |
| Contracts.Builtins | 默认指令及其共用的音频/视觉数据、领域校验和纯算法 | 平台实现、项目自己的设置 DTO、默认窗口/玩家持久化路径 |
| Primitives.Builtins | 默认领域执行、音频队列/票据、场景句柄与临时控制、内置状态注册 | 独立于 Engine 的剧情推进门禁、通用宿主会话 |
| Presentation.Abstractions | presenter/后端端口、可复用的展示恢复协作 | 为通用执行端口承载不必要的展示依赖 |
| GameHost / 默认 Avalonia 宿主 | 会话、持久化与平台协调；默认宿主提供可选默认接线 | 要求所有项目采用同一个玩家设置模型或 Gallery |
| Editor / Storage / 项目组合根 | 作者文档、序列化适配、配置校验、加载/导出、自定义设置的 UI 与持久化 | 把项目的完整配置对象传给每条指令 |
| Sample / 项目模板 | 一套完整项目设置与玩家偏好示例、默认轨道/分类、对模块的显式映射 | 重复实现 Engine、音频队列或可复用呈现逻辑 |

优先复用现有 Contracts.Builtins / Primitives.Builtins，不新增一个装所有内容的 Common，也不按命名空间批量新建程序集。可选领域如果出现真实的独立消费者，再决定是否拆专门的领域包。Gallery 同样应可选，具体包装位置在迁移其消费者时确定，不强行归入 primitive 执行模块。

## 设置的数据流

```text
项目自己的配置 / 玩家偏好 DTO + 自己的读取、UI、持久化
                     |
               组合根显式映射
          +----------+-----------+
          |          |           |
   PlaybackOptions  音频配置/混音值  文本呈现选项
          |          |           |
        Engine    AudioModule   Presenter
```

- Engine 只消费推进真正需要的几个值；配置采用小型强类型值对象，通过显式更新入口应用。无必要时不添加 provider/interface 层。
- 音频模块构造/更新时接收轨道定义与混音值，不从 Runtime.Settings 获取整个 ProjectSettings/GameSettings。
- 接收的配置与高层 DTO 隔离；通过显式更新提交一个一致的配置值，而不是由引擎观察任意可变设置对象。配置模型的序列化、存储 key、变更事件和 UI 均归高层。
- 文本速度属于文本呈现，全屏/窗口大小属于显示宿主，存档槽和缩略图属于保存服务/页面。
- 项目 DTO 无需继承框架基类，不将 `GameEngine<TSettings>` 一直传染到所有模块，也不以 `IServiceProvider` 取代 SettingsContainer 形成新的万能入口。
- 没有模块消费者后，删除 IGameContext.Settings。SettingsContainer/SettingsSection 若仅剩编辑器用途，则收敛到 Editor；不用为了保留 API 将其迁到新公共包。
- `GameContent.Settings` 和 Editor document 也要改：最小内容描述保存 Engine 必需数据；默认项目文档拥有自身配置。若 Editor 要读改自定义项目 section，使用现有 schema 能力做明确的序列化/表单适配；未理解的 JSON section 必须保留。
- 自定义配置必须贯穿打开、编辑、保存、导出、安装加载与预览。仅增加任意字典或把 DTO 改成 object，并不算支持完成。
- 无配置的普通偏好采用中性默认值；无效作者配置在加载/提交边界诊断。不要每条指令重新进行同样的配置验证。

## 音频分组与轨道（方向已确认）

保留 track 的队列、并发和溢出语义，但去掉固定业务分类。

- Track 和 mix group 均由开发者定义稳定 ID；分组是独立声明，不是 bgm/sfx/vocal 枚举，也不以名称推断行为。一个轨道暂归属零个或一个分组；未分组时使用中性分组值。暂不建设多重归属、总线树、路由图或复杂 mixer。
- 混音输入为 master gain/mute，加分别按 group ID 和 track ID 索引的 gain/mute。高层可以提供分组滑块、单轨滑块、两者同时提供或完全采用自己的 UI；内部执行能力不取决于默认 UI 暴露了哪些控件。
- 作用规则明确为：最终增益 = master × group × track × 轨道预设 gain × 播放项内容 gain，最后按后端支持范围规范化；任一层 mute 则静音。未配置/未保存的增益使用 1，mute 使用 false，轨道调节不会改写所属组或其他轨道的配置。
- 配置加载/显式更新时校验分组与轨道 ID 的唯一性和引用关系，再一致应用；声明中的错误引用应诊断，不能把自定义分组退回 SFX。日常播放不重复执行全套配置校验。
- BGM、SFX、Vocal、Ambience 等仅是示例声明。显示名称、本地化 key、预设增益和初始轨道集合由项目/模板提供。
- 未来其他设置也可声明针对 track/group 的明确操作；每项能力分别定义合成/覆盖/执行规则。例如音量相乘不代表并发上限或效果链也能同样合并。当前先实现 gain/mute，不引入任意 object 参数袋，不宣称已有分组 DSP 或所有设置的统一继承系统。
- 默认音频/语音 Composite 中的 bgm/vocal 默认值同样属于预设，需要同步检查；只移除 AudioBuses 会留下隐含依赖。
- 更新设置仍必须作用于当前 voice 和后续 voice；恢复使用当前项目轨道与当前玩家混音，不恢复后端句柄、播放时钟或旧偏好。

Sample 继续只声明音乐、音效、语音三个分组，默认设置 UI 按声明构造音量项；这套项目自有 DTO 与模块映射作为唯一完整接入示例。自定义 `ambience` 分组和同组多轨的场景通过测试证明，不为展示扩展能力给 Sample 增加第四个默认组，也不维护固定三类与自定义路径两套实现。

## 通用执行与可选领域

- 将 IPrimitiveDispatcher 归入 Core 的执行协议；注册/分发实现可暂留现有程序集。Runtime 不再为这个接口引用展示包。状态验证/恢复请求仍是执行生命周期能力，不要求 Engine 知道 presenter 类型。
- 将仅供管理使用的 IGameRuntime / GameRestorePoint 下移 Runtime；保留真正被模块消费的 IGameContext 和快照契约。不要为减少重复领域名合并整个 Core/Runtime。
- 默认 Layer/Timeline/粒子/音频等数据迁到其共享领域契约，纯采样器和静态领域校验可与该契约共置，Editor 和呈现复用同一个算法。
- 音频运行实现在 Runtime 内的直接使用者是 BuiltinAudioModule，具备移入内置执行模块的条件；后台命令顺序、drain 和状态同步语义必须一起保留。
- 场景句柄能力移交默认场景模块，IGameContext 不再强制 SceneInstances。通用 GameState 只管理注册表与同步，不直接维护 ISceneInstance 专用索引。
- 场景句柄跨类型唯一性仍须由场景领域集中保证，并在读档准备阶段验证；临时播放控制不能进快照。优先由模块从唯一持久状态派生查询，不另造一份可变持久索引。确需通用索引能力时再按真实消费者抽取，不能为了搬文件先造框架。

## 共享逻辑的判断

建议共享：Entry 的唯一参数描述、效果/音频效果的 JSON 基础约束、Timeline sampler、同一个 Engine 内快进/回放的执行批次机制。

继续分别拥有：不同领域的恢复/降级策略、音频与视觉效果的业务校验、宿主 UI 模型和运行状态、原始作者文档与已编译指令。

基础参数约束优先适配现有 FieldDescriptor / ValueValidation，不增加第四套 schema。Shader 元数据仍需要解析器将其语法投影为领域/参数描述；解析器不能被通用值校验替代。

## 备选与取舍

- 仅搬文件：调用链继续读取同一 GameSettings/ProjectSettings，不能解决用户模型替换；不采用。
- Core/Runtime 合并：会让自定义契约和编辑工具被迫引用执行实现/NCalc/日志依赖；不采用。
- 只保留接口的极小 Core：纯算法与共享模型被迫散落，容易增加包装与重复；不采用。
- 项目模型 + 小型模块输入 + 可选默认领域：需要一次破坏性迁移，但让功能边界可验证；推荐。

## 实施计划

完整任务、依赖、风险、验证与退出条件统一维护在 [phase-plan.md 的 R1–R8](phase-plan.md#后续整体重构)：执行协议归位→设置消费解耦→自定义音频→配置完整链路→可选领域剥离→参数体系→执行流程→集成验收。

整理整体计划时，将可独立验证的执行协议调整提前，避免后续领域迁移反复改变依赖；将运行端设置解耦与作者配置完整链路拆成两个有明确出口的阶段。它们共同完成同一端到端目标，中间阶段不宣称自定义配置链路已验收。

阶段可以因依赖拆成更小批次，但每批都要完成仓库调用方迁移；不保留旧设置适配器维持兼容。完整 schema/包格式变更必须明确版本，不将格式不兼容伪装成默认值回退。

## 验收影响

- 只用自定义 primitive 的最小运行，不需要 GameSettings/ProjectSettings、场景模块、音频、Gallery 或 Avalonia。
- 项目自己的 DTO 可控制推进、文本速度和自定义 ambience 混音；框架无需修改固定 switch。
- 同组两条轨道中，调整分组同时影响两者，调整单轨只影响该轨；master/group/track 静音和增益组合符合规则，更新同时影响现有和后续 voice。Sample 默认仍只有音乐、音效、语音三个分组。
- Editor 修改已知字段后，自定义配置在保存、撤销/重做、导出、安装加载与预览中完整保留。
- 自定义轨道的播放、静音/增益热更新、队列、恢复和旧 scope 取消继续正确。
- 场景跨类型句柄冲突、临时实例不入档、读档候选验证、旧 scope 隔离仍有回归覆盖。
- Runtime 不引用默认展示/领域实现；契约项目不引用 Runtime。架构测试除程序集引用外，检查内核不得重新依赖具体设置和内置视觉/音频类型。
- 不以文件移动数量或行数下降证明架构完成；以最小自定义项目和端到端配置路径证明。

R1–R3 已按计划实现并验证，相关当前事实已同步 docs/spec/manual；其余目标尚未实现，不视为当前规范。上轮提交及各阶段环境验收限制以 summary.md 为准。

R4 按可验证依赖拆分：R4a 先删除 settings 中的变量声明副本与仓库 Load 的无用名称/设置输入；Graph 是唯一变量持久源。R4b 再落地版本化配置 section 协议、项目契约的 schema 声明和默认模型归属；R4c 验证编辑/历史/保存/导出/安装/预览与项目自有 DTO。R4a 不等于完整自定义配置链路完成，GameContent 与作者模型尚存的固定绑定继续保留为 R4b/R4c 工作。

R4b 实施边界：Core 只提供版本化配置文档和 section 描述协议，值仍由项目 DTO/schema 定义，复用 FieldDescriptor/ValueValidation；它是作者/内容交换数据，不是运行时服务容器。读取不写默认值，提交前验证，未知 section/字段保留。默认项目 DTO 归可选 Contracts.Builtins，组合根显式投影；GameContent/作者聚合只持有配置文档。项目契约公开 Configurations 目录；加载器接收目录与开发语言选择委托，不依赖默认 DTO。R4c 补齐通用 section 编辑 UI、历史和最终自有 DTO 接入验证。
