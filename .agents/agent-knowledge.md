# GalNet Agent Knowledge

这是 GalNet 项目的长期 agent 上下文入口。这里只保存经过验证、以后工作仍然有用的项目事实、边界和经验索引；不要写入对当前任务没有复用价值的对话流水账、秘密或未经确认的猜测。

## 使用方式

开始工作时先读取本文件，再按当前任务读取仓库 `.agents/lessons/` 中相关的经验和 `.agents/skills/` 中匹配的 skill。细节以仓库 `docs/` 下的正式文档为准。

经验在发现时立即记录，feature 结束时再整理和晋升；不要等到上下文结束才回忆。

## 项目事实（已从仓库文档确认）

- 宿主分层已落地：GameHost 提供 GameSession/IGameRun/GameRun、PlayerSession、PlayerPreferences/PlayerStartup；Avalonia.GameHost 提供默认页面会话、运行绑定、展示组合、资源预加载和设置/输入/窗口适配。Sample 仅保留项目配置、启动适配、本地化和调试。中立应用接口在 Presentation.Abstractions.Application，共享音频后端在 Media.LibVlc；项目删除与 ProjectTools.Windows 已移除。见 docs/spec/architecture.md。

- 解决方案入口是 `GalNet.slnx`。
- `docs/spec/` 描述已经实现的稳定事实；`docs/manual/` 描述二次开发路径；`features/` 保留实现过程和历史。
- Runtime/Core 应保持与 Avalonia、WPF、Skia、具体文件系统和其他平台实现解耦。
- Entry 契约与执行已分离：IEntryModule 只含 schema/composite，IPrimitiveModule 按领域共享状态并贡献完整指令 ID；GameRegistration 独立注册，AddBuiltins 为完整内置运行入口；独立 GalNet.Contracts.Builtins 只依赖 Core，CreateAllModules 返回 13 个组件模块。可选 schema、部分内置实现或全自定义；仅完整 ID 排他。同前缀和 schema 模块名可重复。音频用 AudioRuntime + IAudioPlaybackHost 组合，不强制继承。见 docs/spec/runtime.md、docs/spec/entry-types.md 和 docs/manual/。
- 当前 GameSnapshot 版本为 4，不兼容旧版；持久数据在注册式 GameState 表中，快照为 StateId → version/JSON payload；读档先验证，再取消旧 scope 并替换整个状态根。Engine 不拥有执行 view，宿主先释放 Engine，再异步释放 view 与后端。
- Engine 提供终态 StopAsync 和 StateChanged，覆盖玩家推进、Choice 与阻塞实例自然完成；GameHost 仅协调应用会话，不复制 Engine 执行机制。AvaloniaGamePresenters 只组合端口，AvaloniaGamePresentation 管理平台资源。见 docs/spec/runtime.md 和 docs/spec/architecture.md。
- Presentation.Defaults 已删除，空 NullGameView 位于展示抽象的 View 命名空间；当前程序集布局见 docs/spec/architecture.md。GameContent.Settings 包含项目设置，项目/安装加载器与两个 Sample 已接通自定义音频轨道配置。
- 旧的 `GalNet.Control` 与 `GalNet.Control.Abstraction` 已完成迁移并删除；当前共享 Avalonia 游戏页面唯一入口是 `GalNet.Avalonia.GameView`，页面架构见 `docs/spec/control.md`。
- 持久条目先在 `GameState.Update` 中修改唯一数据记录，再通知展示层；模块通过 PrimitiveRegistry.State/Restore/ValidateState 声明状态、恢复依赖和校验，宿主使用展示抽象 StateRestoreSession 统一恢复、等待和逆序清理。旧 SceneState/Active* 镜像与 Builtins 中央 replay 已删除。
- 视觉扩展应通过展示抽象和渲染端口接入；不要把平台类型或渲染资源泄漏进 Core/Runtime。
- 新增 feature 的长期实现意图记录在其自身 feature 目录；只有已验证的稳定事实才合并到 docs/spec/。

- 玩家变量桥接实现为 Runtime.Variables.PlayerVariableService，存储通过 IPlayerVariableStore 注入；Variable.Clone 保留 Uid，VariableValue 不可变，可共享。Runtime loader/SaveManager 不执行文件 I/O。见 docs/spec/architecture.md。
- Runtime.I18n.GameTextResolver 提供 culture-first、父文化与默认文化回退，只依赖宿主查询委托；DynamicLocalization/JSON 适配留在 Sample 的 GameLocalizationProvider。见 docs/spec/i18n.md。
- Assets 的 AssetFileMaterializer 经 IAssetManager acquire 字节实体化媒体；Core 的 AssetFileLease 持有文件租约。Sample 将同一实例供音频/视频/Gallery 使用，最后租约释放才删除文件。见 docs/spec/assets.md。
- 已读进度 MarkRead 只写 Runtime ReadProgressIndex；文件实现默认 2 秒批量提交，失败保留 dirty revision 并重试。Engine StopAsync、Sample 存档和进度对象销毁均等待 flush，失败向调用者传播；后台失败可读 LastPersistenceError。见 docs/spec/runtime.md。

## 文档分层

- `features/<feature-id>/`：一次实现的需求、设计、阶段计划、审核和人类可读总结。
- `.agents/lessons/`：agent 专用的踩坑记录。
- `.agents/skills/`：项目 skill 的可版本化源文件；可复用经验晋升后在这里形成独立 skill。
- Codex 的用户级 skill 发现目录保存运行时副本；源文件变化后使用 `galnet-sync-skills` 同步，避免 agent 使用旧版本。
- `docs/spec/`：当前系统事实。
- `docs/manual/`：二次开发流程与框架接入指南。

## 当前工作

- [F-20261015-02 Editor 创作工作区](../features/F-20261015-02-editor-authoring-workspace/phase-plan.md)：P1a/P1b/P2/P2i/P3 已验证。项目单栈、事务保存、基础 Graph、分支稳定项操作、深复制粘贴与四语已接通；下一阶段 P4a 文件事务/文档协调，游戏预览另立 feature。当前实现见 docs/spec/editor-graph.md 和 editor-history-and-save.md。

- F-20261015-03 模块化游戏状态：GameSnapshot v4；状态 catalog 随 view 组合，Engine 接入；中立 Layer/EffectInstance/ParticleEmitterInstance 直接作为唯一可变数据记录，属性变更必须在 State.Update，编码形成隔离快照。LoopAnimationState 保存类型化 request/plan；停止控制与播放 identity 仅运行期持有。音频 AudioTrackState/AudioSEffectState 包含暂停、队列与空闲效果，所有轨道按稳定检查点从头恢复；不恢复 seek/淡化时钟，玩家设置不回滚。恢复首帧不重复一次性事件，暂停声音在 Resume 前不启动后端。七个相关项目 665 项测试、解决方案构建与 CLI 实际项目校验/编译通过；新档验证见 [feature summary](../features/F-20261015-03-modular-game-state/summary.md)。


- [F-20261015-01 primitive 注册简化](../features/F-20261015-01-primitive-registration/summary.md)：PrimitiveImplementation 已移除，IPrimitiveModule.Register(PrimitiveRegistry) 在 BuildView 时直接登记 ID → 实例工厂；Register<T> 校验 DTO 并逐次解码独立 Options，Immediate/Async 保留短回调。注册结束或失败后冻结；RegisterDeclared 按 catalog 交集筛选并保留最终契约校验。复杂 Builtins 实例接收 context/Options，场景重放仍使用 request 构造路径。

- [F-20261013-02 参数与 Sample 简化](../features/F-20261013-02-parameter-and-sample-simplification/summary.md) 已完成。Core 的 ValueDescriptor/FieldDescriptor、ValueCodec/ValueValidation 为中立值事实；开放 EditorId/SchemaForm/registry/lease 由 Avalonia 宿主/主题组合，Editor 直接绑定作者文档，设置保留独立异步 apply/save。generator 生成有序字段与成员访问，仅为构建期 Analyzer；源基类字段/default/converter 可合并并检查冲突。全量 43 条 Entry 中 38 条参数 Entry 已 DTO 化，5 条无参数空 schema；ID 位于 BuiltinEntryIds，Composite 用 typed options/子 DTO writer，普通字符串 reader/34 个作者类壳已删除。partial/full 校验保持分开，专属资源/表达式编辑器仍消费唯一 schema；省略/null/default 仍独立。SampleCustomization 集中项目选择，保留 Session/Runner/Presentation 等所有权。作者路径为 Immediate<T>、Async<T>、For<T>、typed Composite；AsyncPrimitiveInstance 默认传播失败，Builtin 展示明确处理错误，PrimitiveTaskScope 完成移除/保留错误。TextAdvancePrimitiveInstance 保留展示结束与玩家推进分离，原 Dialogue Lock 语义保留；Sample 的顺序清理继续尝试所有独立释放并聚合错误，Session 重复关闭复用结果。最终 11 项目 898 用例、解决方案编译、契约独立部署、CLI 编译 4 个 Group 和 16→20 Composite 完整产物对照通过；原两项 UI Flush 挂起已由真实 UI pump 测试解决。4b 计入公共机制后生产净减 51 行，5d 净减 155 行；新增表单/generator 的成本单列，不声称全仓净减少。原生媒体人工全程未做，完整范围与自审见 summary/review。全量设置 DTO、自动框架与 Core 领域/快照重构继续暂缓。

- [F-20261013-01-editor-cli-operations](../features/F-20261013-01-editor-cli-operations/summary.md) 已实现独立内置/项目契约、项目 scope catalog、面向 agent 的 CLI 原始查询/校验/单 Group 编译、共享生命周期与无损保存、Windows 回收站和单平台截图样例；Phase 1–7 已验证并提交；审核与当前边界见 summary/review。CLI 发现使用 help --json，不保留旧别名或默认内置 fallback。契约是可信 C# 代码，动态加载不构成沙箱。

- [F-20261011-03-theme-owned-views](../features/F-20261011-03-theme-owned-views/summary.md) 已移除 GameView 普通页面和默认玩家样式。主题参数必需，最终映射校验完整性；Page 仅保留 GameShell/GamePage/ScreenshotDialog 行为。Themes 提供布局、通用样式与工具栏；截图独立构造/释放，普通页面 Scoped。Editor 显式 Classic 并采用 ThemesAssembly 别名。

- [F-20261011-02-player-page-layout](../features/F-20261011-02-player-page-layout/summary.md) 已补齐三套内置主题的全部普通页面 XAML View，主题目录自行维护布局、共享 ViewModel/命令；页面无外框，主题内导航位置一致。对白名字比正文大4，Classic/Paper 26/22、Minimal 24/20；默认 GameView 页面回退已由 F-20261011-03 删除。

- [F-20261011-01-game-ui-themes](../features/F-20261011-01-game-ui-themes/summary.md) 已完成启动期主题组合：可选 Themes 提供三布局/三配色，页面顺序已由 F-20261011-03 收敛为共享 GamePage→主题→宿主→完整性校验；GamePage 是稳定 TemplatedControl，四个 PART_* 承载其自有内容，派生页与 Runtime 保持同一 Scoped 实例。样式应用于玩家根，官方主题局部 Fluent 与宿主 Semi 隔离。当前无运行期切换；配置/契约见 Themes README 和 docs/spec/control.md。

- [F-20261009-02-video-entries](../features/F-20261009-02-video-entries/feature.md) 已实现剧情视频图层、阻塞分段 seek、停止/替换清理和非阻塞视频定义恢复。平台视频必须经 AssetManager 获取，解码帧通过租约进入场景管线；播放游标不入存档。验证见 feature summary。

- [F-20261010-02-runtime-i18n](../features/F-20261010-02-runtime-i18n/summary.md) Phase 1–5 已实现。Sample 用 Core 2.1.2 + ReflectionBinding；每 JSON 独立 provider，culture-first 查询内嵌/包内/External/额外源；首次选择在 Engine 前原子保存 locale/count。设置切换不重译当前剧情。安装器保留 External 并拒绝覆盖不同包，导出不包含第三方文件。当前契约见 `docs/spec/i18n.md`，人工 GUI 全程与原生媒体 opt-in 验证限制见 summary。

- `F-20260926-01-gallery-meta-pages` 的实现、正式文档与示例 Gallery 解锁 smoke test 已完成；其外部阻塞项 `EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 已由 `F-20260929-04-quality-baseline` 修复，Gallery feature 可按自身流程收尾。
- [F-20260916-02-audio-system](../features/F-20260916-02-audio-system/feature.md) 已完成首期：轨道逻辑、`AudioRuntime`、`IAudioPlaybackHost`、Avalonia LibVLC host、语音文本 composite 和 Sample 验收已落地。Runtime 只能传 asset ID；平台 host 必须经 `IAssetManager` 实体化临时媒体文件。后续 [F-20261009-01-track-seffects](../features/F-20261009-01-track-seffects/feature.md) 增加 host 注册的 `SEffectCatalog`、`seffect.gain` 与基于 LibVLC equalizer 的 `seffect.lowPass` / `seffect.highPass`；后续已实现轨道/SEffect 存档与玩家音量，见 F-20261015-03；item scope、音频参数动画和角色默认轨道仍未实现。
- `F-20260929-01-compiled-content-pipeline` 正在实施；粒子 Primitive、Runtime 状态与 Presenter 桥接及存档后的 effect/particle 展示重放已经验证，完整 Sample/Headless smoke 与 Phase 4 收尾仍待完成。
- [F-20260929-15-replay-save-format](../features/F-20260929-15-replay-save-format/feature.md) 已暂缓：正式存档继续使用完整稳定快照；后续节点解锁/快速进入能力应另立 feature。
- `F-20260929-03-maintainability-roadmap` 及其后续 `F-20260929-14-format-baseline` 已完成。资源协议归 Core.Assets；内容、保存、变量、Gallery 组合和进度端口归 Runtime；Editor 设置/退出协议归 Editor.Abstraction；展示端口使用 `GalNet.Presentation.Abstractions.*`，推荐内置类型使用 `GalNet.Primitives.Builtins`。`GalNet.Storage.Abstractions` 与无消费者历史接口已删除，`ISaveService` 已统一为异步可取消 API。测试已按 10 个生产/运行边界拆分，跨外层场景集中在 `GalNet.IntegrationTests`，Architecture 门禁同时约束生产与测试项目引用。Editor Workspace、Avalonia presenter、Sample 会话和 Editor 扩展边界均已按路线图收敛。生产项目的物理路径按 Shared、Presentation、Infrastructure、Editor、Samples 和 Launcher 分组。CI 使用 solution 级 whitespace 与 `IDE0005` import 检查阻止格式基线回退。
- 后续 feature、验证命令和新经验在实际工作中补充，并保留来源链接或 feature ID。

## 维护规则

1. 只写已经观察到或从正式文档确认的事实。
2. 事实变化时更新本文件的对应条目，并注明来源。
3. 一次性事故先写 lesson；只有可复用、重复出现或高风险的规则才晋升成独立 skill。
4. 如果项目架构变化，检查并标记受影响的 lesson 和 skill，而不是继续沿用旧规则。

## 经验索引

- [局部编辑的全局位置](lessons/L-20261017-01-local-edit-global-indices.md)：删除局部集合中的中间项后，After 快照必须同步重算全局索引；比较完整作者快照验证撤销。

- [批量预加载的部分成功租约](lessons/L-20261016-06-partial-preload-leases.md)：每次成功获取立即登记；WhenAll 失败仍须释放其他已成功任务的租约。

- [分支节点高度变化必须发布连线布局刷新](lessons/L-20261016-04-graph-edge-layout-refresh.md)：连接器重建时输入集合事件可能早于输出集合重建，节点布局完成后必须单独通知入边刷新路径。

- [Editor 补全控件的 nullable 失焦边界](lessons/L-20261016-05-editor-autocomplete-null-boundary.md)：可编辑 ComboBox 失焦可将空输入同步为 null；业务提交入口必须先处理 nullable 输入，属性回调中的 `??` 不会规范化属性自身。

- [Editor 语言绑定实际刷新](lessons/L-20261016-03-editor-localization-binding.md)：静态文案显式监听 CurrentCulture，动态 key 同时监听路径；真实窗口切换验收，关闭后释放绑定。

- [Headless 拖动按键状态](lessons/L-20261016-01-headless-drag-modifiers.md)：MouseMove 必须显式携带 LeftMouseButton，不能从 MouseDown 假定状态持续。
- [结构撤销材料捕获时机](lessons/L-20261016-02-structure-baseline-capture.md)：结构准备在取得发布门后读取 Working/Saved，避免正在发布的保存推进基线后恢复旧内容。

- [Editor 历史与发布锁顺序](lessons/L-20261015-04-editor-history-publication-lock-order.md)：先捕获历史，再尝试发布；发布门忙时释放捕获租约，禁止保存与结构编辑互相等待。

- [停止后端前撤销播放身份](lessons/L-20261015-03-audio-stop-identity-order.md)：Stop 可同步重入终态事件；批量停止先清空旧映射，避免旧队列被推进。


- [UI 测试异步观察异常](lessons/L-20261015-02-ui-tests-await-failures.md)：UI 回调中直接 await + try/catch，避免同步 Assert.ThrowsAsync 阻塞依赖 UI pump 的任务。

- [原语结果先于完成通知发布](lessons/L-20261015-01-primitive-completion-result-order.md)：Engine 在完成通知续跑时读取展示任务；先发布错误再通知，任务和通知是不同同步点。

- [连续滑块应走实时预览而非异步提交](lessons/L-20261014-09-live-slider-preview.md)：音量实时更新但不逐刻度提交；空闲后合并持久化，控件保持启用。

- [显式字符串 codec schema](lessons/L-20261014-08-string-codec-schema.md)：STJ 的自定义 string converter 导出不透明 schema；marker 已承诺 wire 类型时显式保留字符串描述。

- [局部 Entry 校验与完整指令校验](lessons/L-20261014-07-partial-entry-validation.md)：完整 validator 依赖 Normalize 的默认/必备字段，逐字段创作使用显式 partial 回调，编译检查仍保留。

- [DTO 保留专属 Entry 编辑器](lessons/L-20261014-05-dto-keeps-dedicated-entry-editors.md)：FieldDataType 不代表所有字段都有共享工厂，补全、资源及表达式控件继续消费唯一 schema。

- [Entry fixture 显式选择 schema](lessons/L-20261014-04-entry-fixture-needs-explicit-schema.md)：legacy/DTO 分支不要借用会继续迁移的 builtin 作为固定测试模型。

- [表单测试必须验证真实视觉树](lessons/L-20261014-03-form-tests-must-mount-editors.md)：工厂输出不代表模板已挂载，动态选项模板必须处理 null 中间态。

- [编辑文本必须携带完整值契约](lessons/L-20261014-02-editor-text-needs-value-contract.md)：string 与字符串编码的 enum 文本表示不同，转换两端都必须使用完整 JSON 配置。

- [受限环境中的 .NET/Avalonia 构建](lessons/L-20260922-01-dotnet-build-sandbox.md)：禁用 Roslyn shared compilation，并关闭 Avalonia build telemetry。
- [读档必须隔离旧运行实例](lessons/L-20261010-02-restore-run-scope.md)：取消与场景对象隔离同时使用；句柄复用的清理需校验实例 identity；快照不能共享 mutable 对象。
- [删除项目后检查依赖锁图](lessons/L-20261010-03-lock-graph-after-project-removal.md)：检查 lock 内容；缓存可能保留旧项目，需要对受影响 roots 强制重算再 locked restore。
- [Engine 停止覆盖所有续跑](lessons/L-20261010-04-engine-stop-continuations.md)：首次推进返回不等于运行结束；停止须取消 scope、等待内部 continuation，统一发布终态。
- [平台无关的进度契约归属](lessons/L-20260922-02-core-progress-contract.md)：已被 F-20260929-07 取代；当前进度端口由实际消费者 Runtime 拥有。
- [受限环境中的平台 SDK 枚举](lessons/L-20260925-01-platform-sdk-sandbox.md)：全解决方案构建可能只因 Android/Browser/iOS 的 SDK 探测读取用户目录失败；应区分平台环境限制和业务项目编译结果。
- [受限环境中的 Avalonia licensing 枚举](lessons/L-20260926-01-avalonia-license-sandbox.md)：Avalonia BuildServices 即使关闭 telemetry 仍可能读取用户级 licensing tickets；应与业务编译失败区分。
- [首次对话的 Avalonia 模板就绪](lessons/L-20260927-01-dialogue-template-startup.md)：阻塞对话不能因模板部件尚未就绪而静默完成，否则引擎会错误地跑完整个流程。
- [样例资源元数据必须随内容版本化](lessons/L-20260927-02-version-sample-meta.md)：被 `.galgroup` 使用的 `.meta` 映射不可被通用忽略规则吞掉。
- [平台媒体路径必须从资源句柄实体化](lessons/L-20260927-03-asset-handle-materialization.md)：页面不能绕过 `AssetManager` 扫描资源目录；文件路径只能由 acquire 的内容临时生成。
- [粒子端到端桥接验证](lessons/L-20260929-01-particle-presenter-unwired.md)：schema 和平台 presenter 存在不代表剧情 primitive 已可执行；必须验证 Runtime 状态和 presenter 桥接。该问题已在 `F-20260929-01` 解决。
- [持久场景状态必须显式重放展示](lessons/L-20260929-03-persistent-scene-presentation-replay.md)：Runtime 快照恢复不会自动重建渲染端对象；每种持久场景对象都必须覆盖状态到 Presenter 的重放路径。
- [动画存档保存稳定语义而非播放游标](lessons/L-20260929-04-animation-save-semantics.md)：一次性非阻塞动画保存末值，循环动画保存第 0 帧状态与完整定义并从头重播，阻塞动画沿用此前稳定快照。
- [Skia atlas 变换使用 source 局部坐标](lessons/L-20260929-05-skia-atlas-local-transform.md)：`DrawAtlas` 的旋转缩放中心应使用 source rect 的宽高，不能重复带入 atlas 偏移。
- [Sample 调试清空的语义](lessons/L-20260929-01-sample-debug-reset-scope.md)：顶层“清空游戏数据”重置玩家状态；日志清空仅属于日志面板。
- [Sample 调试进程内销毁的导航顺序](lessons/L-20260929-02-sample-debug-navigate-before-teardown.md)：已被用户复现推翻；仅调整导航顺序不足以避免黑屏。
- [Sample 调试重置后的重启](lessons/L-20260929-09-sample-debug-restart-after-reset.md)：当前调试清空和资源重载在必要操作后重启进程，避免进程内 Runner 销毁路径。
- [Sample Debug 源码的忽略规则](lessons/L-20260929-10-sample-debug-source-ignore.md)：全局 `[Dd]ebug/` 会吞掉该目录的新源码，必须精确反忽略。
- [实时日志的尾部追随](lessons/L-20260929-11-log-tail-extent-is-not-user-scroll.md)：追加内容只改变 Extent；只以 Offset 变化判断用户是否离开底部。
- [格式门禁必须建立在干净基线上](lessons/L-20260929-06-format-gate-clean-baseline.md)：Windows 仓库在统一行尾前直接启用 `dotnet format` 会把既有行尾、using 和空白债务混入功能改动；先做独立机械格式化，再启用阻断式门禁。
- [测试项目不要用独立 MSBuild 进程并行构建共享依赖](lessons/L-20260929-07-parallel-test-shared-obj-lock.md)：多个 `dotnet test` 进程会争用共享生产项目的 `obj` 输出；全仓测试应顺序执行或由单一 MSBuild graph 调度。
- [项目移动后必须清理旧构建中间产物](lessons/L-20260929-08-project-move-stale-restore-path.md)：移动 .NET 项目后先删除随项目移动的 `bin/obj`，再从新路径 locked restore，避免旧绝对路径缓存重建旧目录和 lock 文件。

- [动态视频图层的加载与帧生命周期](lessons/L-20261010-01-video-layer-lifetime.md)：待加载源先登记，hide/replace 取消加载；帧租约与独立播放 identity 防止晚发布和旧完成误清理。
- [DynamicLocalization Avalonia 主版本兼容性](lessons/L-20261010-05-dynamiclocalization-avalonia-version.md)：2.1.0 与 2.1.2 的 Avalonia extension 都不适用于当前 Avalonia 12 Sample；后者虽能编译仍在启动时调用已移除的 Binding 构造函数。Sample 使用 Core + 自有 `ReflectionBinding` 适配层。
- [JSON provider 的整 culture 替换](lessons/L-20261010-07-json-provider-replaces-culture.md)：2.1.2 的 LoadJsonString 不增量合并，也不做父 culture 回退；分文件需独立 provider 与宿主有序查询。
- [Render 内的粒子耗尽清理](lessons/L-20261010-08-render-pass-particle-drain.md)：confirmed；UI Dispatch 在 UI 线程同步执行，不能作为渲染回调清理的延迟队列；使用保证异步的 `Post`，并以 emitter identity 防止旧回调删除替换实例。

- [Headless session cleanup](lessons/L-20261011-01-headless-session-cleanup.md)：async Dispatch 测试使用 await using，避免同步 Dispose 等待同一个 session worker；动态 Brush 预期值要在资源宿主中解析或比较语义 Color。

- [Editor Avalonia aliases](lessons/L-20261011-02-editor-avalonia-alias.md)：Editor 引用 GalNet.Avalonia.* 时采用程序集别名，避免既有 Avalonia 限定名称被相对命名空间解析遮蔽。

- [运行中的 Editor 输出文件锁](lessons/L-20261013-01-running-editor-output-lock.md)：当前运行的 .NET Host 可能锁住 bin DLL；保留用户程序，使用 feature 专用 --output 目录验证。
- [契约 DLL 流式加载](lessons/L-20261013-02-contract-stream-loading.md)：Unload 不保证立即解除 Windows 映射锁；项目主 DLL 和自有依赖用流式加载，保持共享框架类型身份。

- [构建期间保持源码快照稳定](lessons/L-20261013-03-build-source-snapshot.md)：验证脚本结束前不修改其依赖图源码；交错后完整重跑。

- [Headless 异步 Dispatch 的重载选择](lessons/L-20261013-04-headless-async-void-dispatch.md)：12.0.5 无 Func<Task> 重载，显式 Dispatch<bool> 等待 post-await 断言及实际产物，禁止 async void 隐藏失败。

- [用实际 producer 生成 metadata fixture](lessons/L-20261013-05-metadata-producer-fixtures.md)：AssetMetaCodec 默认输出 id，读取规则大小写无关；查询返回原 ID 拼写并保留实际 pointer，真实 sample smoke 防止测试与实现共享错误假设。
- [Sample 复用页面前先重置 NVL](lessons/L-20261013-06-sample-nvl-reset-order.md)：页面重置依赖仍订阅的 dialogue presenter 清理 NVL；Runner 应先重置页面再释放 presentation，并复位 NVL 可见状态。
- [包装/嵌入 schema 的局部引用](lessons/L-20261014-01-schema-local-references.md)：保留 exporter 对象结构，外层值约束不能污染递归子类型；移动 schema 时按最终 JSON Pointer 重定位局部引用并实际解析验证。

- [Headless XAML 构造前加载主题](lessons/L-20261014-04-load-theme-before-xaml-controls.md)：资源事后可查不代表已初始化控件的空模板查找会刷新；实际 Editor 面板测试已复现并验证。
- [Headless 会话发布竞态](lessons/L-20261014-05-headless-session-publication-race.md)：12.0.5 StartNew 的 task 闭包发布存在竞态；Presentation 通过官方程序集缓存会话避免逐测试释放 worker，Dispatch 保持 PerTest 隔离。

- [Entry 默认值使用完整 codec](lessons/L-20261014-06-entry-defaults-use-value-codec.md)：JSON 字符串编码不代表 CLR 字符串；无法无损投影的默认值保留在字段描述，不填入旧 Defaults。
