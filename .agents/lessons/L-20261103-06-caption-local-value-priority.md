# 标题按钮的本地背景会压过悬停样式

状态：confirmed。日期：2026-11-03。

## 现象与原因

为浮动窗口的标题按钮直接设置 `Background="Transparent"` 看起来可以去掉常态底色，但 Avalonia 本地值的优先级高于控件主题的 pointerover/pressed 样式，导致按钮交互底色不能正常出现。依赖的 Semi 12.0.1 `CaptionButton` 主题本身已经将常态 Background 设为 Transparent，并通过状态样式设悬停与按下背景；Ursa 2.1.0 的绘制标题栏复用该主题。

进一步通过加载完整 EditorControlStyles 的失败测试确认，真正造成现有标题按钮常态不透明的是全局 `Button:not(.editor-icon)` 样式：它覆盖了 CaptionButton 的透明主题。对标题按钮名称设置已有的 `EditorButton.IsIcon` 标记，使普通按钮样式不匹配，标题主题自行提供三种状态；不要通过增加一长串 `:not(#...)` 提高普通按钮样式优先级，该方法曾破坏已有图标按钮的透明状态。六个标题按钮名称及现有图标按钮回归通过。

## 正确做法

先检查目标模板和实际主题值，区分标题栏或窗口底色与按钮底色；不要在按钮实例上设置本地 Background 来覆盖主题状态。若需要定制，保持常态和状态都在可覆盖的同一主题/样式层级，并实测普通、悬停、按下及关闭按钮。不要将 Sample 的 `fit-aspect` 样式误认为系统标题按钮样式，它只用于 Sample 的额外按钮。

## 来源

[F-20261103-02 Editor UI 优化](../../features/F-20261103-02-editor-ui-polish/feature.md)；Semi 12.0.1 `Controls/WindowDrawnDecorations.axaml` 与 Ursa 2.1.0 `Controls/UrsaWindow.axaml` 对照，代码中撤回本地值。真实原生桌面状态仍需实际验证。
