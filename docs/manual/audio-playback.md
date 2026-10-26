# 音频重复与轮播

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
