---
feature: F-20260916-02-audio-system
status: confirmed
created: 2026-09-30
updated: 2026-09-30
---

# GalNet 音频系统：实施前设计复核

## 结论

原始设计的核心分层是正确的：作者面对稳定的逻辑轨道和资源 ID；Runtime 拥有队列与逻辑状态；宿主拥有解码、设备和精确时钟。该分层与 Ren'Py 的 channel/mixer 区分、Godot 的 bus 与有限复音、Unreal 的 source/submix/DSP 图相符。

但它还不能直接进入实现。复核发现四个必须写成契约的缺口：多声部的队列与 `next`、有界排队、后端能力/自然衔接、以及当前原语与 Editor 的真实接入点。它们不改变原始架构方向；本设计把它们收束为 Phase 4A 的前置约束，并把精确衔接、恢复位置和 Effect 留在后续阶段。

## 当前事实

- `BuiltinAudioModule` 目前只注册 `audio.play`、`stop`、`pause`、`resume`、`enqueue` 的静态 schema；它们创建的是空的即时原语，不会播放声音。
- 当前不存在设计稿所称的 `IAudioView`。`IGameView` 通过可组合的 primitive module 工作；新的音频端口须作为该组合中的显式依赖接入，而非假定已有 View 方法可替换。
- 当前 `dialogue.text` 的 `voice` 参数仍直接调用 `IDialoguePresenter.SetVoice`，而 Avalonia 实现只显示状态文字。因此它必须在同一迁移中拆为 `dialogue.textVoice`，并改走音频 Runtime，不能留下第二条播放路径。
- `AudioChannelManager`、`IAudioService` 与 `LibVlcAudioController` 是历史占位模型；`LibVlcAudioController` 按 channel 只保存一个 `MediaPlayer`，也不会传递结束事件。它们不能作为新 Runtime 的数据模型。
- 项目已引用 `LibVLCSharp`，而 Avalonia Sample/Editor 只在 Windows 引入 `VideoLAN.LibVLC.Windows`。已确认：音频后端是平台实现；首期由 Avalonia 提供真实播放 host，Headless 使用可观测的 fake host，不假装可以播放声音。
- Entry schema 的 `Select` 选项在 recommended target profile 中是静态的；Editor 现只为 Effect catalog 特殊注入动态选项。因此“`trackId` 使用项目轨道下拉”需要一个通用的项目上下文选项提供器，不能沿用硬编码的 `ParameterOptions`。

## 参考引擎带来的约束

- Ren'Py 将单声道的正常 channel（播放或排队）和可同时播放、但不支持排队的 `audio` channel 明确区分；这支持把“播放队列”和“多声部”视为两个需分别定义的维度，而非默认互相兼容。[Ren'Py Audio](https://www.renpy.org/doc/html/audio.html)
- Godot 的播放器同时暴露 bus、`max_polyphony` 与自然结束信号；达到复音上限时会截断最老声音。GalNet 应明确 overflow 作用对象与结束事件身份，而不能只存一个 track state。[Godot AudioStreamPlayer](https://docs.godotengine.org/en/stable/classes/class_audiostreamplayer.html)
- Unreal 把 source、submix 和 DSP 分开；这验证 item → track → bus → master 的处理拓扑，但不要求第一期实现 DSP。[Unreal Audio Mixer](https://dev.epicgames.com/documentation/unreal-engine/audio-mixer-overview-in-unreal-engine)
- Unity 的无间隙拼接依赖预先调度及可供准备的音频时钟。因而自然结束前的无间隙交叉淡化不能只由 Runtime 数据模型承诺，必须受后端能力约束。[Unity PlayScheduled](https://docs.unity3d.com/ScriptReference/AudioSource.PlayScheduled.html)

## 目标与边界

Phase 4A 的交付是：Avalonia Sample 可播放单声部 BGM/Vocal、有限复音 SFX，且 Runtime 在无音频设备、解码失败和错误资源时安全完成剧情。项目设置只提供轨道配置；bus 是预定义的 `bgm`、`sfx`、`vocal` 路由，不提供 bus CRUD 或玩家音量设置 UI。逻辑命令、队列状态和后台事件在没有实际设备的测试 host 中可完全验证。`GameTestCase/Assets/心.mp3` 必须演示播放中淡入/淡出，以及最小轨道 SEffect 的启用和关闭。

4A 不承诺 sample-accurate 无间隙循环、自然结束前交叉淡化、毫秒级读档恢复、DSP/Effect、3D 空间化，或 Android/iOS/browser 的实际播放。这些能力不得通过静默退化伪装为已支持。

## 运行时模型

```text
Author entry / dialogue.textVoice / nvl.appendVoice
        │
        ▼
Audio primitive module ──► AudioRuntime（单序列化器）
        │                         │  queue / active voices / generation
        │                         ▼
        │                  IAudioPlaybackHost
        │                         │  resolve and own playable source
        ▼                         ▼
non-blocking completion     backend players ──► backend events
                                      │              │
                                      └──────────────┘
                                           dispatch to AudioRuntime
```

`AudioRuntime` 是 `GalNet.Runtime` 的游戏 scope 服务。它将作者命令转换为**声部级** start/pause/resume/stop/volume 请求；宿主不得自行排队或决定下一个播放项。每一个 start 生成 `(trackId, voiceId, generation)`，事件必须带回这一完整身份。Runtime 只接收仍存在且 generation 匹配的事件，销毁 scope 时先取消 Runtime，再释放 host。

Core 保存可序列化的 `AudioTrackDefinition`、`AudioItemDefinition`、枚举与快照 DTO；`Presentation.Abstractions` 保存 host contract；具体资源实体化、LibVLC 对象和设备线程都停留在 Presentation/宿主。资源解析必须通过 `IAssetManager` acquire 后由宿主会话实体化临时源并持有其释放责任，不能把路径或 `MediaPlayer` 放入 Core/Runtime。

建议端口分成命令和事件，而非一个含糊的 `ApplyAudioCommandAsync`：

```csharp
public interface IAudioPlaybackHost : IDisposable
{
    AudioBackendCapabilities Capabilities { get; }
    Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken);
    event Action<AudioBackendEvent>? EventReceived;
}
```

`AudioBackendRequest` 只有声部级动作与稳定身份；`AudioBackendEvent` 至少为 Started、Ended、Failed。`AudioBackendCapabilities` 显式声明多声部、暂停、可查询 duration、scheduled start、seek 和 item/track `SEffect` 的支持度。不能满足的需求由 Runtime 在命令前给出诊断，并执行文档化的降级或拒绝。

## 队列、复音与溢出

原设计对 `maxVoices > 1` 的 `enqueue` 和 `next` 有歧义。采用以下规则：

1. `enqueue` 在有空闲声部时立即启动；没有空闲声部且允许排队时才加入待播队列。Runtime 每次声部释放后 pump 一个或多个待播项。因此 SFX 在未满时会立即重叠，满时才等待。
2. 每个轨道增加明确的 `maxQueuedItems`，并为等待中的队列溢出单独声明 `dropNewest` 或 `dropOldest`。`maxVoices` 只限制活动声部，绝不隐式限制或无限制地积累队列。
3. `maxQueuedItems: 0` 表示不允许等待队列；此时声部已满的 `enqueue` 直接按 `overflowPolicy` 处置，初版为 `dropNewest` 与 `stopOldest`。允许排队的轨道在队列已满时使用 `queueOverflowPolicy`。track 配置和诊断必须分别显示 active-voice 与 queued-item 的策略。
4. `next` 仅对 `maxVoices == 1` 有作者语义：停止唯一活动项并 pump 一个后继。多声部轨道使用 `stop`、`clear` 或新的播放命令；在 4A 中调用 `next` 必须产生诊断且不修改状态，避免任意挑选一个 SFX 声部。
5. `replace` 停止全部 active voices、保留队列；`replaceAll` 还清空队列；`clear` 只清空队列；`stop(clearQueue: true)` 是两者的组合。停止可使用每个 voice 的 fade-out，但状态变更立即生效，迟到事件由 generation 忽略。

初始轨道为：`bgm`（1 voice、可排队）、`vocal`（1 voice、可排队）和 `sfx`（8 voices、默认 queue capacity 0）。这保留常见视觉小说的直觉：BGM/语音可排歌单，普通 SFX 不会在繁忙时延迟播放成过期声音；作者若确需排队，可新建专用单声道 track。

## 淡化、循环与后端降级

命令触发的单声道替换可以在后端能同时持有旧/新声部时做 equal-power fade；否则 4A 必须记录 `CrossfadeUnavailable` 并按“停止旧项、开始新项”降级。自然结束前启动淡出或无间隙循环额外要求 duration、预加载和 scheduled start，故不属于 4A 验收；仅在能力为真时才启用，并以契约测试证明。

`loopCount: infinite` 在后端支持原生循环时使用该能力。不能原生循环的 host 可从头重启并记录 `SeamlessLoopUnavailable`；它不能宣称无间隙。`fadeIn`/`fadeOut` 的曲线、范围和后继拥有切换权规则保持原设计，但仅对实际能力可达的场景生效。

## 作者与 Editor 接入

所有音频原语迁移为 `trackId`；旧 `channel/mode/times` schema 在同一变更中移除，避免编译 profile 同时接受相互冲突的模型。音频模块创建非阻塞 primitive instance 并提交给 Runtime；它不直接触碰 host。

`dialogue.text` 删除 `voice` 参数，`nvl.append` 保持纯文本。新增非原语 composite entries `dialogue.textVoice` 与 `nvl.appendVoice`：它们分别编译为 `audio.play` 加 `dialogue.text` 或 `nvl.append`。除文本自身的 `speaker`/`content` 与必填音频资源外，音频附加参数只有 `gain`（局部音量）和 `trackId`（默认 `vocal`）。播放使用 `replaceIfDifferent`：同一轨道当前项引用同一资源时保留播放位置与暂停状态，仅更新局部 gain；资源不同才替换当前 vocal。这样每句文本仍按原有阻塞语义推进，音频仍是非阻塞的。

点击推进、skip、auto、场景切换默认不停止已发出的 vocal；下一段 voice、显式 `audio.stop` 或 scope disposal 才停止它。没有音频 host 时记录一次诊断、正常展示文本。

`trackId` 是明确作者选择时的最高优先级。未来若项目获得稳定的角色 ID，可增加可选 speaker audio profile：`speakerId → defaultVocalTrackId`，只在 `textVoice`/`appendVoice` 未显式指定轨道时生效。它不自动为每个角色创建轨道；作者可按角色、角色组或统一 `vocal` 组织轨道，避免把角色名、玩家音量偏好和轨道路由永久绑定。

Editor 将新增一个可注入的 project-context option provider。它在编辑时读取项目 audio track definitions，给 `trackId` 提供选择项；核心编译器仍只验证字符串、唯一轨道表和资源类型，因而不依赖 Editor。删除或重命名轨道须列出 entry、Effect 和玩家偏好引用；重命名操作以原子重构更新引用。

## 设置、存档与迁移

本期有效增益为 `trackDefaultGain × itemGain`；bus/master/player-track 音量和 UI 不在范围内，静音也不通过 stop 实现。bus 字段已经是轨道定义的一部分，但只允许三个内置 bus，bus 配置与玩家音量设置留给后续 feature。

本 feature 不考虑向后兼容：旧 `channel/mode/times` authoring 或已编译内容直接被当前 profile/format 拒绝，既有 `GameSettings` 音量字段不迁移。存档恢复、播放器设置和 Effect 不属于首期交付；后续恢复若实现，默认 BGM 可从头恢复，SFX 与 vocal 不重播。

## SEffect 后处理命名

音频后处理使用 `SEffect` 前缀，避免与图像 `EffectDefinition`/`EffectCatalog` 混淆：`SEffectDefinition`、`SEffectCatalog`、`SEffectScope` 与 `SEffectParameterDefinition`。作者侧稳定 ID 采用 `seffect.*`，如 `seffect.lowPass`、`seffect.pan` 与 `seffect.gain`。它们描述 item → track → bus 的音频处理链。本期提供最小轨道 SEffect 的 apply/stop、生命周期和 capability 降级，并在 `GameTestCase` 演示；参数编辑器、动画与多种 DSP 留给后续 feature。

## 备选方案与取舍

1. **继续扩展 `LibVlcAudioController`**：拒绝。它按 channel 保存单 player，会让 queue、voice identity 和 Runtime 状态落在错误层。
2. **让后端接收 track 级 queue**：拒绝。会让 Host 决定作者语义，无法以 fake host 确定性测试、序列化或安全处理迟到事件。
3. **把多声部 SFX 伪装成 `sfx1…sfxN`**：拒绝。它把设备实现细节泄漏给作者，无法表达统一的 bus/voice policy。
4. **第一期保证所有交叉淡化和无缝循环**：拒绝。准确调度是后端能力，不可用 Runtime 计时器替代。

## 风险、验收影响与实施门槛

必须先以 fake host 覆盖 queue pump、active/queued overflow、generation、暂停、替换、错误资源和 scope dispose。Windows LibVLC host 还须独立验证资源实体化、事件线程回投、同 track 多 player 的释放，以及设备缺失时不阻塞剧情。

已确认的 Phase Plan 按“轨道模型与文本 composite → Runtime + fake host → Avalonia host/Sample”拆分；每期都以可执行契约测试为退出条件。bus CRUD、玩家音量/存档、SEffect 参数编辑器/动画和角色默认轨道随后另立 feature。
