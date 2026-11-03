# 第三轮：问题面板和 Inspector

2026-11-03。Problems 列表移除详情 expander 和行内导航按钮，选择问题由未锁定 Inspector 展示可复制的消息、来源、代码、详情和归档输入；双击问题行或 Enter 导航，空白和操作按钮双击不导航。沿用锁定语义，空 Problems 不主动替换已有 Inspector。保留草稿按钮和既有稳定目标导航。

针对 `GroupProblemsAutomaticallyFollowEditsUndoAndRedoAndKeepPendingInputNeutral`、`UiuxProblemsValidateUnsavedAuthorValuesAndKeepStableEntryNavigation`、`UiuxProjectOwnedPanelsRemainLiveAfterClosingAndReopeningTheirDocks` 的测试 3/3 通过；测试包含问题选择、Inspector 跟随及锁定切换。文档已同步。

仍待真实桌面鼠标双击、键盘、多个 Inspector 与导航后的焦点时序综合验证，P3 保持 in-progress。
