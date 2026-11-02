# 列表高亮与剧情提交必须分开

状态：confirmed。

现象：旧 ChoiceList 在 SelectionChanged 直接执行剧情命令，不能用于方向浏览。改为显式提交后，真实键盘确认回归仍失败，因为项目自定义 ChoiceList 主题没有赋予列表可聚焦状态；直接调用 CommitSelection 无法验证键盘链路。

修正：ChoiceList 显式 Focusable，方向只改变高亮，Enter/Space 在列表范围 tunnel 阶段提交；指针点击时只提交实际条目。输入导航测试同时覆盖手柄确认、真实 Window 键盘事件和鼠标点击，防止 ListBoxItem 先消费按键或仅测试直接方法。

适用：剧情选项和使用自定义模板的选择控件。禁止把 SelectedIndex 更新等同于用户确认，或把对象创建/命令直调当作可聚焦与键盘可用的证据。

来源：[手柄导航](../../features/F-20261102-07-gamepad-navigation/design.md)，GamepadNavigationTests.ChoicePointerAndKeyboardStillCommitExplicitly 首次失败与修复记录在 output/gamepad-navigation/navigation-second.trx、navigation-fourth.trx。
