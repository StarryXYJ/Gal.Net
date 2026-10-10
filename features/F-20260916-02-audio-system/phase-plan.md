---
feature: F-20260916-02-audio-system
status: planned
created: 2026-09-30
updated: 2026-09-30
---

# GalNet 音频系统实施计划

## 已确认范围

本计划仅交付可配置的逻辑轨道、Runtime 音频状态机、Avalonia 实际播放 host、最小轨道 SEffect，以及新的带语音文本 composite entries。bus 是固定的 `bgm`、`sfx`、`vocal`，轨道可选择其一；本期不做 bus CRUD、玩家音量/设置、存档恢复或角色 profile。旧 audio schema 和已编译内容不兼容，直接拒绝，不实现迁移器。

## Phase 1 — 轨道模型与作者入口

**状态：completed（静态 schema 的 trackId 仍为文本字段；未实现 project-context 下拉）**

目标：将项目设置、authoring schema 和编译输出切换到唯一的新音频模型，并消除对 `IDialoguePresenter.SetVoice` 的依赖。

前置条件：无。

涉及模块：`GalNet.Core`（`ProjectSettings` 与 audio DTO）、`GalNet.Primitives.Builtins`、`GalNet.Editor` / `GalNet.Editor.Shared`、编译器及其测试。

工作：

- 定义 `AudioTrackDefinition` 和固定三条默认轨道；校验唯一不可变 ID、内置 bus、voice/queue 上限、增益与溢出策略。
- 在项目设置页实现轨道 CRUD、轨道引用校验和安全重命名；不添加 bus CRUD。
- 将音频 primitive schema 改为 `trackId`、资源、局部 `gain` 与 placement，并新增 `replaceIfDifferent`；移除旧 `channel/mode/times` schema 与 `SfxChannelCount` 的音频职责。
- 将 `dialogue.text` 变为纯文本；新增 `dialogue.textVoice` 和 `nvl.appendVoice` composites。它们产生非阻塞 `audio.play` 后紧接原有文本 primitive，默认 `trackId: vocal`，并在同资源时不重启播放。
- 移除 `IDialoguePresenter.SetVoice`、Headless/Avalonia 的占位实现及所有旧入口；让 target profile 和 compiler 拒绝旧格式。
- 扩展 Editor 的 project-context option provider，使 `trackId` 使用当前项目轨道下拉，不将项目数据写入静态 primitive schema。

验证：ProjectSettings JSON round-trip、轨道唯一性/引用验证、Editor CRUD/重命名测试、composite source-map/编译测试、旧 schema 拒绝测试、`textVoice`/`appendVoice` 输出顺序和参数测试。

风险：现有静态 Select 选项不能表示项目轨道；先引入通用上下文 option provider，禁止为 audio 再建一条专用 Editor 分支。

退出条件：新项目自动拥有三条轨道，Editor 可安全维护它们；已编译内容中没有旧 channel 参数或 presenter voice 调用。

## Phase 2 — Runtime 状态机与无设备契约

**状态：completed**

目标：实现平台无关的队列、voice identity、淡化意图和错误降级，并在 fake host 中确定性验证。

前置条件：Phase 1 的新 primitive/composite schema 已通过编译测试。

涉及模块：`GalNet.Runtime`、`GalNet.Presentation.Abstractions`、`GalNet.Primitives.Builtins`、Runtime/Integration tests。

工作：

- 建立 Core audio DTO 与 `AudioRuntime`；每个 start 分配 `(trackId, voiceId, generation)`，由游戏 scope 串行化命令和后台事件。
- 定义 `IAudioPlaybackHost`、声部级 request/event、capabilities 和会话生命周期。Host 解析与持有可播放资源；Runtime 绝不接触路径、LibVLC 或平台对象。
- 实现单声部 BGM/Vocal、有限复音 SFX、queue pump、`maxQueuedItems`、active/queued overflow、暂停、恢复、停止、清队、`next`（仅单声部）、`replace`、`replaceAll`、`replaceIfDifferent`。
- 保留 item fade-in/fade-out 与后继接管的逻辑定义；后端没有并发/调度能力时，Runtime 生成可观察诊断并采用停止后开始的降级，而不声称无间隙或自然结束交叉淡化。
- 通过 non-blocking primitive instance 将 audio command 投递给 Runtime；未知轨道、非音频资源、解码失败、无 host 和过期事件都安全完成剧情。
- 提供最小 `SEffectDefinition`/catalog 和 `audio.seffect.apply`/`audio.seffect.stop` 原语；本期只要求轨道 attachment 的生命周期和 capability 降级，具体 DSP 由 host 决定。
- 提供 fake host，以可控 Started/Ended/Failed 事件、能力矩阵和资源错误支持单元/集成测试。

验证：队列与 voice 溢出矩阵、同资源 `replaceIfDifferent` 不重播且更新 gain、迟到 generation、scope dispose、暂停/恢复、缺少 host/资源、淡化能力降级、SEffect apply/stop 生命周期，以及带语音文本不会改变文本阻塞/skip 语义。

风险：不得让 Host 排队或直接从回调线程修改 Engine 状态；事件身份缺少 `voiceId` 或 generation 时必须拒绝该实现。

退出条件：在不含 LibVLC 的测试进程中，所有作者侧音频语义和诊断都可重复验证。

## Phase 3 — Avalonia 播放 host 与示例验收

**状态：completed（已完成编译/集成验证；实际听感仍需目标设备人工验收）**

目标：让 Avalonia 游戏 scope 将 Runtime 声部请求转为实际 LibVLC 播放，并验证资源与生命周期边界。

前置条件：Phase 2 fake-host 契约已通过；Avalonia 可取得项目 scope 的 `IAssetManager`/资源会话。

涉及模块：`GalNet.Avalonia.GameView`、`GalNet.Presentation.Defaults`（仅在抽象可复用时）、Avalonia Sample/Editor composition、手动 smoke test。

工作：

- 实现 Avalonia `IAudioPlaybackHost`：由资源 handle 安全实体化会话临时播放源，按 `voiceId` 管理 player，回传完整 backend event，并在 scope dispose 时停止、解除订阅和释放临时资源。
- 初始化 LibVLC 的失败路径、设备不可用路径和线程回投；仅声明已被契约测试证实的 capability。
- 将 host 装配进 Avalonia game scope；`GameTestCase/Assets/心.mp3` 提供可复现的 BGM 播放中淡入/淡出、轨道 SEffect apply/stop、重叠 SFX、同资源不重播 vocal 与错误资源诊断场景。
- 将 Headless 保持为 fake/no-op diagnostics，不为无音频环境添加隐式平台依赖。

验证：Avalonia 自动化/集成测试覆盖 composition 与释放；Windows 手动验收 BGM、SFX、Vocal、暂停/继续、页面离开和无设备降级。验证同轨多 player 不泄漏，且迟到 LibVLC 事件不影响新 voice。

风险：LibVLC 的 duration、精确调度或 simultaneous fade 并非默认可保证；未验证即把 capability 设为 false，保留文档化降级。

退出条件：Avalonia Sample 在支持设备上完成三类播放，在错误或无设备环境中继续剧情并报告诊断；资源和 player 生命周期无泄漏证据。

## 延后事项

- bus CRUD、玩家 master/bus/track 音量与存档恢复；
- SEffect 参数编辑器、动画和额外 DSP；
- 角色 profile 的 `speakerId → defaultVocalTrackId` 默认路由；
- 精确 seek、无间隙循环、自然结束交叉淡化、ducking 与 `audio.wait`。

每项均应以独立 feature 重新确认后端能力和存档/作者语义，不能借本期接口暗中实现。
