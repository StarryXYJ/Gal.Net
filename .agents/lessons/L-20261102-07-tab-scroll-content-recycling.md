---
status: confirmed
date: 2026-11-02
feature: F-20261102-06-player-layout-polish
---

# 分类正文复用时，滚动恢复必须跟随 DataContext

## 现象与原因

SettingsCategories 改为固定分类头、正文独立滚动后，最初仅在 ScrollViewer.AttachedToVisualTree 恢复分类偏移。分类切走再切回，120 的滚动位置变为 0；行为测试证明实际复用了同一个 ScrollViewer（最终 TRX 输出 `Category content ScrollViewer reused: True`）。切换 DataContext 不保证重新触发挂载。

ScrollChanged 的延迟事件也不适合单独持有偏移：模板切换和重新测量可能先把位置重置。应同步观察 OffsetProperty，并在 DataContext 改变时开始恢复，临时阻止过渡重置覆盖已保存位置；Loaded 队列恢复前校验版本、挂载和分类身份，避免旧回调回写。

## 验证与边界

PlayerLayoutPolishTests.CategoryNavigationRestoresScrollAndResizePreservesTheEditor 复现旧实现失败，修正后验证切换恢复、原编辑器保留、稳定字号与原生键盘切换。证据：output/player-layout-polish/regression/regression.trx。

范围是可回收/复用的分类内容。不能只用 VM 选中项或 Attached 事件证明滚动体验正确。正式行为见 docs/spec/presentation/game-view.md。
