---
status: confirmed
date: 2026-11-02
feature: F-20261102-06-player-layout-polish
---

# 限宽编辑器要验证控件本身的列对齐

设置表单的编辑器容器左边界一致，不代表内部 ComboBox、Slider 和 TextBox 都对齐。P5 截图中，将 ComboBox 设为 Stretch 并保留 MaxWidth=360 后，它在更宽的容器内居中；下拉框起点约为 x=607，而同列滑条、数值框为 x=554。无溢出和容器列断言都发现不了这一偏移。

固定短输入和下拉框使用 Left，滑条按剩余空间伸展。验证输入控件相对 SettingRow 的实际起点，覆盖双列与上下重排；不要仅检查 MaxWidth 或父容器坐标，也不要通过重建编辑器纠正布局。整体 Viewbox 缩放后的窗口坐标仍须用 TranslatePoint。

发现方式：首次渲染截图中观察到列内偏移，修正后重新检查同名截图；对应回归 PlayerLayoutPolishTests.PageHeadersStayAlignedAndNavigationTextFollowsTheFontChoice 验证控件实际起点。修正后相关 132 项全部通过，证据为 output/layout-refinement/regression.trx 与 after/ 截图。
