# 程序化 TextBox 刷新不能通过延迟 TextChanged 标记草稿

状态：confirmed
日期：2026-10-20
来源：F-20261019-01-editor-game-localization / P3

## 现象和证据

首次完整 Editor 回归中 AuthoringDraftsReopenInMountedGroupAndKeepOpaqueValuesThroughEditsCopyUndoAndSave 失败：旧项目 speaker 保存的是待修复 JSON 对象，仅挂载页面并修改另一字段就变成字符串。失败记录为 Temp/localization-p3-GalNet.Editor.Tests.log；修正后的完整重跑 Temp/localization-p3-GalNet.Editor.Tests-final.log 中该项通过，210 项为 208 通过和 2 项已记录布局失败。

## 根因与修正

Avalonia TextBox.TextChanged 延迟派发。Refresh 在 updating=true 内赋 Text，finally 解除标志，随后事件才到达并被误判为用户输入；输入未聚焦又触发失焦提交，导致未主动编辑的作者值被覆盖。

CharacterFieldEditor 改订阅同步的 TextBox.TextProperty PropertyChanged，并在同一 updating guard 内忽略程序化赋值。异常非字符串 JSON 显示原始 JSON 文本但不自动提交；普通字段和姓名刷新也不再生成草稿。新角色提交成功后主动刷新候选缓存，避免提交期间因 context.IsEnabled=false 跳过来源通知而继续显示 raw ID。

## 适用范围

任何把输入框变更自动映射为草稿、失焦保存或创建记录的自定义编辑器。禁止用只覆盖属性赋值瞬间的 bool guard 假定延迟事件已被抑制；必须用同步属性事件或明确版本/期望值判定，并通过真实挂载、旧异常值和编辑其他字段的测试验证不会无意写入。

关联：docs/spec/editor-game-localization.md；features/F-20261019-01-editor-game-localization/phase-3.md。
