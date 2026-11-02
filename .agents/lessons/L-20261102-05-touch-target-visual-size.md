---
status: confirmed
date: 2026-11-02
feature: F-20261102-04-mobile-layout
---

# 移动工具栏的命中大小不等于视觉大小

> 2026-11-02 后续：F-20261102-06-player-layout-polish 已替代本文中的普通页面连续字号折算、统一 compact 控件高度及旧断点；现在普通正文保持逻辑字号，容器按空间调整。本文的工具栏、资源优先级和实际命中验证规则仍有效。

现象：独立工具栏移出固定画布后，直接使用 24/28 的设计字号，并在 320×56 的命中区绘制整块面色，按钮比对白显著更大。用户明确指出“这按钮也太大了吧”。

根因：脱离 Viewbox 的交互层把设计空间字号当作实际视口字号，又让容器绘制填满整个触摸区；只验证 48×48 命中与无溢出，不能验证构图和视觉密度。

后续反馈：用户继续否定紧贴文字的小底色，并指出对白与底部间距过大。透明的 48 高命中区也会撑开实际布局，不能只检查背景透明。

正确做法：字号先按画布基准折算，再限制可读范围；遵循用户的紧凑构图要求，工具栏使用最小宽 48、高 32 的透明命中区，普通页面控件仍保持 48 高。文字无底色，用浅色和轻微黑影保持可读，状态通过前景色和字重反馈；取消额外底部边距，测量对白与按钮实际净空。不要机械套用 48×48。

验证：初版 MobileLayoutTests（现为 ResponsiveLayoutTests） 同时断言按钮命中宽至少 48、高 32–36、可见内容高度不超过 30、透明区与可见内容有高度差、文字和工具栏背景透明、贴底及横屏对白净空 3–5；桌面 PNG/几何与原版一致。修正后 21 项布局/播放交互回归通过。截图见 output/mobile-layout/plain/（不提交）。

范围：固定设计画布之外的触摸交互层。不借此修改玩家已有字号配置或整体放大其他 UI。规范见 docs/spec/presentation/game-view.md 的自适应页面与工具栏。后续用户要求桌面共用自适应，桌面整页像素一致不再作为门禁，固定游戏场景构图仍保持。

后续跨平台测量陷阱：按钮上的 SetCurrentValue(FontSize) 可在挂载/资源更新后被 DynamicResource 覆盖，先测得的总宽失效，WrapPanel 临时换行会把真实点击位置移开。菜单与工具按钮的视口字号由 GamePage 直接赋值拥有；固定样式类和 margin 在 XAML 提前声明；完整工具栏使用单行 StackPanel，由已有宽度测量唯一决定收纳。验证必须包含真实鼠标按下/抬起导航与隐藏 UI，不能仅检查命令绑定或改用命令直调。143 项相关 Integration 回归通过，见 output/responsive-layout/regression/。

整体 UI 复查后的当前规则：外部字号必须折算实际逻辑视口，不能直接套设计字号；Avalonia 已处理 DPI，不重复乘屏幕缩放。普通按钮 36、紧凑页 40，而工具栏保持 32。hover 不能与 idle 同色，使用主题色和预留的透明下边线反馈；对白至工具栏区域约 2，工具栏锚定场景下沿，避免竖向黑边拉开距离。旧 4 净空、始终贴视口底部和普通页面 48 高已被这次反馈替代。

复现到的资源陷阱：Sample CreateStyles 将 Dimensions.axaml 合并在外层 Styles.Resources，内层 Player.axaml 重复定义 PageMargin/PanelPadding 等键不会成为最终值。只改内层会导致代码看似收紧、截图仍保留原间距。当前已合并为 Dimensions 单一来源；验证需读取实际控件 Bounds 和最终截图，不能只看 XAML diff。外部缩放还必须排除 Dialogue/Speaker/SceneBody，避免固定画布内二次缩放；ChoiceList 显式绑定 SceneBodyFontSize。

设置空白的另一实际来源：IsVisible 绑定 Context.Issue.Message，Issue 为 null 时会返回 UnsetValue，StringConverters.IsNotNullOrEmpty 并不一定被调用，TextBlock 可能保留默认可见并占一行。当前 SettingsCategories 明确设置 FallbackValue=False，无错误的语言行 <=44 高有实际测量断言。不能仅凭 Text=null 推断控件不占空间。
