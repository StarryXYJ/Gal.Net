---
id: F-20260916-02-audio-system
title: GalNet 音频系统
type: feature
status: discovery
created: 2026-09-16
updated: 2026-09-16
---

# GalNet 音频系统

## 原始目标

以 [音频系统设计](../../docs/design/audio-system-design.md) 为基础，审查其合理性、识别实现难点和潜在问题，并澄清需要产品或技术决策的范围；后续将其实现为 GalNet 的音频 feature。

## 当前事实

- 音频设计文档是设计稿，尚未成为实现契约。
- 当前 `audio.play` / `audio.stop` / `audio.pause` / `audio.resume` / `audio.enqueue` 仅支持固定的 `channel`、字符串 `mode` 和 `times`；没有 `trackId`、播放项、队列状态或后端事件模型。
- `IAudioView` 和 `LibVlcAudioController` 是占位式接口与适配器：后者以每 channel 一个 `MediaPlayer` 播放，`Enqueue` 直接调用 `Play`，不具备真实队列、并发 SFX、结束事件或 DSP 语义。
- Avalonia Sample 的 `SampleMediaViews` 目前只显示“Audio unavailable”，还没有可验收的实际音频后端。
- `GameSnapshot` 尚未保存音频状态；玩家设置目前固定为 `BgmVolume`、`SfxVolume`、`VoiceVolume`。

## 初步合理性结论

设计在以下关键边界上是合理的，应保留作为后续设计的基础：

- 分开逻辑轨道、混音 bus 和实际 voice，能同时覆盖 BGM、语音与并发短音效，且不把作者模型绑定到设备后端。
- 以不可随意编辑的 `trackId` 寻址、以资源 ID 而非文件路径传递，符合项目迁移、重构和 Runtime/Core 解耦要求。
- 将队列/播放意图交给 Runtime、将解码/设备时钟交给宿主，并用 generation 抵御迟到回调，是可测试且可恢复的职责划分。
- 明确区分作者混音、玩家偏好和单次播放增益，避免设置页覆盖作品本身的相对混音。
- 把 item Effect 与 track Effect 的生命周期和动画能力分开，避免给短命多声部 SFX 伪造不可靠的公开句柄。
- 将空间化、精确播放位置恢复、复杂 Effect 等排出基础版本，方向正确。

## 主要风险与难点

1. **Phase 4A 的范围过大。** 它同时要求项目配置/编辑器 CRUD、原语迁移、队列状态机、跨淡化、资源定位、后端事件、多声部、音量分层和 Sample 集成；应在下一阶段拆出可独立验证的实现 Phase，避免把后端与 editor 问题混在一次交付中。
2. **多声部的 `enqueue` 语义尚不完整。** 文档写明无活动声部时才启动入队项，但这会把同一 `sfx` 轨道串行化，和“短音效可重叠”冲突。需要明确：容量未满时 `enqueue` 是否立即启动新声部、何时才保留为待播队列，以及多声部 `next` 选中/停止哪些活动声部。
3. **后端能力是关键依赖。** 当前 LibVLC 控制器不能提供设计所需的并发 voice、准确结束事件、可取消回调、预加载/时长、淡化及实时 DSP。必须先决定基础版本使用的后端、支持的平台/格式和允许的能力降级；不能把 LibVLC 当前实现直接扩展成核心状态机。
4. **资源定位尚未落地。** 设计要求 Runtime 只传资源 ID，但当前适配器把 `assetId` 直接交给 `LibVLC Media`。需要定义宿主如何经项目资源提供器得到可播放流或 URI，以及资源打包、临时文件与释放责任。
5. **迁移会影响公开作者内容和设置。** 既有 channel 值、`mode/times`、`text.voice`、三项固定音量设置和已有存档都需要版本化迁移/诊断规则；迁移失败时仅记录诊断还是阻止项目打开，需要明确。
6. **并发与生命周期需先定串行化边界。** 后端结束/失败事件可能在非引擎线程、旧 generation 或宿主释放后到达；需要明确 AudioRuntime 的拥有者、调度器、取消顺序和事件是否可重入。
7. **存档语义和内容变更存在边界。** “重启式恢复”可行，但应定义读档时轨道/资源已删除、Effect 不受支持、项目配置变化和设备不可用时的统一降级结果。
8. **交叉淡化不能只由数据模型保证。** 自然结束前启动淡出需要时长与后继资源预加载；若后端无法保证无间隙衔接，应明确定义可观察的降级行为及编辑器诊断，而不是默默改变播放曲线。
9. **Effect 不应阻塞基础可播放版本。** 4C 依赖真实 PCM/mixer/DSP 管线和通用动画目标；应确认它是本 feature 的后续 Phase，而不是 4A 的交付前置条件。

## 范围（待确认）

- 以轨道为单位的基础播放控制：播放、暂停、继续、停止、清队与播放状态。
- BGM、语音的单声部及 SFX 的多声部行为；队列、placement、循环、溢出与淡入淡出语义。
- 项目轨道定义、编辑器配置和既有内容的迁移。
- 资源 ID 定位、宿主音频端口、后端事件、诊断和资源释放。
- 玩家音量设置迁移，以及在确认范围后加入的快照恢复和音频 Effect。

## 非目标（当前）

- 在 discovery 阶段修改业务代码、选择未经确认的第三方后端或创建实现 Phase Plan。
- 网络同步、实时录音、任意 VST/AU、精确逐样本跨设备恢复。
- 把三维空间化、ducking、`audio.wait`、`shuffleCycle` 或完整 DSP 效果链默认纳入基础播放交付。

## 验收标准（discovery）

- 明确首个可交付范围是仅 4A，还是包括 4B/4C 的哪些部分。
- 为多声部队列、播放/停止、跨淡化和 `text.voice` 定义无歧义的作者侧语义。
- 决定目标平台、音频格式、基础后端与不能满足能力时的降级策略。
- 明确旧内容、玩家设置和存档的迁移与失败策略。
- 确定 Runtime、宿主后端和编辑器之间的资源定位、线程与生命周期边界，之后才进入设计阶段。

## 约束与假设

- Runtime/Core 不依赖 Avalonia、LibVLC、Skia、具体文件路径或平台对象。
- 设计文档中的资源 ID、强类型命令和 generation 事件防护是当前候选方向，尚未实现。
- 首版普通 `audio.play` 保持非阻塞；等待声音结束必须是另行明确的能力。
- 当前实现与既有未提交的 workflow 文档均为工作区内容；本 feature 不覆盖它们。

## 开放问题

1. 第一个实施目标应只交付 Phase 4A，还是必须在同一 feature 内完成存档/设置（4B）和 Effect（4C）？
2. 目标平台、最低支持的音频格式、是否允许新增音频依赖，以及是否必须继续使用 LibVLC，分别是什么？
3. 对 `maxVoices > 1` 的轨道，`enqueue` 在尚有容量时应立即并发播放，还是始终只排队？`next` 和 `replace` 分别作用于哪些 active voices？
4. 旧项目内容和存档是否必须自动迁移？对于无法映射的 channel、删除的轨道或丢失资源，应该阻止加载、要求作者修复，还是记录诊断后安全跳过？
5. `text.voice` 在点击推进、自动播放、快速模式、场景切换和读档恢复时的默认停止/继续策略是什么？
6. “无间隙循环”和 natural-end 交叉淡化是基础版本硬性验收，还是允许在不支持的后端降级并给出诊断？
7. 4C 的实时 Effect 与通用动画是否属于此 feature 的必须交付，还是待基础后端验证后独立排期？

## 相关链接

- [音频系统设计](../../docs/design/audio-system-design.md)
- [Avalonia 游戏页面与内容对接计划：Phase 4](../../docs/design/avalonia-game-page-integration-plan.md)
- [当前音频条目类型](../../src/GalNet.Core/Entry/MediaEntries.cs)
- [当前呈现端口](../../src/GalNet.Presentation.Abstractions/View/IAudioView.cs)
- [当前 LibVLC 适配器](../../src/GalNet.Presentation.Defaults/Media/LibVlcAudioController.cs)
- [当前快照模型](../../src/GalNet.Core/Runtime/GameSnapshot.cs)
