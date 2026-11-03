# Entry Inspector 点击切换先于失焦提交

状态：confirmed
来源：2026-11-03 Editor UIUX 跟进；`InspectorTextCommitsWhenAnotherEntryIsClicked`。

## 现象与原因

在 Entry Inspector 编辑本地化正文后，直接点击 Group 的另一条 Entry，输入保留在项目草稿中而没有应用。Group 列表的 pointer tunnel 阶段先变更选中项，Inspector 随即释放旧表单；正文控件排队的失焦提交执行时编辑器已释放。单独测试点击普通按钮失焦不能覆盖这一时序。

## 做法与边界

在 Inspector 所属窗口的 pointer tunnel 阶段，针对焦点所在字段、且点击目标在该字段外时，使用项目拥有的 FieldDraftState 提交；保留输入法 guard、冲突和无效输入，阻止同一草稿重复提交。草稿提交结束时只清除提交开始时的版本，不能擦除异步期间的新输入。失焦处理仍负责键盘等其他焦点切换。

回归必须挂载真实 Group 和 Inspector，编辑后通过指针点击另一条 Entry，检查原条目已更新；还需覆盖非法数字留存、后续有效输入、删除按钮的选中状态和草稿并发。不要通过延迟重绑或忽略失焦草稿来掩盖切换先后关系。
