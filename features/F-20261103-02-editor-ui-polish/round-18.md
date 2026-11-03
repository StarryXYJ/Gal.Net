# 第十八轮：修正 Ursa 标题按钮背景

2026-11-03。主窗口和设置窗口为 UrsaWindow，Dock 浮动窗口使用 Dock HostWindow 与自定义绘制标题栏，两者复用 Semi CaptionButton。加载完整 EditorControlStyles 后的测试复现了常态不透明：全局普通按钮样式覆盖了 CaptionButton 自身的透明值。

为标题按钮设置已有的 icon 分类标记，排除普通按钮样式，仍由 CaptionButton 和窗口主题处理 hover/pressed、关闭按钮颜色。六种标题按钮名称的常态/悬停/按下及现有图标按钮主题回归共 7/7 通过。首次尝试对普通样式增加多个名称排除条件时，现有图标按钮测试失败；已改用标记并重测通过。原生桌面与关闭按钮专用状态仍待综合验证。
