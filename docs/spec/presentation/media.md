# 媒体后端契约

## 对象职责

- 播放实例：资源、内容 gain、`fadeIn` / `fadeOut`、`loopCount`（0 为持续重复）和 `completion`（丢弃或重新入队）。替换时默认采用旧实例自己的淡出；显式 `audio.stop.fadeOut` 可覆盖。自然结尾淡出需要解码器提供时长。
- Track：`maxVoices`、有界队列/溢出规则、`playbackOrder`（Sequential/Random）、`repeat`（None/One/All）、轨道音量和效果、`busId`。随机选择状态随存档恢复。失败、显式停止不会按自然结束重新入队。
- Bus：真实汇总所属 Track，再执行一次共享效果链和整组音量。轨道不指定 Bus 时直接进入 Master。

顺序为实例包络/gain → Track 汇总/有序 SEffect/音量 → Bus 汇总/有序 SEffect/音量 → Master 音量和限幅。Bus 效果不会复制到每个 Track 或实例。空输入仍处理效果尾音；停止音源不会清空房间混响。读档/reset 会清空旧 DSP 缓冲，不保存精确播放游标、包络时钟或尾音。

## 音频显示名称

`AudioPlayOptions.announceName` 为默认关闭的播放提示开关，传入 `AudioPlayRequest.AnnounceName`；资源名称来自 `AssetMeta.DisplayNameKey`。运行时只为新执行且显式启用提示的逻辑播放保留瞬时提示资格。排队不会提前触发，只有匹配当前 voice/generation 的后端 Started 才产生 `BuiltinAudioModule.PlaybackStarted`，每个逻辑播放最多一次。失败、过期回调、循环、恢复和追赶不重新授予资格；该资格不进入存档。

事件在音频/状态锁外串行派发，隔离观察者异常；观察者负责切换 UI 线程。Started 事件携带资源身份，宿主在 UI 线程按当前语言解析资源名称；未命名资源不显示。默认 Avalonia 宿主将曲名交给通用 TextCardHost，默认位置 BottomLeft、样式 music；应用在 PlayerView 定义 TextCardTheme.music，左右位置决定默认滑动方向，同位置替换。入场/退场默认各 0.8 秒，AudioAnnouncementDuration（默认 3 秒）包含入场与停留，不包含退场。提示开关不改变读档、循环或追赶的抑制规则；鉴赏试听和场景回放不弹曲名提示。


默认 PCM 效果库还包括 Delay、立体声平衡、峰值 EQ、软失真、位深/降采样与左右联动 Compressor。AudioDuckingState 保存路由规则，瞬时压低包络由 PCM 混音器根据实际开始且未暂停/结束的源声部计算，叠加在剧情/玩家音量上；重置清空。Timeline 音频参数与控制事件由 BuiltinAudioModule 复用 AudioRuntime 执行，设备回调不处理剧情 Entry。
