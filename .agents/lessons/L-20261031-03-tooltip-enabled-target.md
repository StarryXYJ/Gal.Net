# ToolTip.IsEnabled 会禁用目标控件

状态：confirmed
来源：features/F-20261031-01-resource-gallery/summary.md

现象：鉴赏锁定音乐的 Title 已是 ???，但文字仍使用禁用样式；无名称但已解锁的卡片也可能无法点击。最初测试只检查 Text 值，未检查实际启用状态，因此未发现。

根因：`ToolTip.IsEnabled` 并非独立的提示开关；Avalonia 该字段声明于 InputElement，和目标的 IsEnabled 是同一属性。将它绑定到 HasName 会禁用没有名称的文字控件，放到 Button 上还会与 IsUnlocked 绑定争用。

正确做法：用 nullable tooltip 内容，空名称时设置 ToolTip.Tip=null，禁止借 ToolTip.IsEnabled 控制空提示；目标控件只按自身交互条件控制 IsEnabled。状态文字应保持可读。

验证：反射确认字段 DeclaringType 为 Avalonia.Input.InputElement；新增锁定曲名 IsEffectivelyEnabled 断言在修改前三套主题均失败，修正后完整展示层 358 项通过，并覆盖未命名已解锁卡片可点击及真实截图。

适用：所有 Avalonia XAML 中使用类名限定继承属性的场景。测试可见状态时，不只断言绑定数据，还要检查实际控件的可见性、有效启用状态与交互。
