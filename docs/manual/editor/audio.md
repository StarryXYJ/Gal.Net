# 音频创作与播放

默认宿主使用 `MiniaudioAudioPlaybackHost`，原生设备/解码适配位于 `GalNet.Media.Miniaudio`，跨平台 PCM 混音与 DSP 位于 `GalNet.Media.Audio`。LibVLC 继续服务视频和 Gallery，剧情音频不再通过独立 LibVLC player 输出。

项目设置的“音频设置”按总线展示：展开总线可以编辑名称、ID、默认增益，并查看和添加它的轨道。轨道展开后显示名称、ID、播放顺序、循环、默认增益和最大声音数；总线归属、队列与溢出策略等放在“进阶设置”。宽窗口采用两列表单，窄窗口改为单列；每个可重置字段保留重置图标。

总线和轨道标题显示名称、ID 以及轨道数量或播放摘要。右侧更多菜单提供排序、轨道复制和删除；复制轨道会生成唯一 ID。修改总线 ID 会同步更新其轨道的引用；删除总线会保留轨道并移入“未分配 / 引用失效的轨道”区域。轨道的总线字段可从当前总线列表选择，尚未提交的总线草稿也会显示；选择“无”表示不归属总线。无法解析的 ID 保留显示，不会因打开设置被自动替换。

修改先保留在项目设置草稿中，提交后进入项目历史，支持撤销和重做。关闭设置视图再打开会保留未完成输入；无效输入会阻止保存。

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

同一精确挂载身份在不同分支来自不同类型时，仍能停止，但更新表单不会猜测参数类型。既有未知类型、参数和失效引用保留，查询刷新不自动清空或改选。音效参数与混音 gain 可通过 Timeline 的 audioTracks 动画；事件及使用方式见下文。

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


## 扩展音效

| ID | 参数（默认值） |
|---|---|
| seffect.delay | delaySeconds=0.25（0.001～2）、feedback=0.35（0～0.95）、mix=0.25 |
| seffect.pan | pan=0（-1～1）；立体声平衡，居中左右保持原增益，正值衰减左声道、负值衰减右声道 |
| seffect.eq | frequencyHz=1000（20～20000）、q=0.7071068（0.1～10）、gainDb=0（-24～24）；峰值均衡 |
| seffect.distortion | drive=2（1～20）、mix=0.25；软失真 |
| seffect.bitcrusher | bits=8（2～16）、downsample=1（1～64）、mix=0.5；位深/采样保持参数取整 |
| seffect.compressor | thresholdDb=-18（-60～0）、ratio=4（1～20）、attackMs=10（0.1～200）、releaseMs=100（5～2000）、makeupDb=0（0～24）；左右联动 |

各效果都支持 Track/Bus。mix 范围均为 0～1。Delay 与混响都可在音源结束后保留尾音；读档/reset 清空延迟缓冲，移除效果会平滑旁路。

## Timeline 音频

Timeline 允许 audio.play/stop、audio.seffect.apply/update/stop、audio.mix.set 事件。事件以相同播放时钟在对应帧触发，不额外生成剧情检查点；快进不补播未来 audio.play，Quick/CatchUp 不触发瞬时声音。持续声音应通过普通音频条目管理，或明确安排停止事件。

`audioTracks` 中每项指定 scope（Track/Bus）、targetId、instanceId、parameter、keys。instanceId 为空时 parameter 只能为 gain，作用于剧情混音，保留 muted；不为空时更新直接挂载效果的指定数值字段，保留其他参数。关键帧规则与视觉轨道一致，同一精确参数的新播放替换旧播放的控制权。

```json
{
  "type": "timeline.play",
  "parameters": {
    "timeline": {
      "playbackHandleId": "muffle", "frameRate": 60, "durationFrames": 180,
      "blocking": true, "skippable": true, "tracks": [], "events": [],
      "audioTracks": [{
        "scope": "Bus", "targetId": "bgm", "instanceId": "filter", "parameter": "cutoffHz",
        "keys": [{"frame": 0, "value": 20000}, {"frame": 180, "value": 500}]
      }]
    }
  }
}
```

先挂载 filter 实例，再播放上例。正常完成/立即跳过提交末值；scope 取消或替换不补播未来事件。一次性音频参数动画不保存精确时钟，读档保留已提交参数值；循环沿用 Timeline 定义恢复。无呈现的循环只建立第零帧状态，显式停止再执行终态清理。

## 语音压低背景声

```json
{"type":"audio.duck","parameters":{"instanceId":"voice-duck","sourceTrackId":"vocal","scope":"bus","targetId":"bgm","gain":0.35,"attackMs":80,"releaseMs":400,"enabled":true}}
```

规则随剧情存档，包络不存档。PCM 根据源轨道已经开始实际输出且未暂停/结束的声音压低目标；加载待播、排队不触发。gain 与剧情音量、玩家偏好相乘；多个规则取当前最小目标 gain。相同 instanceId 更新规则，enabled=false 移除规则并恢复。禁止目标包含源轨道的自压低路由。此能力是播放状态驱动的 duck，不是完整信号侧链压缩。
