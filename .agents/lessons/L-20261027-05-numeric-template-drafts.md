# 数值控件的模板初始化不能成为作者输入

状态：confirmed
日期：2026-10-27
来源：F-20261023-01-editor-uiux / follow-up-group-diagnostics-numeric-20261027

现象：把保留草稿的数字 TextBox 换成 NumericUpDown、只覆盖 OnTextChanged 和 OnLostFocus，虽然 `.22` 可写入，却在挂载表单时产生空草稿。结构化列表/DTO 操作后保存出现源冲突，项目设置已提交值被初始草稿覆盖，外部文件刷新被误判为本地修改。

根因：原生 NumericUpDown 的模板挂载还会通过 Value/内部输入框同步文字；只抑制 TextProperty 的工厂初始化或改写格式化回调不足以隔离这些通知。Avalonia TextChanged 事件又晚于立即的 Enter 提交，不能用它捕获同一消息中的最新文字。

发现与证据：首次 Editor 全回归 15 项失败；加入挂载后 PendingCount=0 断言复现。改为借用原生主题、由字段编辑器直接拥有模板 TextBox/ButtonSpinner 事件后，结构化列表、DTO patch、设置、外部刷新等聚焦回归通过。输入使用 TextBox.PropertyChanged 同步捕获，显式刷新用同步标记保护。

规则：作者数字文字和提交权属于 FieldEditorContext/FieldDraftState。模板初始化、换主题、默认值显示和外部刷新不得制造草稿；不能用 NumericUpDown.Value 的自动转换/钳制回写作者 JSON。保留原生外观时需确认实际输入、按钮及键盘行为，验证初始无草稿、同消息输入后 Enter、规范回显、Undo 和关闭后保存。

不要仅凭源码赋值或单个合法数字测试判定控件可用，也不要靠清空项目草稿来掩盖初始化副作用。
