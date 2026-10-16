# 游戏快捷键需要验证窗口与工具栏焦点

状态：confirmed（本 feature 内暂存；`.agents` 在本会话只读，未写入 lesson 索引）。

## 现象与根因

用户报告游戏页快捷键无效。输入事件只订阅 GameShell 时，焦点在 Window 的按键路由不经过该壳；将 Button 与 TextBox 一并过滤，又使点击工具栏后 H/A/Tab 等无效。原测试先对 root.Focus()，只验证了理想焦点条件，无法发现这两个问题。

## 证据与修正

先增加 `GameplayShortcutWorksWithWindowOrToolbarFocus(false/true)`，旧代码两项均失败；窗口级键盘订阅和分类型焦点策略后两项通过。键盘订阅跟随根控件 attach/detach 到 TopLevel，鼠标监听保留根范围；不通过不可见控件抢焦点。按钮只保留 Enter/Space 的本地激活，文本/选择/范围编辑器与壳外控件不执行游戏动作。

## 适用范围与禁止做法

适用于 Avalonia 播放器、预览器及类似壳内快捷键。不要用 root.Focus() 的单一路径证明快捷键可用；至少覆盖 Window、工具栏、文本编辑、模态状态和解绑重挂，不要为了游戏按键抢夺文本或宿主输入。指针命中测试使用布局后的真实控件坐标，不能假设固定坐标命中按钮。

来源：[F-20261015-04](feature.md)，关联：[设计修正](design.md#9-2026-10-16-游戏快捷键路由修正)、[控件契约](../../docs/spec/control.md)。
