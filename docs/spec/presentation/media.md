# 媒体后端契约

## 对象职责

- 播放实例：资源、内容 gain、`fadeIn` / `fadeOut`、`loopCount`（0 为持续重复）和 `completion`（丢弃或重新入队）。替换时默认采用旧实例自己的淡出；显式 `audio.stop.fadeOut` 可覆盖。自然结尾淡出需要解码器提供时长。
- Track：`maxVoices`、有界队列/溢出规则、`playbackOrder`（Sequential/Random）、`repeat`（None/One/All）、轨道音量和效果、`busId`。随机选择状态随存档恢复。失败、显式停止不会按自然结束重新入队。
- Bus：真实汇总所属 Track，再执行一次共享效果链和整组音量。轨道不指定 Bus 时直接进入 Master。

顺序为实例包络/gain → Track 汇总/有序 SEffect/音量 → Bus 汇总/有序 SEffect/音量 → Master 音量和限幅。Bus 效果不会复制到每个 Track 或实例。空输入仍处理效果尾音；停止音源不会清空房间混响。读档/reset 会清空旧 DSP 缓冲，不保存精确播放游标、包络时钟或尾音。

## 音频显示名称

`AudioPlayOptions.announceName` 为默认关闭的播放提示开关，传入 `AudioPlayRequest.AnnounceName`；资源名称来自 `AssetMeta.DisplayNameKey`。运行时只为新执行且显式启用提示的逻辑播放保留瞬时提示资格。排队不会提前触发，只有匹配当前 voice/generation 的后端 Started 才产生 `BuiltinAudioModule.PlaybackStarted`，每个逻辑播放最多一次。失败、过期回调、循环、恢复和追赶不重新授予资格；该资格不进入存档。

事件在音频/状态锁外串行派发，隔离观察者异常；观察者负责切换 UI 线程。Started 事件携带资源身份，宿主在 UI 线程按当前语言解析资源名称；未命名资源不显示。默认 Avalonia 宿主使用 Ursa 提示容器和主题化提示条：最小宽度 280 DIP、背景不透明度 0.84，向右滑入 0.8 秒后停留两秒，2.8 秒后开始向左滑出及淡出，退场持续 0.8 秒，动画结束后从容器移除；后端与 primitives 不依赖 Ursa。提示开关不改变读档、循环或追赶的抑制规则；鉴赏试听和场景回放不弹曲名提示。
