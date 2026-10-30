# 音频创作与播放

默认宿主使用 `MiniaudioAudioPlaybackHost`，原生设备/解码适配位于 `GalNet.Media.Miniaudio`，跨平台 PCM 混音与 DSP 位于 `GalNet.Media.Audio`。LibVLC 继续服务视频和 Gallery，剧情音频不再通过独立 LibVLC player 输出。

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

音轨选择使用项目设置中的稳定音轨 ID，`asset` 使用音频资源 ID。以下示例中的资源 ID 需要替换为项目实际值。

## 重复同一首音乐

```json
{
  "type": "audio.play",
  "parameters": {
    "trackId": "bgm",
    "asset": "music-resource-id",
    "placement": "replaceAll",
    "loopCount": 0,
    "completion": "discard"
  }
}
```

`loopCount` 为总播放次数：1 播放一次、3 连续播放三次、0 无限重复。默认值为 1。重复通过 Runtime 收到结束事件后重新启动，不保证无缝衔接。无限重复会占用当前逻辑声部，直到被停止、替换或 next 移除。

## 多首曲目轮播

在允许排队的单声部音轨上，依次添加两个 `audio.enqueue` 条目，每个条目分别设置不同的 `asset`，并都设置：

```json
{
  "trackId": "bgm",
  "asset": "music-a-resource-id",
  "loopCount": 1,
  "completion": "requeueTail"
}
```

结果按 A、B、A、B 轮播。只有一个条目时会反复播放这一项。`completion` 默认为 `discard`，表示播放次数耗尽后丢弃；`requeueTail` 每次回到队尾时重置该项的播放次数进度。排队仍受音轨容量和溢出策略限制，失败项不会重新入队。

## 停止、等待与恢复

- `audio.stop` 停止当前项，`clearQueue` 为字符串 `"true"` 时同时清空待播项；不会因 completion 再次启动。`audio.next` 丢弃当前项并切换下一项。`audio.clear` 只清空待播队列。
- `autoBlocking` 为字符串 `"true"` 时，自动推进等待完整逻辑播放结束；无限循环或轮播要靠停止/跳过终止等待。通常让 BGM 保持默认非自动阻塞。
- 暂停保留播放和队列；读档从当前轮开头恢复，保留已完成的有限循环轮数，不恢复音频游标或淡化时钟。
- 淡出期间调整音量或静音仍然有效。媒体文件直接复用已有资源租约；最后一个声音、视频或 Gallery 消费者释放后才删除临时文件。
