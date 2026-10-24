# Group Entry 表单按需创建

状态：confirmed
日期：2026-10-24
来源：Group Editor hover/打开卡顿反馈

现象：打开包含多个 Entry 的 Group 页面时，即使虚拟列表尚未显示大部分条目，页面仍会有可感知的同步卡顿。

根因：`GroupEditorPanelViewModel` 在构造时为组内每个 Entry 调用 `ConfigureParameterFields`。该调用会创建参数绑定、DTO 表单投影和候选项；虚拟化只能延后视觉容器，不能抵消这些模型创建工作。

正确做法：Entry 附加到列表可视树且参数已展开时才创建表单；Inspector 选择 Entry 时也显式确保其表单已准备。变量定义变化只刷新已经创建的表单，类型变化仍为当前/已创建 Entry 重建投影。

证据：实际 Group 视图挂载回归覆盖默认展开的可视行、Inspector 表单与 Entry 交互；构建同时保留虚拟列表。

适用范围：虚拟化 Group Entry 及类似的 Editor 列表。禁止在页面构造时为整个数据集预建仅供可见行或详情面板使用的 UI 表单模型。
