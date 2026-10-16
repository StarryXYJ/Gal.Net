# 表单测试必须验证真实视觉树

状态：confirmed
来源 Feature：F-20261013-02-parameter-and-sample-simplification / Phase 2b

## 现象与根因

SchemaForm 的工厂生成了 4 个编辑控件且 Editors 列表正确，但当前测试宿主加载只有 ControlTheme 资源的样式文件后，视觉树只有 SchemaForm 本体，没有编辑子控件。样式改为用 selector 显式设置该主题后，控件实际挂载；这又暴露 ComboBox 重置选中项期间会以 null 调用 item template，原先强制解引用 option 导致 NullReferenceException。只验证工厂输出列表会漏掉这两个问题。

## 发现与验证

SchemaFormTests.DefaultFactoriesMountInDeclarationOrderAndCreateIndependentControls 使用真实 Window、FluentTheme、SchemaForm 结构样式，Show/UpdateLayout 后断言窗口视觉树包含实际 TextBox。ChoiceOptionsAndHostStateUpdateWithoutRecreatingControls 在挂载后更新选项和选中值。修正显式主题选择和 null-safe template 后，153 项完整 Presentation 测试通过。

## 正确做法与禁止做法

UI 测试需验证挂载后的视觉子控件、绑定与真实控件事件，不能把对象创建成功等同于可见表单可用。主题不含控件模板时列表仍可有内容；模板 build 回调必须接受框架更新期间的空数据。测试派发事件要使用事件实际的参数类型，LostFocus 在 Avalonia 12 使用 FocusChangedEventArgs。异步 session 测试显式使用 Dispatch<bool>，关联已有 async-void lesson。

## 适用范围

SchemaForm、设置和 Editor 的生成式控件、主题切换/选项刷新及任何动态 data template。此处只记录已验证的宿主场景，不推断所有 ControlTheme 的隐式资源解析行为。
