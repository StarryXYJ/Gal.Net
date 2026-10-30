# 模态遮罩下的静态标题前景色

- 状态：confirmed
- 日期：2026-10-29
- 来源：用户反馈退出确认出现后主页标题消失；独立 Sample UI 修复。
- 现象：退出确认将 PlayerContent.IsEnabled 设为 false。主页 GameTitle 没有显式 Foreground，继承内容容器的禁用文字色，再叠加遮罩后标题显著变淡；标题字符串与可见性没有改变。
- 根因：容器禁用会改变后代继承的前景画刷。之前的弹窗截图没有执行主页导航或 CompleteStartup，背景为空，未覆盖实际主页。
- 做法：Classic/Minimal/Paper 静态标题显式绑定 TextPrimary；保留背景禁用及输入保护。弹窗测试挂载真实主页，比较打开前后标题画刷的 Color 与 Opacity，不能只检查标题字符串或 IsVisible。
- 验证：修复前实际挂载回归失败（标题颜色由 #FF181D25 变为 #FF1C1F23，截图显著变淡）；修复后 SampleWindowThemeTests 通过，三布局×三配色的 9 项挂载测试通过。前后截图在 out/exit-title/。
- 边界：静态标题保留正常色；可操作控件的禁用反馈不在此规则范围。不要为保留标题而解除背景输入禁用，也不要用空背景截图证明主页仍可见。
- 正式文档：docs/spec/presentation/game-view.md、docs/manual/extensions/customize-sample.md。
