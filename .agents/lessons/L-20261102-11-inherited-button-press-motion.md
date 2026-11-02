---
status: confirmed
date: 2026-11-02
feature: F-20261102-06-player-layout-polish
---

# 替换按钮模板不会移除基础主题的按压缩放

P8 要求按钮交互只改变背景。项目已经使用自己的Button/ToggleButton模板，但实际MouseDown后RenderTransform仍为非单位矩阵，TranslatePoint在取帧期间持续变化。仅断言Bounds/字号不变发现不了这类渲染缩放。

Avalonia Fluent 12.0.5 的Button与ToggleButton主题在控件根的:pressed设置scale(0.98)，并有75ms的RenderTransform过渡；这些与模板部件无关，换掉ControlTemplate仍会保留。来源：https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Themes.Fluent/Controls/Button.xaml 及同目录ToggleButton.xaml。

在应用的Button范围统一取消这层Transitions，并显式覆盖:pressed的RenderTransform为none；不改引擎或全局宿主。用真实按下验证最终Transform矩阵为单位矩阵，再检查实际像素、文字与布局；不能仅凭自定义模板判断默认交互已经移除。

证据：output/button-feedback/buttons.trx 中诊断输出包含按下时的变换与小数起点；修正后的 layout-fixed.trx 共50项通过，包含三套配色的真实像素/单位变换及五种视口的工具栏按下、选中和键盘焦点验证。
