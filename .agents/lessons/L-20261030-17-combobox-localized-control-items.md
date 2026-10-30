# ComboBox 的本地化控件选项

- 状态：confirmed
- 来源：2026-10-30 用户报告触屏操作设置修改后选中区空白，切换页面恢复。
- 关联：`docs/spec/presentation/game-view.md`、`test/GalNet.Presentation.Tests/InputChoiceDisplayTests.cs`

## 现象与根因

InputBindingsPanel 曾把 PlayerTextBlock 实例直接放进 ComboBox.ItemsSource。Avalonia 12.0.5 对 Control 类型选项用 Rectangle/VisualBrush 引用原控件作为 SelectionBoxItem，而非创建独立文本。关闭 popup 时 PlayerTextBlock 脱离视觉树，释放 TextProperty 的本地化绑定，Text 回到 null；选中值仍正确，但镜像也变空。手柄确认布局有同样问题。

## 复现与修正

先用内存 Headless 探针观察到 SelectedIndex 不变而 Text 在 popup 关闭后变空。随后使用真实 AvaloniaPlayerLocalization 和设置提交路径，在 Classic、Minimal、Paper 的触屏/手柄六组回归中全部复现，再验证修复后全部通过。测试还覆盖关闭后切换三种语言、重新打开 popup 和编辑器重建。

选项保存本地化 key，ItemTemplate 各自创建 PlayerTextBlock。保持控件正常 detach 释放，不通过保留卸载控件绑定、切页刷新或重置 SelectedIndex 掩盖问题。测试必须开启真实本地化 resolver 并检查已关闭选中区的实际文字，不能仅检查配置值或使用字符串选项代替真实路径。
