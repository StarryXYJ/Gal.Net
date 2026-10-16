# Editor 补全控件的 nullable 失焦边界

状态：candidate
来源 Feature：F-20261015-02-editor-authoring-workspace

## 现象与根因

可编辑 Avalonia `ComboBox` 失去焦点时，可能把空输入同步为 `null`。Editor 的专属参数 ViewModel 将 `StringValue` 声明为非空 `string`，但绑定运行时仍能写入 `null`；`OnStringValueChanged` 中的 `value ?? ""` 只规范化写入参数字典，不会把属性自身恢复为非空值。

失焦事件随后把该属性传给业务提交方法。若提交方法先调用 `Trim()`，就会在空输入失焦时触发 `NullReferenceException`；外层通常会被 Avalonia 事件反射包装成 `TargetInvocationException`。

## 正确做法

UI 事件进入业务边界时仍须按 nullable 输入处理：先判断 `null`/空白，再执行 `Trim()` 或持久化。对“空说话人”这类合法 UI 状态，不应新增项目说话人，也不应因失焦抛异常。

## 发现与证据

堆栈定位到 `GroupEditorPanelViewModel.CommitSpeaker` 的 `value.Trim()`；调用者是 `EntryListEditorControl.OnAutocompleteLostFocus`。修复将提交参数改为 `string?` 并在 `Trim()` 前拒绝空白值，同时增加 Editor 回归测试覆盖 `null` 和空白输入。

## 适用范围

所有由 Avalonia 可编辑文本/补全控件驱动的 Editor 业务回调，尤其是可选字符串字段。不要仅依赖 C# 非空声明或属性变更回调中的下游 `??` 来保证 UI 输入非空。

