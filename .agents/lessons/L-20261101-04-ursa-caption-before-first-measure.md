# Ursa 标题栏必须在首次测量前预留系统按钮宽度

状态：confirmed
来源：2026-11-01 Sample 开场后续修复（独立实现，未创建 feature）。

## 现象与证据

Ursa 2.1.0 的 UrsaWindow.OnLoaded 才读取 WindowDrawnDecorations 按钮 Bounds 并设置 TitleBar.Margin。原生 Windows 探针在第一次有效布局及 base.OnLoaded 前测得：1280 宽窗口中 FitAspectButton 位于 [1240,1280]，系统按钮区从 1184 开始，关闭按钮位于 [1248,1280]；下一次布局才退到 [1144,1184]。Headless 宿主没有原生 decorations，现有仅检查 VM 或窗口类型的测试不能覆盖。

## 正确做法

Sample MainWindow.Chrome 在 MeasureOverride 调用基类前，查找真实 WindowDrawnDecorations.Content.Overlay，测量可见的系统 caption 按钮，以 DesiredSize.Width 总和预留 TitleBar 的右边距；不计 fullscreen popover、隐藏按钮或其他用户控件，不硬编码按钮宽度，也不用延时或等待内容加载。保留 TitleBarMargin 的项目偏移。系统按钮可见性改变时使窗口重新测量；Loaded 后重申测量值，避免基类一次性 bounds 计算覆盖。

原生探针修复后的第一有效布局即为 [1144,1184]，Loaded 前后相同；窗口缩窄、隐藏最小化、仅留关闭和恢复按钮均通过非重叠断言。临时探针及 before/after JSON 已按用户要求清理；机制在 Views/MainWindow.Chrome.cs。

## 适用范围

当前 Ursa 2.1.0/Avalonia 12 的自绘系统按钮与 RightContent 共存。原生/Headless 无自绘装饰时不强加留白；升级 Ursa 后重新检查上游是否已修复，避免保留无效 workaround。规范见 docs/manual/sample/gameplay-and-settings.md。
