# F-20261010-02 实施 Phase Plan

**设计依据：** [design.md](design.md)

**状态：** implementation in progress

## Phase 1 — Avalonia Sample UI 本地化基础

**状态：** verified（2026-10-10）

**目标：** 在 Avalonia Sample 宿主中引入固定版本的 DynamicLocalization.Core，加载程序集内嵌 UI JSON，使用 Sample 自有 Avalonia 12 绑定适配层支持运行时 culture 切换，但不改变游戏内容格式。

**前置条件：** 确认目标 NuGet 版本与 Avalonia 目标框架兼容。

**涉及模块：** `GalNet.Sample.Avalonia`、Sample composition root、必要的 Presentation 测试项目。

**任务：**

1. 固定 `DynamicLocalization.Core` 2.1.2；以小型 characterization test 验证嵌入 JSON、culture change、逐文件动态加载与重复 key 的后加载覆盖语义。
2. 在 Sample composition root 注册唯一的 culture service，嵌入 `ui.*` JSON，并通过 Sample 的 Avalonia 12 绑定适配层暴露本地化文本。
3. 保证 UI culture 改变会刷新现有页面绑定，且未加载游戏时仍能显示 bootstrap UI。

**测试与验证：**

- 嵌入 `zh-CN`、`en-US` 等 UI 资源可被解析。
- culture 切换后 UI binding 刷新。
- 两个按确定顺序加载的 JSON 含同 key 时，后加载值生效。

**风险：** 库的多 provider 优先级可能不同于预期；本 Phase 的 characterization test 是后续所有覆盖规则的依据。

**退出条件：** Sample UI 独立完成嵌入资源加载和切换；库的加载/覆盖行为有自动化证据。

**验证证据：**

- `dotnet build src\Samples\GalNet.Sample.Avalonia\GalNet.Sample.Avalonia.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:AvaloniaTelemetryOptOut=true` 成功；移除 `DynamicLocalization.Avalonia` 2.1.2 后输出不含其 DLL，且仅有既有 Avalonia runtime-loader 可达性警告。
- `dotnet test test\GalNet.IntegrationTests\GalNet.IntegrationTests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:AvaloniaTelemetryOptOut=true --filter FullyQualifiedName~SampleLocalizationTests`：1/1 通过，验证内嵌中英文 JSON 与后加载覆盖。

## Phase 2 — 稳定游戏文本契约与人物表

**状态：** verified（2026-10-10）

**目标：** 建立人物 catalog 与内置文本字段的 key 契约；raw 中已有 key 在编译、复合展开和 Runtime 之间保持一致。

**前置条件：** Phase 1 已确认库行为；不依赖外部语言文件。

**涉及模块：** `GalNet.Core`（人物与 key 契约）、`GalNet.Runtime`（compiled group loader）、`GalNet.Primitives.Builtins`（dialogue/NVL/choice）、Editor 共享编译/验证层及相关测试。

**任务：**

1. 定义 character catalog DTO/验证：唯一 CharacterId、必填 DisplayNameKey；以模块参数接收 catalog。文件加载与 Editor 自动补全接线留在 Phase 3。
2. 编译前后继续用 `Entry.Id` 表示顺序位置，不增加 SourceEntryId 持久化字段。key 由未来 Editor 根据 raw 阶段的 ID 生成一次并保存为 content；本期 compiler 原样传递。
3. 将内置 `speaker` 统一解释为 CharacterId；内置 content/NVL/choice 文本统一解释为 key。未知人物 ID 是内容错误；默认语言必需 key 校验归 Phase 3。
4. 保持自定义 entry 不受通用字符串推断影响；其实现自行决定参数的本地化语义。

**测试与验证：**

- 在 group 插入或重排条目后，既有 raw entry ID 对应的 key 不变。
- voice-text composite 展开后，文本 primitive 保留原 key。
- 有效/重复人物 ID、未知 speaker CharacterId、key 化 choice/dialogue 的验证测试。

**风险：** 现有项目使用 speaker 显示名。迁移或拒绝策略必须在实现时以明确诊断处理，不能静默改变显示名称。

**退出条件：** 内置游戏文本和人物显示名经 resolver 解析，编译和展开不改写 content key；无需 Runtime source identity。

**验证证据：**

- Core i18n 测试 12/12 通过：人物 ID/姓名 key 必填、重复 ID 拒绝与生成 helper。
- Builtins 全组 52/52 通过。
- Integration 定向测试 28/28 通过：编译展开与插入后 key 不变、姓名/对话/NVL/choice 解析、未知人物拒绝及原有 loader/engine 行为。
- 未新增 compiled 或 Runtime 的 SourceEntryId 字段，未改写 raw content，现有文件格式保持不变。
- 人物 catalog 文件加载、Sample 的 catalog/resolver 接线和项目示例迁移待 Phase 3；现有含非空 speaker 的宿主需提供 catalog。

## Phase 3 — 包内游戏 i18n 加载与 Runtime resolver

**状态：** planned

**目标：** 从项目/安装内容的 `I18n/` 递归加载人物 catalog 与 per-culture JSON，并把同一 culture service 适配为 Runtime `ITextResolver`。

**前置条件：** Phase 2 契约和测试通过。

**涉及模块：** `GalNet.Runtime.Content`、`GalNet.Storage.FileSystem` 内容 providers、Sample game/session composition、`GalNet.Core.Services.ITextResolver` adapter、集成测试。

**任务：**

1. 实现 `I18n/catalog/characters.json` 与 `I18n/<culture>/**/*.json` 的安全递归枚举、路径规范化和 JSON 验证。
2. 在项目和安装内容 provider 中加载 i18n 描述，向 Sample composition 暴露足以建立 resolver 的内容；保持 `I18n/` 不进入 `IAssetProvider`。
3. 按明确顺序逐文件调用 DynamicLocalization 动态加载，不在 GalNet 中重写 JSON 合并算法。
4. 实现 `ITextResolver` adapter：请求 culture → 父 culture → `ProjectSettings.TargetLocale` → key；Core/Runtime 不引用第三方包类型。
5. 对默认语言做完整性验证，至少覆盖人物、group entry 与 branch key。

**测试与验证：**

- Project 与 installed content 对相同 i18n tree 得到等价解析结果。
- dialogue、NVL、choice 与人物名按当前/父/默认 culture 回退。
- 默认语言缺 key 被内容验证拒绝；非默认语言缺 key 正确回退。
- `I18n/` 文件不被资源 asset provider 枚举。

**风险：** 载入时机必须先于 GameEngine 建立；包内 UI/game key 的命名空间冲突应由 `ui.` 保留前缀防止。

**退出条件：** 仅靠包内内容，Sample 能以所选 culture 运行 key 化剧情和人物名，并保持 Runtime 无第三方包依赖。

## Phase 4 — 外部 JSON 覆盖源

**状态：** planned

**目标：** 让已安装游戏递归加载 `I18n/External/<culture>/**/*.json`，以确定性方式覆盖包内游戏和 Sample UI 文本。

**前置条件：** Phase 3 完成且逐文件加载覆盖语义已由 Phase 1 锁定。

**涉及模块：** 安装/内容加载边界、Sample composition、文件系统集成测试；必要时安装器。

**任务：**

1. 创建并保留 External 目录；更新/重新安装流程不得删除第三方语言文件。
2. 路径按 ordinal 相对路径排序后逐个加载；外部层位于内置层之后。
3. 将无效 JSON 限制为单个文件诊断，不损害已加载语言和正常启动。
4. 提供宿主注册额外 source 的组合根扩展点，而不将文件系统或 DynamicLocalization 类型泄漏进 Core/Runtime。

**测试与验证：**

- 外部 JSON 覆盖一个 `ui.*` key 和一个 group/character key。
- 嵌套外部文件的相对路径确定最后覆盖值。
- 更新/install 保留 external 文件；损坏外部文件不会阻止其余翻译加载。

**风险：** 当前安装器的更新策略需要被实际验证；不要在没有证据时假定目录保留。

**退出条件：** 翻译者手动放入 JSON 即可覆盖界面和剧情文本，且行为跨启动可复现。

## Phase 5 — 首次语言选择与持久化

**状态：** partially implemented（2026-10-10）

用户调整顺序，先完成设置页：共享页使用通用 `IPlayerSettingsService` 与选择/范围项模板，Sample 注册语言和文字速度。语言绑定参照官方 Avalonia JSON demo 使用 `LocalizedString.Value`；直接更改 culture 会刷新当前绑定。两项偏好写入 session 的 player variables 并在下次 session 初始化时恢复；首次启动选择、启动计数与游戏文本 resolver 同步仍待后续 Phase。

验证：Sample 构建成功；`SampleLocalizationTests` 3/3 通过，包括已建立的 Avalonia TextBlock binding 在直接 SetCulture 后更新，以及偏好保存/恢复且保留其他玩家变量。

**目标：** 在 Sample 以本机、按游戏、非存档的玩家变量保存语言与启动次数，并实现可重置的首次语言选择页。

**前置条件：** Phase 1 culture service 和 Phase 3 game resolver 均可在 Engine 创建前配置。

**涉及模块：** `GalNet.Sample.Avalonia` session/startup、`FilePlayerVariableStore` 使用点、Sample settings UI、自动化测试。

**任务：**

1. 定义受保留命名空间保护的 player variables：所选 locale 与启动计数。
2. 启动时先读 player variables；计数为零时用 bootstrap UI 显示选择页。
3. 确认选择时原子地保存 locale 后增加计数；取消/异常不增加计数。
4. 后续启动在 Engine 创建前应用已保存 locale；设置页可重新选择语言；既有“清空游戏数据”重置这两个变量。

**测试与验证：**

- 首次、确认、取消、重启和 reset 流程。
- 选择 language 后 UI 与游戏文本同步更新；保存不进入 save slot。
- profile 内不同游戏目录互不污染。

**风险：** language picker 在游戏内容未准备好时仍需有可用 bootstrap UI；不要让失败的内容加载永久写入 onboarding 完成状态。

**退出条件：** Sample 首次启动可选语言、后续可持久化并从设置切换，重置后可重复验收。

## 跨 Phase 文档与完成条件

- 每个 Phase 落地后更新 `docs/spec/i18n.md` 与受影响的内容格式文档，只记录已验证事实。
- 任何发现的非显然问题先写入 `.agents/lessons/`；结束时按 closeout 流程整理。
- 完成前应顺序运行受影响项目的测试，避免共享 `obj` 并行构建争用；验证具体命令由实施时的项目依赖与环境确定。
