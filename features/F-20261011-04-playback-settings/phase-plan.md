# 实现阶段

授权：2026-10-11 用户要求继续直到实现。业务代码可修改；既有 App.axaml.cs 用户改动保留。

## Phase 1 — 原语契约、稳定身份与已读（verified）

前置：feature/design。Core 的 Normal/Quick/CatchUp、自动阻塞状态与推进等待；Builtins 文本/NVL 的单次模式行为；编译/加载保留稳定 ID 与阅读边界；已读版本化。验证 Core、Builtins、Runtime/Storage 的相关测试。退出：手动语义不回退，快速全文与已读身份可测试。

证据（2026-10-11）：Core 70、Builtins 54、Storage 14、Runtime 59、Integration 63 项通过。compiled 独立 v3，raw 仍 v2；progress v2 拒绝旧版本不修改旧文件。用户追加不保留兼容后，删除 presenter 的单步 Skip 回退、IGameView 旧 Dispatch 分支、Variable 旧类型容错及注释 Layer 桥。Integration 需要沙箱外读取 Avalonia licensing tickets；已获工具批准并实际跑通。

## Phase 2 — Engine 策略、语音与追帧（verified）

前置：Phase 1。Engine 阻塞集合、计时、代际隔离、模式与追帧；音频播放 ticket、快速停止及音量；动画/视频追帧语义。验证自然完成、重复事件、音文等待、Choice、取消/读档及追帧真实变量路径。退出：完整策略可在无 UI 测试验证。

证据（2026-10-11）：Runtime 69、Builtins 59、Integration 63 项通过。包括自动阻塞与计时取消、旧读档实例隔离、真实变量/条件路径、Choice 停止、循环预算取消、追帧最终同步先于目标派发、同步失败后的停止清理、持久视频定义与叠加动画只提交一次；音频 ticket 的结束/失败/替换、BGM 追帧最终投影及实际增益已有无 UI 测试。取消追帧后模式切换/手动推进会等待旧展示收尾，未启动的追帧不修改活动实例。具体 Sample/Editor 协调接线、UI 抑制与真实媒体人工验收按 Phase 4 实施，不能将端口测试视为页面已接通。

## Phase 3 — 类型化分类设置（verified）

前置：配置消费契约。设置分类/项、整数浮点编辑方式、Choice、availability、应用/保存、自定义编辑器。验证 Step、回退、状态刷新、失败与生命周期。退出：宿主可扩展配置，无平台类型泄漏。

证据（2026-10-11）：设置模型及主题定向 Presentation 测试 39 项（模型 9 项、主题 30 项）、Integration 63 项通过。IPlayerSettingsService 已替换为只读 Categories，旧扁平 PlayerSetting/ChoicePlayerSetting/RangePlayerSetting 删除，Sample 语言/文本速度应用及保存恢复使用新队列。覆盖 Step 不校验倍数、动态 Choice 默认回退（隐藏/禁用时也修正运行值）、无效默认禁用、依赖刷新、应用失败、保存失败后同值重试且不重复应用、强制值与 Action。自定义编辑器 detach/替换释放租约并重建独立 Control。因删除旧契约需要保持编译，三主题最小分类 TabControl/模板接线提前完成，共用 Themes 内模板，完整原因/错误本地化与实际功能接线仍属 Phase 4。

补充验证：完整 Presentation 回归 116 项通过；原生媒体 opt-in 用例未运行，不声称验证真实解码后端。Editor 独立输出构建成功（0 error、9 warning）；默认输出构建因两个正在运行的 Editor 进程锁定 DLL 而在复制阶段失败，未结束用户进程。验证输出在 obj/verification/playback-settings-editor（忽略目录），不会更新运行中的 Editor。git diff --check 通过（仅既有/当前 LF 到 CRLF 提示）。

验证发现：绑定 null 的可选 Format 会覆盖 NumericUpDown 自身默认并在实际 attach 时抛出 NullReferenceException；模板使用 TargetNullValue=G 修正。编译成功不能代替三主题实际 headless 渲染，9 组 layout/palette 回归已通过。

## Phase 4 — 三主题与宿主接入（in-progress）

前置：Phase 2/3。工具栏互斥 Toggle、左键取消、主题确认；设置分类模板；Sample/Editor 接入、显示能力、玩家偏好/音量。验证主题与交互测试、Sample/Editor 构建及集成。退出：实际页面可使用所有已支持设置，平台不支持功能有明确可用性。

阶段内进展（2026-10-11）：三主题工具栏已加入 Quick/Auto 互斥 Toggle 与跳过按钮；GamePage 左键 tunnel 先取消旧请求，保存按钮点击前选中状态，右键不推进。GameShell 玩家根加入主题化确认层，确认期间禁用背景，取消/Escape 不执行追帧。Sample/Editor 绑定 Engine 的纯展示控制端口；离页、截图及释放撤销旧模式。确认复用 GamePageViewModel 的命令和可见状态，不再增加单独确认 ViewModel/普通页面注册；它仍由主题提供 ControlTemplate，且不参与导航历史。

Sample 游戏分类包含语言、文本速度、仅已读快速、快速/自动间隔，音频分类包含 Master/BGM/SFX/Voice 音量与静音。设置和 Engine 使用同一 GameSettings 实例，宿主保留由执行 view 拥有的 BuiltinAudioModule 引用来刷新当前播放增益；不是仅更新 UI。偏好使用 player.galnet.settings 命名空间，恢复时校验类型和范围，无效值用默认值、不强制迁移或修改原记录。动态说明、禁用原因、错误及单位采用与 PlayerText 相同的可观察本地化适配，原因同时有 tooltip 与行内文本；空 key 不占文本布局。播放请求异常通过 StatusMessage 和宿主已有 InteractionObserved 暴露详情，不静默吞掉错误。

当前验证：Presentation 122 项通过（包含三主题实际点击、根确认层背景屏蔽/Escape、分类隐藏选择与 Flush 重试）；Integration 65 项通过（新增偏好恢复、范围内非 Step 倍数、错误类型默认回退、动态原因 key/语言更新）。原生媒体 opt-in 未运行，不能将这些测试视为真实媒体人工验收。修复了新增 Common.Cancel 与既有资源重复导致 JSON provider 加载失败的问题，已重新构建资源并通过完整 Integration。git diff --check 通过，只有行尾提示。

补充：全隐藏/空分类不显示标签，当前分类失效时回退首个可见分类，未失效时保持选择；页面 detach 解除设置项事件订阅。退出 Flush 不仅等待队列，还重试 pending save，失败向调用者传播而不重复应用运行值。Editor 独立输出构建成功（0 error、10 warning），未覆盖运行中的程序。

仍未完成：Sample/Editor 的具体 Begin/EndCatchUp 展示抑制及最终场景重建；窗口/显示能力设置；Editor 预览的完整设置消费者；主题单项模板覆盖；真实媒体及完整场景人工验收。上述事项完成前保持 in-progress，跳过端口的 Runtime 测试不能替代真实宿主接线。下一步需首先处理已恢复的循环动画/视频不在 Engine 活动集合中的暂停与晚清理隔离，再接追帧协调，不能直接 Dispose presenter 后重放而删除 Runtime 持久定义。

2026-10-12：用户移除 Editor 宿主实现范围，后续验收只针对 Sample 与共享能力。Sample 增加画面分类及 IPlayerDisplayService 纯能力端口；MainWindow 绑定桌面实现，支持 Window / BorderlessFullscreen（Avalonia FullScreen 不宣称独占），预设窗口尺寸独立于设计分辨率。全屏期间尺寸禁用并提供本地化说明；无 Window 时整组隐藏。显示偏好由设置队列保存，启动先恢复窗口尺寸再恢复显示模式，不重复回写偏好。集成测试 67 项通过，包含能力筛选、全屏禁用、显示恢复和后端拒绝后的实际值保持；真实桌面切换尚未人工验证。

恢复语言候选时原 Attach 使用 fire-and-forget，新增测试后的回归复现了旧候选偶发可见的问题；改为 AttachAsync 并等待 UpdateOptionsAsync，调用者等待恢复完成。原有语言快照回归重新通过。

追帧生命周期修正：Sample Prepare 不再用 Engine.Runtime 和调用者 token 重放循环对象，而经 SynchronizePresentationAsync 使用引擎该代 scoped runtime/token，重建受同一执行门保护。Runtime 70 项通过，新增测试验证重建回调持有的旧场景隔离、读档/停止取消 token，旧回调不能写变量。调用者 token 控制进入执行门，持久播放保留 Engine run token，不能将临时 linked CTS 在重建返回后 Dispose 而切断停止通知。

上述旧剩余项已于 2026-10-12 推进：Replay 通过 onContinuation 在 Dispatch 前交付动画/视频实例；PrimitiveInstance.PresentationCompletion 包含展示清理，Engine 先模式切换再排空已有展示任务，restorer 对恢复实例同样跟踪、压缩、排空，取消等待不丢失跟踪。SampleGamePresentation 实现并实际传入 ICatchUpPresentation，Begin 接音频最终投影、文本缓存和恢复实例暂停，End 清 visual、不 Dispose 后端，再以 scoped runtime/token 重建场景、对白/NVL 和音频。文本投影通过 ICatchUpTextRecorder 接入，不扩展 snapshot。Normal 和最终重建的可见状态/NVL clear 分开处理；立即呈现显式 RevealAll，不能只靠文字速度 0 忽略文本内部 delay。

2026-10-12 等待时间按用户要求改为 int 毫秒，Runtime 字段显式命名 Milliseconds，快速默认 50（0–5000，步长 10），自动默认 2000（0–30000，步长 100）；持久偏好保存 Int。Step 不做校验，75/3250 能正常保存恢复；负值、越界与旧 float 值回退默认值，不迁移。

主题覆盖已提供 Common.SettingsCategories.EditorTemplates，按匹配顺序优先覆盖某种类型，其余回退内置编辑器，仍保留统一设置行。三布局覆盖/撤销覆盖验证通过。最新验证：Runtime 71、Builtins 60、Integration 73、Presentation 125 项通过。三布局×三配色验证追帧中无中间 NVL clear/append、最终历史/可见性正确，含 60 秒 delay 的文本最终重建在 2 秒内完成。恢复动画、plan、视频的延迟 finally 清理与取消后继续重建有 4 项测试；Sample 已构建。

原生 opt-in 已实际执行：VideoNativeSmokeTests 4 项（真实 niko.mp4 解码、seek/结束、加载时隐藏/替换），AvaloniaAudioLifetimeTests 2 项（加载时停止/销毁）通过，使用 Sample 已有 win-x64 LibVLC。这些不是完整桌面 Sample 的操作链人工验收；物理窗口全屏切换、实际音量听感及完整追帧媒体组合仍未人工验证。因此 Phase 4 暂保持 in-progress，不能将单项后端测试替代完整宿主验收。

## Phase 5 — 审核、正式文档与收尾（planned）

2026-10-12 Phase 4 工具栏补充：自动/快速仅选中显示主题色半透明背景；普通工具栏按钮透明→8% 悬浮→18% 按下，选中模式 24%。局部模板避免 Fluent 默认 PART_ContentPresenter 状态画刷覆盖。模式应用后重新发布 OneWay 选中绑定，保证按钮原生 toggle 与左键取消后的权威模式一致。九组布局×配色交互测试通过，覆盖两模式再次点击取消、互斥、真实模板背景与确认。完整 Presentation 回归 131 项通过（本轮未运行 6 项原生 opt-in），Sample 构建成功（0 error、2 warning），git diff --check 通过。

2026-10-12 样例优先修复：GameTestCase 的三个 group 均可使用 `line`，问题是 Shared 校验与 AddEntry 错将条目 ID 要求为全局唯一。修复为 group 内唯一并增加两项回归，以及样例作者文档校验。Shared 18、完整 Integration 68 项通过；CLI validate 无诊断，GameTestCase/Output 的 4 个 group 已重编译为 compiled v3。样例 README 更新格式与进度说明。本地旧 progress version 0 按本次修复显式移至 progress.legacy-20261012.json.bak，保留设置及存档，Runtime 无自动迁移/清空兼容代码。本次只修正 Shared 作者内容契约，不扩展 Editor UI；Phase 4 剩余项不变。

样例补充运行验证：实际 Headless 加载 GameTestCase/Output，经对白、动画、NVL、Choice 和 downtown 路线到达 Runtime smoke test complete，进程 exit 0；独立 artifacts/verification/sample-fix-20261012 档案生成 progress v2。Avalonia Sample 构建通过，但本轮未人工验证桌面界面及原生媒体。

前置：实现阶段通过。检查 diff/架构/相关集成与格式，记录实现偏差和未能人工验证的限制，同步 runtime/control/i18n 文档和 feature summary。退出：证据完整，未完成项不隐藏。

测试避免多 MSBuild 进程同时构建共享 obj。阶段状态仅在验证有证据后变更。
