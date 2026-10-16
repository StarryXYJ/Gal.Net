# 可调整提示应验证实际编辑入口

状态：confirmed。按 capture-lesson 流程暂存 feature；`.agents` 本会话只读，未改其索引。

用户要求让 Control 等键值像输入框，上一轮误把每条设置行包成带描边卡片，且测试只证明行外框存在。用户已明确纠正目标是绑定编辑入口本身。

旧实现三主题的 `BindingFieldsHaveARealButtonTemplateAndForegroundHoverFeedback` 全部在边框断言失败：InputBindingCapture 已有模板，但实际 BorderThickness 为 0。正确做法是去掉行外框，明确复用 Button 的样式键，在编辑器类上定义边框和悬停状态；测试需同时检查模板里的边框和实际文字前景，而不只检查祖先/根控件属性。

设置模板还无条件创建 ToolTip 的 PlayerTextBlock，即使 ReasonKey 空、文字隐藏，Tip 内容对象仍存在，造成空提示容器。原有内联原因已足以说明不可用状态，本次移除重复 Tooltip，保留内联说明。

适用于自定义派生编辑控件和可调整提示。禁止用祖先框样式测试代替实际编辑入口的视觉验证；不要让无内容的提示控件仍成为非空 Tip。

来源：[feature](feature.md)、[设计修正](design.md#11-绑定编辑框与空-tooltip-修正)。
