# Track / Bus 音频混音

默认宿主使用 `MiniaudioAudioPlaybackHost`，Windows x64 设备/解码适配位于 `GalNet.Media.Miniaudio`，跨平台 PCM 混音与 DSP 位于 `GalNet.Media.Audio`。LibVLC 继续服务视频和 Gallery，剧情音频不再通过独立 LibVLC player 输出。

## 对象职责

- 播放实例：资源、内容 gain、`fadeIn` / `fadeOut`、`loopCount`（0 为持续重复）和 `completion`（丢弃或重新入队）。替换时默认采用旧实例自己的淡出；显式 `audio.stop.fadeOut` 可覆盖。自然结尾淡出需要解码器提供时长。
- Track：`maxVoices`、有界队列/溢出规则、`playbackOrder`（Sequential/Random）、`repeat`（None/One/All）、轨道音量和效果、`busId`。随机选择状态随存档恢复。失败、显式停止不会按自然结束重新入队。
- Bus：真实汇总所属 Track，再执行一次共享效果链和整组音量。轨道不指定 Bus 时直接进入 Master。

顺序为实例包络/gain → Track 汇总/有序 SEffect/音量 → Bus 汇总/有序 SEffect/音量 → Master 音量和限幅。Bus 效果不会复制到每个 Track 或实例。空输入仍处理效果尾音；停止音源不会清空房间混响。读档/reset 会清空旧 DSP 缓冲，不保存精确播放游标、包络时钟或尾音。

## 效果与混音条目

`audio.seffect.apply` / `audio.seffect.stop` 使用 `scope`（`track` / `bus`）、必填 `targetId` 和 `instanceId`。首次 apply 添加到链尾；相同实例/类型更新参数并保留链位置。移除平滑旁路，不重启声音。已附着的效果可在空闲时保留，新音源第一帧即经过目标链。

Editor 的应用表单依次选择作用范围、Track/Bus、效果类型，再编辑参数。效果类型为 ComboBox；`id` 是其内部值。新建条目自动生成隐藏的 `instanceId`，复制时生成新身份并重映射批内引用，打开和保存既有内容不会补写身份。同一条目在剧情循环中再次执行仍操作原实例。

停止表单先选择 Track/Bus，再选择该剧情位置之前已经挂载的实例，列表显示效果类型、创建来源和一定/可能存在。只列出直接挂载的效果；Track 不列出所属 Bus 的效果。分支、循环或自定义语义使分析不完整时显示提示，候选不要求先运行游戏。

“全部效果”是显式 `all: true`，此时不填写 `instanceId`；不能同时指定非空实例。省略实例不会隐含停止全部。应用效果要求非空实例身份，既有无效身份保留原文供修复。

```json
{"type":"audio.seffect.apply","parameters":{"scope":"bus","targetId":"environment","instanceId":"room","id":"seffect.reverb","parameters":{"roomSize":0.65,"decaySeconds":1.8,"damping":0.4,"mix":0.25}}}
```

剧情后续调整已有实例使用 `audio.seffect.update`。在 Editor 中选择目标和实例后会展示对应参数；只填写需要改变的字段，未指定值保持不变。运行时合并后验证全部参数，失败时保留原值；实例不存在时无操作，不改变类型、不创建新实例、不改变链位置。

```json
{"type":"audio.seffect.update","parameters":{"scope":"bus","targetId":"environment","instanceId":"room","parameters":{"mix":0.4}}}
```

```json
{"type":"audio.seffect.stop","parameters":{"scope":"bus","targetId":"environment","all":true}}
```

同一精确挂载身份在不同分支来自不同类型时，仍能停止，但更新表单不会猜测参数类型。既有未知类型、参数和失效引用保留，查询刷新不自动清空或改选。音频效果参数尚未接入 Timeline 动画，也没有新增音频 Timeline 事件。

内置效果：

| ID | 参数 |
| --- | --- |
| `seffect.gain` | `gain`，0～1 |
| `seffect.lowPass` / `seffect.highPass` | 必填 `cutoffHz`，20～20000 Hz；`q` 默认 0.7071068，0.1～10；真实二阶滤波 |
| `seffect.reverb` | `roomSize` 默认 0.5；`decaySeconds` 默认 1.5（0.1～20 秒）；`damping` 默认 0.4；`mix` 默认 0.25；其余范围 0～1 |

混响是基础算法房间混响，空间大小、衰减、高频阻尼和干湿比例分别控制。更新参数/移除有平滑处理；移除效果会淡掉其输出，保留尾音的通常做法是仅停止音源。

`audio.mix.set` 使用同样的 `scope` / `targetId`，配置 `gain`（默认 1）与 `muted`（默认 false）。这是可存档的剧情混音值；玩家音量由 `AudioMixOptions` 的 Master/Buses/Tracks 独立叠加。Master 静音覆盖干声和效果尾音。未指定字段使用 DTO 默认值，并非增量 patch。

## 配置、构建与边界

默认 `galnet.project` section 版本为 2，使用 `audioBuses` / `audioTracks[].busId`。旧 section v1 被明确拒绝，需更新字段和版本；SEffect 旧 `trackId` 改为 `targetId`，旧 EQ 参数 `attenuationDb` 删除。音频轨道/效果状态表版本为 2，并新增 `builtin.audio.channels`，旧档不能混用。

Windows 开发构建需要 Visual Studio C++ x64 Build Tools；源码固定 miniaudio 0.11.23，构建自动生成并复制 `galnet_miniaudio.dll`，发布目录也包含它。玩家只需发布产物，无需编译器。当前验证 Windows x64；其他设备平台保留 `IAudioOutput` / `IAudioPcmDecoder` 适配边界。内置解码支持 WAV/MP3/Ogg Vorbis/FLAC，其他格式明确报错；当前不提供 Send、跨 Bus 路由或 Sidechain。

后端构造工厂为 `AvaloniaGamePresentationOptions.CreateAudioHost(assets, files)`。新适配器的所有权交给呈现会话，借用的 materializer 仍由宿主拥有。默认后端在后台解码至每实例两秒 PCM 有界缓冲，设备回调不读文件、不等待 Runtime 锁、不发布终态订阅；终态与文件释放在后台完成。源耗尽导致的缓冲不足填静音，不能据此宣称任意存储/设备条件下绝无断音。

设备不可用时保留 PCM 效果目录和逻辑能力，避免设备状态使可移植存档的效果校验失效；实际 Start 通过 Failed 终态降级。
