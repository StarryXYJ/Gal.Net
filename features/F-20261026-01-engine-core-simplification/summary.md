# 引擎核心精简与职责收敛

状态：上一轮 P1–P5 verified，P6 保留环境待验证项；后续整体重构 R1–R2 verified，R3–R8 planned。下列“实现结果”记录上一轮；后续归属变化见本文 R1 段。

## 实现结果

- 表达式双引号文本按 JSON 转义绑定为值，保留 NCalc 单引号语法；非法条件按 false 处理并限量诊断。变量赋值表达式失败/无值时保留原值，不把失败写成空字符串。
- 状态、播放模式和检查点由 EngineNotifications 按发生顺序在执行门外发布，隔离订阅者异常。音频诊断观察者不能阻断后端清理。
- 所有推进路径有批次预算和让出机制，长剧情/合法循环不因预算耗尽报错；调用方取消不使 Engine Faulted。跨批次重查 scope 与阻塞边界。
- Engine 读档使用 RestoreFromAsync，先准备 GameRestorePoint 并验证图位置、变量和模块状态，再替换旧 scope；复用已解码的候选状态。宿主替换前预检候选存档，无效存档保留当前运行。旧 scope 的取消回调异常不能留下半份恢复。
- 变量读取与集合快照隔离；运行图保存验证后的私有副本。模块共享场景记录保留，通过 GameState.Read/Update 协调，锁对象不跨程序集公开。
- Entry 只有一份 JSON 参数；AuthoringPrimitiveEntry/CompositeEntry 与 sealed PrimitiveEntry 类型分离。移除 IsGeneric、字符串 Values、CreatePrimitive<T>、Layer 历史参数特判和必然失败的序列化回退。Choices 最终只产生类型化 enum 约束，表单和生成器同步迁移。
- primitive/呈现使用 IGameContext，Engine 使用含游标与恢复能力的 IGameRuntime；分发依赖改为 Execution.IPrimitiveDispatcher。编辑器设置/最近项目/布局编码迁到 Editor.Abstraction.Settings，无使用者的 LauncherSettings 删除。
- 删除活动列表的多余排序、sequence 字段和自动阻塞镜像集合；未引入缓存、增量快照、对象池或并行调度。

## 关键边界

错误处理按业务后果确定。可选展示有已定义降级时继续；无效条件有诊断；无效存档拒绝本次操作。关键执行或状态失败仍正常传播，不统一吞异常。已部分提交的状态不靠 catch 伪装成成功。

GameState 的活记录是执行模块内部共享对象，不宣称其读取结果是不可变快照。Engine 的图副本对宿主/作者代码隔离，继续使用已有 Node/Group 数据形状，没有另建公开 CompiledGraph 类型层。

Engine 仍拥有唯一执行门；调试控制保留为内部 DebugSession，没有新增独立 Runner。通知队列与已有导航/活动实例协作者承担独立职责。同步的低层 GameRuntime.RestoreFrom 是纯内存准备/提交入口，Engine 的读档入口已异步化；现有同步释放入口仍供同步宿主使用，不为兼容新增包装。

## 验证

最终回归共 1648 通过、4 项因 Windows 创建符号链接缺少特权而失败、2 项跳过。详细结果如下；本地输出在忽略产物 `artifacts/engine-core-audit/final-*.log`。

| 测试项目 | 通过 | 失败/跳过 |
| --- | ---: | --- |
| Core | 150 | 0 |
| Runtime | 134 | 0 |
| Primitives.Builtins | 177 | 0 |
| GameHost | 6 | 0 |
| Architecture | 49 | 0 |
| SchemaGenerator | 18 | 0 |
| Storage.FileSystem | 22 | 0 |
| Presentation | 243 | 0 |
| Editor.Shared | 419 | 3 失败、2 跳过 |
| Editor | 308 | 0 |
| Integration | 122 | 1 失败 |

4 个失败均停在测试准备阶段的 Directory.CreateSymbolicLink：GroupCompileServiceTests.LinkedOutput_IsRejectedAndExternalDirectoryUnchanged、ProjectAuthoringSafetyTests.Preview_RejectsLinkedOutputBeforeWriting、ProjectSchemaContextTests.AncestorLinks_AreRejectedForRawReadsAndContractLoads、ScreenshotEntryTests.GeneratedNames_AreUnique_AndRelativeOrLinkedDirectoriesAreRejected。没有通过跳过或修改这些用例来掩盖失败，仍待支持符号链接权限的环境复核。Editor.Shared 初轮另有一次文件移动拒绝访问，最终完整重跑已通过，根因未确认。

原生桌面/真实外进程的显式测试没有执行；相关能力不能由 headless 测试替代。Editor 构建已通过；最终解决方案构建仅剩 Browser 的 NU1403：Microsoft.NET.Sdk.WebAssembly.Pack 10.0.12 包内容哈希不匹配。只使用本地包缓存的 locked restore 仍复现，仓库锁文件保持未改动；没有禁用哈希校验或改写包缓存来绕过问题。空白格式和 git diff --check 已通过。

P6 保持 in-progress，待具备目录符号链接特权、正确 WebAssembly SDK 包和原生桌面条件后补验，不把以上失败算成完整验收。

验证命令使用单进程构建、禁用共享编译器，并为 Avalonia 设置遥测 opt-out；现有平台环境限制与代码问题分别记录。未开展专项 benchmark/profile，不报告未经测量的性能收益。

## 计划调整

- P5 补充宿主替换前的候选存档预检：仅修正 Engine 读档不能防止上层提前释放旧运行。
- P6 自审补充旧 scope 取消回调异常的隔离与回归，保证合法候选不会因旧回调异常停在恢复中间。
- 不增加额外可变场景镜像/深拷贝体系；保留模块活记录，隐藏同步实现并明确使用边界。
- 调试路线测试原先引用未声明的第 7 个出口，夹具补齐实际分支项，保留“忽略条件/选择进行显式路线回放”的原测试目标。

## 当前规范

本次公开 API 有破坏性变化，仓库内调用方已同步迁移；外部项目的 Contracts DLL 需要使用新 Core/Contracts 重新构建，不保留旧二进制适配。

- [架构](../../docs/spec/architecture.md)
- [运行时](../../docs/spec/runtime.md)
- [Entry 与参数](../../docs/spec/entry-types.md)
- [术语](../../docs/glossary.md)
- [接入表现层](../../docs/manual/new-presentation-layer.md)

## R1：执行协议与管理类型归位

执行契约移入 Core.Execution：IPrimitiveDispatcher、IChoicePresenter、ICatchUpPresentation、IPlayerPlaybackController；IAudioPlaybackHost 与现有音频数据协议同归 Core.Audio。IGameRuntime 与 GameRestorePoint 移入 Runtime.Runtime。Runtime 的项目依赖现在只有 Core，注册与展示恢复组合继续留在 Presentation.Abstractions，没有新增包装类型或变更播放/恢复语义。音频领域整体可选化仍待 R5。

仓库内宿主、呈现、内置模块、Sample 和测试调用方已迁移。Debugging.Tests 夹具使用 GameRegistration，以前借 Runtime 获得隐式呈现引用；现在由测试项目直接声明 Presentation.Abstractions，生产 Debugging/Runtime 不因此依赖展示实现。

相关测试共 893 通过、1 个既有符号链接夹具失败：

| 项目 | 通过 | 限制 |
| --- | ---: | --- |
| Core | 150 | 无 |
| Runtime | 136 | 含新增自定义分发器执行/恢复与编译产物依赖测试 |
| Architecture | 49 | Runtime allowlist 仅 Core |
| GameHost | 6 | 无 |
| Presentation | 243 | 无 |
| Primitives.Builtins | 177 | 无 |
| Debugging | 10 | 无 |
| Integration | 122 | 1 项 ScreenshotEntryTests 在 Directory.CreateSymbolicLink 缺少 Windows 特权 |

普通桌面/测试解决方案构建通过，包含 Editor、两个 Sample 和所有桌面测试项目；该验证使用忽略产物 r1-desktop.slnx 排除 Android/iOS/Browser，不等同于全部平台解决方案构建。Editor 单独构建也通过。普通桌面图从本地缓存执行不含 force-evaluate 的 locked restore 通过；定向 C# 未使用引用清理和空白格式通过，git diff --check 通过。日志在忽略产物 artifacts/engine-core-audit/r1-*.log。

34 个普通/ReadyToRun 锁文件已重算并审查，仅删除 Runtime→Presentation 的项目依赖及无其他消费者的项目节点，第三方版本/哈希和框架键保持原值。发布验证仍有限制：R2R 本地缺少 .NET Runtime/WindowsDesktop/AspNetCore/Crossgen2 等 win-x64 包，未完成发布还原/构建；Browser 恢复仓库原哈希后，普通 locked restore 仍复现 WebAssembly.Pack 10.0.12 的 NU1403。不能把锁图生成成功当作发布验证成功。iOS 本地还原曾派生出不同平台版本键，已撤回该无关生成差异；原生平台未构建。

强制重算 Browser 锁图会接受本地包的不同 contentHash，即使同时使用 locked-mode 也不是原锁哈希验证。本轮恢复原哈希后以不含 force-evaluate 的 locked restore 复现失败，没有改写缓存或关闭验证来绕过问题；可复用经验见 .agents/lessons/L-20261026-03-forced-restore-package-hash.md。新增测试初版在 Engine 构造后才初始化变量，却读取构造期稳定快照，夹具改为构造前初始化；生产检查点语义未改动。

R1 的普通执行边界退出条件已满足；完整平台限制继续保留在 P6/R8，不视为解决。后续 R2 从设置模型与模块配置解耦开始。外部消费者需要随公开类型迁移重新构建，不提供旧命名空间适配。

## R2：设置模型与模块配置解耦

IGameContext/GameRuntime 不再携带 Settings。Engine 构造和 UpdatePlaybackOptionsAsync 只接收不可变 PlaybackOptions，配置无效时拒绝本次更新，不替换旧值或使运行 Faulted。BuiltinAudioModule 在组合时接收独立的轨道定义副本，以 AudioMixOptions/AudioMixLevel 接收主音量和按分组 ID 索引的混音值；入口复制集合，不在每次播放或恢复时读取完整项目/玩家模型，更新作用于当前及后续 voice。

默认 GameSettings 上移 Avalonia.GameHost.Settings；它向模块投影配置，项目不必继承此模型。通用 PlayerSession 不再拥有该模型，清空玩家数据后由默认宿主重新绑定偏好。Headless 文本只消费速度值，项目轨道显式传入模块；FilePlayerSession 接收简单 maxSlots 参数，两个默认宿主都将项目 SaveSlotCount 传入保存服务，修正原先忽略该值的问题。

SettingsContainer/SettingsSection、无消费者的 SaveSettings、Editor 的旧游戏设置接口/空实现/DI 入口，以及 SfxChannelCount 字段与 UI/CLI/复制路径已删除。编辑器历史夹具用实际仍生效的分辨率字段验证无关设置保留。分组声明与单轨混音仍待 R3；GameContent/Editor/Storage 共用的作者 ProjectSettings 归属和自定义配置链路仍待 R4，本阶段没有用额外跨层依赖把作者模型简单搬家。

验证共 1645 项通过、4 项符号链接权限失败、2 项跳过：Core 150、Runtime 138、Builtins 178、GameHost 6、Presentation 243、Architecture 49、Debugging 10、Storage 22、Editor.Shared 419（3 失败、2 跳过）、Editor 308、Integration 122（1 失败）。4 个失败仍为先前记录的 Directory.CreateSymbolicLink 特权不足，没有新增未修复的代码失败。默认偏好模型最终上移后重跑了 GameHost/Integration，存档槽映射补齐后重跑了 Storage/Integration。

当前完整 GalNet.slnx 构建、普通 locked restore、定向 C# 格式清理与 git diff --check 通过。工作期间的独立清理提交 2a5c678 删除 Launcher 并包含部分早期 R2 修改，保留该提交；旧临时解决方案引用删除项目导致的构建失败已改用当前 GalNet.slnx 后验证通过。当前解决方案不再包含旧 Browser 目标，这不证明其历史哈希问题被修复；原生交互、符号链接权限与 R2R 发布环境限制仍按后续阶段处理。日志在忽略产物 artifacts/engine-core-audit/r2-*.log。

新增回归覆盖推进选项显式更新/无效输入、混音集合隔离和当前/后续 voice、轨道定义不借用作者对象、清空后默认偏好重绑和自定义存档容量。正式规范与接入示例已同步，未引入兼容适配层。R2 完成后按用户约定提交，再进入 R3。

## R3：自定义音频分组与轨道

AudioConfiguration 在组合/加载入口验证并复制分组与轨道，声明元素改为不可变 record。组与轨道 ID 均由开发者定义，轨道可以不分组，空配置合法；移除 AudioBuses 和 Core 默认轨道工厂。Runtime 使用索引查找声明，播放期间不重复验证整个配置。master/group/track 的 gain/mute 同时作用于已有和后续 voice，最终叠加轨道预设与内容增益并限制到后端范围；中间乘积使用 double，合法的大增益不会因 float 溢出意外变成静音。

快进保留由 PreserveDuringCatchUp 明确指定，不按 bgm 名称推断。普通音频与语音 Composite 均要求显式 trackId；没有保留旧默认目标。可选 BuiltinAudioDefaults 位于 Contracts.Builtins，Sample/新项目模板显式选择音乐、音效、语音三组，完整路径仍复用同一配置/混音机制。

默认 Avalonia 偏好改为 Groups/Tracks 字典；声明生成分组 gain/mute，ShowTrackAudioControls 可选择显示单轨控件，隐藏时仍恢复单轨偏好。存储键使用区分层级、URI 转义的 ID；清空和重绑会重建动态恢复项。Editor 支持增删分组、任意归属和快进策略，删组将成员轨道解除归属；草稿、Undo/Redo、保存、重开、导出/安装和预览保留这些值。Editor 提交/加载/保存与安装加载复用 AudioConfiguration 的校验。

新增回归覆盖配置集合隔离、空/未分组/任意分组、同组两轨的独立混音、各层静音、当前/后续 voice、与名称无关的快进策略、大增益饱和、动态偏好恢复/隐藏控件/重新配置、Editor 历史与重开及包安装。初轮发现 DTO 空串属性初始化值仍导出 schema 默认，按现有 lesson 改为构造函数初始化，并扩展所有音频/语音 schema 的缺省/null 回归。

最终验证 1752 项通过、4 项既有符号链接权限失败、3 项跳过；Core 152、Runtime 141、Builtins 185、Editor 323、Editor.Shared 422（3 失败/2 跳过）、Integration 123（1 失败）、Presentation 243、Architecture 49、Assets 57（1 跳过）、GameHost 6、Debugging 10、Diagnostics 1、Storage 22、Generator 18。完整解决方案构建通过；最后仅迁移遗漏的测试轨道目标及复合测试断言，分别重建并重跑 Builtins/Integration。C# 空白格式和 git diff 检查通过，无依赖/锁文件变更。原生媒体、显式外进程及符号链接权限限制继续保留。日志位于忽略产物 artifacts/engine-core-audit/r3-*.log。正式行为见 [runtime](../../docs/spec/runtime.md)、[entry-types](../../docs/spec/entry-types.md)，接入见 [new-presentation-layer](../../docs/manual/new-presentation-layer.md)。完整自定义项目 section 和领域归属仍归 R4/R5，本阶段不将其计为完成。

## R4a：变量单一数据源与仓库输入

ProjectSettings 不再包含 PlayerVariables/SaveVariables，删除命令编辑、快照比较、克隆和保存时的派生副本与同步。变量定义只由 Graph 保存并供运行初始化使用；不保留旧设置变量适配。EditorDocumentRepository.Load 仅接收项目路径，删除一直未使用的项目名与完整设置参数，迁移 GUI、CLI/Shared、外部重载和测试包装器。未使用的 VariableNameRules.Normalize 和重复定义清理分支已删除，保留实际使用的名称验证/清理。

保存断言改为确认 settings 无变量列表、Graph 保留原声明；面板重开继续从 Graph 验证描述、UID 和默认值。完整配置 section 和默认 DTO 归属仍待 R4b/R4c，R4 总阶段尚未完成。

普通输出构建遇到正在运行的用户 Editor 锁定 DLL，保留其进程并改用 artifacts/engine-core-audit/r4a-isolated。locked restore 和同源码隔离解决方案构建通过；固定路径的契约夹具由本次隔离输出补齐，没有取用不明版本 DLL。初次默认并发 restore 未输出诊断即退出并遗留 380 个 MSBuild worker，按父 ID/命令行/启动时间确认后只清理本次 worker；串行 --disable-parallel -m:1 /nr:false 后成功。根因尚未确认，经验补充到现有构建 lesson。

最终相关回归 868 项通过：Editor.Shared 422（3 项既有符号链接权限失败、2 跳过）、Editor 323、Integration 123（1 项既有符号链接权限失败）。初次 Editor 回归 322 通过、1 项 Timeline 测试在 HeadlessUnitTestSession.DisposeAsync 发生空引用，与现有 Avalonia 12.0.5 会话发布竞态 lesson 的失败位置一致；定向 1 项与完整 323 项重跑通过，未修改 Timeline 业务代码，保留原失败日志，不声称修复框架竞态。空白格式和 git diff 检查通过，依赖/锁文件没有变化。日志位于忽略产物 artifacts/engine-core-audit/r4a-*.log。

R4a 收尾时默认输出不再占用，普通 GalNet.slnx 完整构建也通过，日志 r4a-final-standard-build.log。工作区后来出现本地化面板的并行变更，保留且不纳入本阶段提交；868 项回归以本阶段同源码隔离产物为依据。

## R4b：配置协议与默认模型归位

删除 Core.Settings.ProjectSettings，默认作者模型改为可选 Contracts.Builtins.DefaultProjectOptions；Core.Configuration 仅持有版本化配置文档、section/schema 协议，复用 FieldDescriptor/ValueValidation。GameContent、GalProject、作者快照和外部重载统一使用 Configuration，没有旧 Settings 别名。settings.json 改为 version/sections 信封，各 section 带版本与对象 data，不读取旧平铺格式。

类型化读取返回独立 DTO，缺省读取不落盘；显式 Set 在验证后发布，保留 section 中未声明的字段和不认识的 section。生成 DTO 使用字段 codecs/schema 默认，普通 DTO 可显式提供字段描述。未知版本只在声明消费该 section 时检查；修改已知字段失败不发布半份配置，合法更新可修复旧的已知无效值。字段检查与 DTO 解码统一区分大小写，避免未知大小写字段在解码时覆盖已验证值。

项目契约可公开 ProjectSchemas.Configurations；缺省为空，关闭项目/契约时释放对自有 CLR 类型的引用。Editor 打开、保存、结构发布和外部重载应用所选 schema；CLI 编辑与 apply 接收 schema。Storage 不依赖默认模型，由 GameContentLoadOptions 接收 schema 与开发语言选择委托；本地化加载器只接收文化代码。两种 Sample 和默认契约显式选择默认 schema。预览 provider 补齐项目配置副本，避免预览丢失声明的音频配置。

初轮全量回归暴露 caada23 中的语言显示名称输入问题：设置显示字符串触发过滤，候选列表已清空，旧候选匹配无法取得代码。保留新的 Code 绑定，只恢复显示字符串回退的代码提取，单独提交。另去掉 DirectProjectPersistence 为复制配置进行的 JSON 编解码往返，使用文档 Clone；本地化索引单次取得文化代码，避免每条文本重新解码配置。

R4c 仍需完成通用 section 编辑/历史及自有 DTO 的完整导出、安装、运行与预览链路，R4 整体尚未完成。正式协议见 [file-formats](../../docs/spec/file-formats.md)、[runtime](../../docs/spec/runtime.md)，接入见 [new-presentation-layer](../../docs/manual/new-presentation-layer.md)。

最终全量回归 1770 项通过、4 项既有符号链接权限失败、3 项跳过：Core 166、Runtime 141、Builtins 185、Editor 323、Editor.Shared 424、Integration 125、Presentation 243、Architecture 49、Assets 57、GameHost 6、Debugging 10、Diagnostics 1、Storage 22、Generator 18。普通解决方案构建通过，无依赖或锁文件变更；格式和 diff 检查通过。格式工具的 MSBuild 工作区因沙箱命名管道权限不可用，使用 folder 模式完成格式化与只读核验。初轮语言名称失败日志保留，独立修复提交为 `2a92fb6`，最终完整 GUI 回归通过。原生媒体/显式外进程未执行；符号链接环境限制不隐藏。证据位于忽略产物 artifacts/engine-core-audit/r4b-final-build.log、r4b-final-tests.log/trx 与 r4b-format-verify.log。

### R4b 精简

按用户反馈删除配置专用 IFieldDto 分支、ForDto 工厂和无消费者的 DataType/DefaultData，统一普通 DTO + 字段 schema 的 JSON 路径；默认值和候选验证共用 Read。SDK JSON 信封反序列化、重复属性选项及标准序列化替代手写解析/递归检查/输出。Set 在合并后验证一次，持久化移除重复读取/克隆和无效 serializer 配置。保留构造默认值、schema 默认值、未知数据、大小写规则、版本与原子拒绝行为；不把外部同时进行的 Editor 精简混入本次提交。

产品代码净删 88 行，Core 配置从 239 缩到 166 行；测试代码净删 7 行，只移除已删除能力的模拟 DTO，关键行为回归保留。当前工作区并行 Editor 重构曾导致 DebugApplicationProcess.Error/Status/Changed 未定义，未修改这些外部文件；以 1228f94 基线和五个配置改动源码组成独立候选，SHA-256 与提交源码核对一致。候选 locked restore、完整解决方案构建和格式检查通过；774 项通过（Core 166、Architecture 49、Editor.Shared 424、Integration 125、配置相关 GUI 10），4 项既有符号链接权限失败、2 项跳过。原生/显式外进程未执行，NU1900 漏洞源不可达警告保留，无依赖/锁文件变更。证据：artifacts/engine-core-audit/r4b-cleanup-candidate-*.log、r4b-cleanup-results 和 candidate-source.json。


## R4c1：通用配置命令与发布链路

用 project.configuration.patch 取代固定设置字段分支，按项目 schema 验证整份 section 候选后发布；values 支持显式 null，remove 表示删除并恢复读取默认值，同键以 values 为准。拒绝未声明字段/section 和不支持的版本，保留已有未知数据。CLI 构建、两种导出、Editor 导出及外部调试构建显式传递所选 schema，无效配置不能覆盖既有产物。没有新增配置框架，产品代码净减 20 行；新增主要为测试夹具与验证。

以 b87297f 为基线组成隔离候选，当前阶段源码 SHA-256 一致。locked restore、完整解决方案构建、格式/diff 检查通过；初轮构建发现 builder 缺少持久化命名空间引用，补齐后最终构建通过。1089 项通过：Core 166、Architecture 49、Editor.Shared 430、Editor 318、Integration 126；Shared 2 项和 Integration 1 项仍因 Windows 创建符号链接权限失败，Shared 另有 2 项跳过。NU1900 漏洞源不可达警告保留，没有依赖或锁文件变更。

ProjectContractPipelineTests 覆盖实际 CLI 修改、显式 null/删除、领域校验整批拒绝、版本拒绝、未知数据、两种导出和安装保留；GameInstallationTests 验证自有 DTO 经命令保存、导出、安装后由所选 schema 重新加载。证据位于 artifacts/engine-core-audit/r4c1-build-final.log、r4c1-restore.log、r4c1-results 与 r4c1-sources.json。协议与用法已同步至 file-formats 和 new-presentation-layer；GUI 通用表单、Undo/Redo 和自有配置的宿主映射属于 R4c2，R4 整体仍在进行中，不恢复内嵌预览。


## R4c2：通用表单、历史与宿主投影

删除固定设置/音频编辑模型，用 DtoEditorHost 和 FieldDraftState 生成 section 表单；普通复合值复用 JSON 编辑。草稿应用/保存整批校验，历史只保留数据与必要项目引用，Undo 可恢复缺省 section，未知 section/字段和无关更新保留。命令与 GUI 共享 Configuration.Patch；Resolve 为表单读取 DTO 的有效默认值，不回写原始配置。默认语言声明仍由 I18n 的内容迁移操作维护。AvaloniaGameHostOptions.SelectProjectOptions 将项目 DTO 映射为存档容量、语言和音频声明，宿主启动后缓存这些简单配置，删除宿主对默认 DTO 的直接读取。产品净减 143 行。

11 项定向测试通过；初轮发现语言切换清掉错误提示及新增挂载测试缺少主题，均修正后通过。完整解决方案构建、格式/diff 检查与 1091 项相关回归通过（Core 166、Architecture 49、Shared 430、Editor 320、Integration 126）；3 项符号链接权限失败、2 项跳过保留。新增真实挂载验证覆盖字段输入、批量应用、过期绑定、Undo/Redo、保存、导出、安装及重新打开；安装用例进一步验证自有 DTO 映射存档容量并实际生成对应数量槽位。

显式原生外进程 3 项实际完成启动、变量编辑、Seek、Step、停止/重启和旧 run 拒绝，均在请求关闭后的退出等待超时。首次诊断读取活动日志遇到 FileShare 冲突，改为共享读写读取后暴露真实关闭超时；未吞掉异常或把失败计为通过。日志保留于 artifacts/engine-core-audit/r4c2-native*.log/trx，关闭链路继续纳入 R7/R8；不阻塞其余重构。其他证据为 r4c2-verified-build.log、r4c2-targeted-final.log、r4c2-results。正式用法同步 new-presentation-layer 与 file-formats。


## R5a：场景、音频领域归位

Scene/Audio 数据、端口和纯算法从 Core 迁到 Contracts.Builtins；音频执行/后台调度/票据迁入 Primitives.Builtins.Audio，混音输入归可选契约。删除 IGameContext/GameRuntime 的场景管理器要求及无消费者的接口，内置模块通过 runtime.Scene() 使用按 GameState 根隔离的场景服务。GameState 的场景专用字典改为状态契约选择的 keySpace 身份索引，写入/恢复时继续保证跨表唯一性与 O(1) 查询；不保存额外状态镜像。相关领域测试随职责迁移，调试引擎夹具改用自有普通状态，补充通用身份空间写入/恢复测试。产品 C# 净减 31 行。

完整解决方案构建、locked restore、格式/diff 检查通过。14 个测试项目共 1776 项通过，3 项既有符号链接权限失败、3 项跳过；Runtime 115、Builtin 232、Core 147、Presentation 243、Editor 320、Shared 430、Integration 126，其余架构/资源/生成器/宿主等全部通过。验证覆盖场景跨类型冲突、临时控制不入档、旧状态根/旧 scope 隔离、音频队列/drain/恢复及无 Builtins 的自有执行器。23 个锁文件只有项目引用变化，第三方依赖/目标框架/哈希不变。证据为 artifacts/engine-core-audit/r5a-results、r5a-verified-build.log、r5a-locked-restore.log。

R5 整体仍在进行中：Gallery、资源默认策略和 Runtime.Content 的宿主归属留在 R5b；已观察到的原生关闭超时仍归 R7/R8，未计为本阶段原生验收。
