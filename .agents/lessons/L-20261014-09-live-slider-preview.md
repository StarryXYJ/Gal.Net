# 连续滑块应走实时预览而非异步提交

状态：candidate  
来源：Sample 设置音量拖动卡顿修复（2026-10-14）

## 现象

Sample 的音量滑块拖动时会暂停跟手，重新拖动也不顺畅。源码显示滑块每个 `Value` 变化都会提交设置；提交期间 `FieldEditorContext.IsEnabled` 为 false，表单同步禁用控件。

## 根因

普通设置提交包含串行队列、运行时应用、播放状态刷新和玩家变量持久化。滑块把这条完整异步提交路径用于连续输入，`_submitting` 会暂时禁用控件并打断鼠标拖动。等待鼠标松开并不能解决根因，只会延迟反馈。

## 正确做法

- 只有声明支持预览的绑定才为滑块提供同步、轻量的实时预览；其他滑块保留原有提交语义。
- Sample 音量预览只更新运行时音量和内存变量，不等待播放引擎刷新，也不在每个刻度写磁盘。
- 最后一次变化后短暂空闲时，通过设置协调器合并保存；会话显式 flush 时也先刷未保存的预览值。
- 预览路径不设置 submitting/applying 状态，保持滑块可拖动。

## 证据和范围

`DefaultFieldEditors.CreateSlider`、`FieldEditorContext.IsEnabled`、`FieldEditorContext.CommitAsync`、`SettingItem.ApplyValueAsync` 和 Sample 音量设置链路共同构成该问题。该规则适用于需要连续反馈的滑块；离散选择、文本框和慢速外部操作仍使用普通异步提交。

本修复经目标 Sample 项目编译确认；尚未进行运行中的 GUI 拖动复现。
