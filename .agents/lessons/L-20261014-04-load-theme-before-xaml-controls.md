# Headless 宿主在构造 XAML 控件前加载主题

状态：confirmed
来源：F-20261013-02-parameter-and-sample-simplification / Phase 2c

## 现象与证据

真实 EntryListEditorControl 测试中 ListBox.ItemCount 为 1，TryFindResource(typeof(ListBox)) 为 true，但 Template 为 null，视觉树没有任何列表项，SchemaForm 无法挂载。切换 Fluent/Semi、修改主题变体和排空 Dispatcher 都未解决。将 SemiTheme 加载移到 EntryListEditorControl 构造之前后，编辑、撤销、重做和保存测试通过。

## 根因和规则

此场景下 XAML 初始化期间已经发生隐式控件主题查找；后续加载资源没有刷新此前的空模板结果。测试应用应在构造 XAML 页面前加载实际主题，与生产 App.Initialize 的顺序一致。不要只确认资源后来可查到，也不要为了缺失测试主题给产品添加模板/绑定包装。

## 适用范围

Avalonia 12.0.5 Headless 的真实 XAML 面板测试。与 L-20261014-03-form-tests-must-mount-editors 相互补充；此证据不推断所有运行期主题切换行为。
