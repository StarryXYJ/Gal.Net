# 日志追加的 Extent 变化不等于用户离开底部

状态：candidate
来源：2026-09-29 Sample Avalonia DebugLogWindow 自动追随修复

## 现象

仅在日志追加时调用“是否在底部”的判断，会让用户已向上滚动的视图被强制拉回底部；直接在每个 `ScrollChanged` 事件中重算状态，又会把日志追加造成的内容高度变化误判为用户离开了底部。

## 根因

Avalonia 的 `ScrollChangedEventArgs` 同时报告 `ExtentDelta`、`OffsetDelta` 和 `ViewportDelta`。追加日志只会改变 `Extent`，而用户拖动或滚轮滚动会改变 `Offset`。

## 正确做法

维护独立的 tail-follow 状态：仅当 `OffsetDelta.Y` 非零时按当前位置更新该状态；追加时仅在状态为 true 时排队 `ScrollIntoView`，并在真正滚动前再次检查状态。

## 适用范围

Avalonia 中任何有实时追加内容、并允许用户查看历史的日志或聊天列表。

## 不要做什么

不要用内容高度变化作为用户滚动意图的代理，也不要在新项到达后无条件执行 `ScrollIntoView`。

## 证据和关联文档

- `src/Samples/GalNet.Sample.Avalonia/Views/DebugLogWindow.axaml.cs`
- Avalonia 12 `ScrollChangedEventArgs` API 文档
