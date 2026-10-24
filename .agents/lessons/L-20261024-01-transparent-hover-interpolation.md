# 暗色 hover 的透明颜色插值

状态：confirmed
日期：2026-10-24
来源：features/F-20261023-01-editor-uiux

现象：按钮从透明背景 hover 到暗色填充时，先出现浅亮色，再落回目标色。降低目标亮度或关闭动画都会掩盖真正原因。

根因与复现：Avalonia 12.0.5 的 ColorAnimator 将 Transparent（#00FFFFFF）的隐藏白色 RGB 参与插值；到 #FF313244 的 25% / 50% 为 #40e2e2e3 / #80bebec0，叠在暗背景上的中间结果亮于两个端点。Editor 的全局 Border/文字过渡使该行为扩散到模板子控件。

正确做法：solid brush 的颜色和透明度以预乘 alpha 插值，再转换为输出 brush；保留 160ms 线性过渡及中途反向。检查合成后颜色是否位于端点之间，并验证正常图标按钮透明、hover、按下、焦点和 checked 状态。原生控件自带的调色板需要与 Editor 语义色明确绑定；普通按钮的背景样式必须排除 icon theme。

证据：EditorUiuxTests.TransparentHoverDoesNotCreateALightIntermediateSurface 复现框架中间色并验证修复；EditorUiuxIntegrationTests 的真实模板挂载与四主题截图检查。Headless 没有持续桌面帧时钟，稳定状态截图可禁用实例过渡后生成，但必须独立测试真正的插值与反向，不能用截图取代动画验证。

适用范围：暗色 UI 的透明 solid brush 过渡和模板子控件；不推广为所有动画必须关闭。禁止仅改 hover 目标色或只比较最终帧就宣称修复。