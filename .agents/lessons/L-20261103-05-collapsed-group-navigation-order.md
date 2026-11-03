# 默认折叠时先打开 Group 再展开导航目标

状态：confirmed
来源：2026-11-03 Editor 字号与 Group 默认折叠；`GroupProblemsAutomaticallyFollowEditsUndoAndRedoAndKeepPendingInputNeutral`。

## 现象与原因

Group 现有条目首次挂载时默认折叠。问题面板定位若在打开 Group 前先设置目标 Entry 展开，新建 GroupEditorPanelViewModel 的 Attach 会按默认状态重新折叠该 Entry，导致定位后看不到目标。

## 做法与验证

先让 Dock 创建或激活 Group 面板，再展开稳定身份对应的目标 Entry，最后显示 Inspector 和触发字段导航。回归应覆盖尚未打开目标 Group、搜索过滤与锁定/切换等真实导航路径；手动展开或设置新条目展开的用例必须在面板创建后设置状态，不要假定所有条目初始展开。
