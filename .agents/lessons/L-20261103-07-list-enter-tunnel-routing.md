# 列表导航键需要在控件处理前路由

状态：confirmed。日期：2026-11-03。

Problems 列表的 Enter 导航若只依赖 ListBox 的普通 KeyDown 事件，Avalonia 列表自身可能先处理 Enter，外层事件处理器收不到按键。直接调用导航命令的测试无法发现此问题。

在列表上使用 tunnel 阶段处理 Enter，同时排除行内按钮的按键，防止草稿操作被误当作导航。回归需在真实挂载控件上验证单击只选择、Enter 导航、双击仅导航一次，并保持原有稳定身份和失效目标保护。

来源：[Editor UI 优化](../../features/F-20261103-02-editor-ui-polish/feature.md)，实际控件测试修复前失败、修复后通过。
