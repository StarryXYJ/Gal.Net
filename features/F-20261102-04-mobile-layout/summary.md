# 跨平台自适应布局：实现总结

日期：2026-11-02。用户最初要求移动适配，随后明确要求桌面也共用自适应；初版桌面专用固定外层画布和整页像素一致门禁已被新方向替代。

## 当前行为

- 所有平台将同一个 GameShell 挂入实际内容视口，保留启动可见性绑定，解绑清理订阅并恢复局部资源；不复制页面、导航、VM 或 presenter。
- 移除 IsMobile 布局标记。PlayerLayout.IsCompact(Size) 在宽 <1000 或高 <600 逻辑单位时启用 compact-player，放宽恢复标题列定义、页边距、页头和内容间距。桌面与移动端字号默认继续沿用 PlayerTypography。
- scene/dialogue/NVL/choice 保留内部 1920×1080 画布，历史为全页覆盖层。对白只补足工具栏必要净空，保留横向构图和字号；可见工具栏按实际高度补足净空，隐藏时恢复原 70 设计单位底边距。
- 工具栏所有平台按实际按钮宽度收纳，窄时快进/自动/历史/更多，更窄时历史收入更多；放宽关闭菜单并恢复完整工具栏。最小命中宽 48、高 32，文字 12–18、菜单 14–18；背景透明、浅色文字加轻微黑影，hover 主题色加下划线、pressed 独立颜色。工具栏锚定场景下沿，对白净空约 2，竖向黑边不再拉开间距。
- 工具栏使用单行 StackPanel，由唯一的宽度测量决定收纳，避免 WrapPanel 在字号资源更新后临时换行；操作字号由 GamePage 直接拥有，避免资源在挂载后覆盖已测值。
- 设置/绑定保留原编辑器及输入草稿重排，长标签可换行，宽排标签列 220、行底间距 16，分类可换行；存档按 240 列宽计算列数，上限跟随 SaveColumns（默认 4），Gallery 按宽度填列并保持 260:190 比例。
- 更多直接绑定原命令，返回先关闭，切页/卸载清理；Flyout 打开期间阻断剧情设备动作。触摸 UI/滑块清理等待双击判定的单击，避免延迟推进。
- 场景 PNG 仍为 1920×1080、存档预览仍为 480×270；含 UI 截图在所有平台均使用实际内容视口逻辑尺寸。截图弹窗保留自身窗口高度/预览限制，不套普通页面通用触摸高度。安全区不重复扣除。

## 整体 UI/UX 复查

用户继续指出 hover、间距、弹窗按钮和桌面密度问题后，对照 [Ren’Py GUI](https://www.renpy.org/doc/html/gui.html) 与 [Windows 窗口断点](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design) 检查全部玩家页面。外部字号按逻辑视口在 0.75–1 内折算，DPI 不重复计算；Dialogue/Speaker/SceneBody 保留设计字号。普通按钮 36、紧凑页 40，统一项目按钮模板与主次操作；退出/返回/跳过确认及截图均检查。合并重复尺寸资源，压缩页面/设置间距；标题背景装饰保持原设计比例缩放。细目见 [UI 审查](ui-audit.md)。

## 用户反馈与历史验证

首版工具字号和整条背景过大；后续紧贴文字的底色、48 高透明目标和额外留白也被用户否定。最终移除全部文字底色、收紧为 32 高、去掉额外底边距。此后用户要求桌面也适配，统一跨平台机制。

初版证据：相关 Integration 129 passed，Presentation 255 passed /263 total（8 Explicit 未运行）；工具栏修正专项 21 passed。初版桌面样本 PNG 与原基线逐字节相同，这是当时的结果，不再代表当前跨平台自适应验收。证据保留于 output/mobile-layout/。

## 最新验证与限制

跨平台初版相关 Integration 143 passed /0 failed。全部修正后的整体 UI/UX 跟进回归 173 passed /0 failed（output/ui-audit/complete/），固定画布字号补充 13 passed /0 failed；标题背景比例修正后视觉/页面专项 46 passed /0 failed（各次用例重叠，不相加）。截图、日志和 TRX 在 output/ui-audit/，过程见 execution.md。Headless 视口测试不等同原生设备旋转、安全区或手指操作验收。本次统一布局改动未重新导出 Android APK；之前 output/android-mobile-20261102/ 为统一布局改动前的包。

用户允许共享工作区并分开提交。本项未修改演出库或 GameTestCase；PlayerTypography 原有工作区调整也不在本项提交中。

当前规范见 [游戏页面](../../docs/spec/presentation/game-view.md#自适应页面与工具栏)，操作见 [玩家手册](../../docs/manual/sample/gameplay-and-settings.md#自适应布局)，定制见 [扩展手册](../../docs/manual/extensions/pages-and-themes.md#定制自适应布局)。[工具栏经验](../../.agents/lessons/L-20261102-05-touch-target-visual-size.md) 保留用户反馈及修正来源。
