# 表面反馈动画统一

用户在首次反馈分析后明确授权“统一下动画”。范围为按钮、Entry 类型选择器、图标按钮、输入与菜单的背景/边框反馈。

## 实现

- 新增 `EditorMotion.HoverDuration`，集中定义 160ms。维持零延迟与线性曲线，以及既有预乘 alpha 插值，避免把结构问题简化为缩短时长。
- Button/ToggleButton 的 Background/BorderBrush 绑定来源不再执行动画，只有模板的实际 ContentPresenter 执行一次。EntryTypePicker 的 ToggleButton 随之使用同一策略。
- 图标按钮只保留 ButtonSurface Border 的动画，空的 ContentPresenter 从通用规则中排除。
- 删除宽泛 Panel/TextBlock 过渡，输入和菜单继续由其背景 Border 负责反馈。
- 图标 ToggleButton 明确 checked → checked-hover → checked-pressed 的目标色，pressed 优先；禁用时 hover/pressed 不覆盖原选中底色，保持禁用透明度。

## 验证方式

`UiuxMotionUsesOneSurfaceAndRespondsOnTheFirstFrames` 在实际挂载的 Light、Dark、Violet、Aurora 控件上驱动已安装 Avalonia 框架的动画时钟；检查首 32ms 颜色变化、快速反向从当前颜色连续返回、一个过渡时长内到达 checked 目标，以及选中/hover/pressed/disabled 优先级。参考 TextBox 和 MenuItem 的实际 Border 使用同一时长、零延迟和 LinearEasing。既有防闪白插值与四主题小窗口挂载回归继续保留。

测试颜色须纳入 Brush.Opacity，并按实际 ThemeVariant 读取目标资源；裸 Color.A 或无主题参数的资源查找不能代表最终绘制颜色。最初的两次断言问题分别由这两项测量错误导致，均修正测试测量后验证实际动画。

确定性时钟用于验证状态与中间帧连续性，不将其当作真实桌面 GPU/帧率测量。

最终 Editor 全量回归 272 项通过、0 失败（`artifacts-uiux-motion-full.log`、`test/GalNet.Editor.Tests/TestResults/uiux-motion.trx`，2m29s），测试命令同时构建 Editor 与 XAML。动画时钟、防闪白及真实小窗口挂载定向回归 3 项通过（`artifacts-uiux-motion.log`）。新增两个 C# 文件的只读空白和 IDE0005 检查均 exit 0（`artifacts-uiux-motion-verify-whitespace.log`、`artifacts-uiux-motion-verify-style.log`）；`git diff --check` 通过。

本轮未修改其他并行 feature，未自动提交工作区中的后续修复。

## 后续废止

2026-10-24 用户实测发现，统一过渡仍会令 Group entry 选择出现卡顿，而顶部菜单及 Ursa 默认控件正常。已删除 `ThemeTransitions`、`LinearBrushTransition`、`EditorMotion` 和图标按钮模板的专用画刷过渡，改回 Ursa/Semi 的普通模板反馈；这里的中间帧测试及其前提不再适用。
