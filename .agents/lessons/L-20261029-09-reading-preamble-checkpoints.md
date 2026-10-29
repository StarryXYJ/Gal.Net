# 阅读复合条目的存档边界应覆盖语音前导

状态：confirmed
来源：F-20261029-03-dialogue-history

## 现象和根因

带语音对白编译为非流程阻塞 audio.play 和阻塞 dialogue.text。Engine 原先在音频后发布稳定快照，EntryIndex 指向文本；读档只重执行文本，不能通过本次执行恢复同源语音关联，而且恢复的音频缺少原语的自动等待所有权。

## 正确做法

复用 readingBoundaryId，从前导原语开始至阅读原语完成前保持此前稳定快照。阅读原语进入后由原有阻塞状态继续保护；CatchUp 完整执行后才能发布新稳定点。宿主历史按快照中的小游标截取正文，当前句读档重执行不重复。

展示完成的异步 continuation 不保证先于玩家下一次推进执行；历史游标还须在 OnCheckpointCreating（Engine 执行门内）同步提交。不能只依赖后台观察 PresentationCompletion。CheckpointFlushesPresentedTextEvenIfItsPresentationContinuationHasNotRun 用延迟的呈现完成任务覆盖该交错。

## 证据

DialogueHistoryTests.EngineCheckpointRestartsTheWholeVoicedLineAndKeepsHistoryWithoutDuplicates：显示带语音当前句时保存仍为整句入口，读档重新执行音频/文本并保留语音引用；进入下一句后只保存此前历史前缀。Runtime 124 项和现有自动等待集成用例通过。

## 边界

不要只序列化一个历史语音字段掩盖半句快照问题，也不要从当前正在响的音频推断文本关系。Core/Runtime 仅处理阅读边界，不识别具体音频或对白类型。
