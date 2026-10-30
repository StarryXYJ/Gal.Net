# 窗口装饰颜色与播放器内容样式的边界

状态：confirmed
日期：2026-10-29
来源：Sample UI 调整（独立修复，无 feature 记录）

- 现象：播放器内容使用 Mono 浅色，系统/宿主主题可能仍是深色；标题栏关闭按钮颜色与背景混在一起。为 UrsaWindow 内容模板下的 Button 写选择器不能命中独立装饰层。
- 原因：Ursa 2.1/Avalonia 12 的关闭按钮属于 WindowDrawnDecorations，不是 UrsaWindow 内容模板的后代。当前 Headless 平台不会生成平台窗口装饰，GetVisualDescendants 只返回内容控件，不能用“找不到关闭按钮”推断原生 UI 没有按钮。
- 正确做法：在窗口资源层将 CaptionButtonForeground、关闭按钮悬停/按下颜色、TitleBarBackground 等真实装饰资源映射到玩家调色板。完整播放器 Fluent 样式仍放在播放器根；窗口只共享调色板，避免改变宿主窗口控件主题。
- 验证：SampleWindowThemeTests 检查实际装饰资源与玩家前景色一致，并验证退出遮罩的取消焦点、Escape 和结果。窗口装饰的像素与系统交互仍需原生桌面验证；Headless 资源断言不替代原生截图。
- 禁止：把 Window 内容树中的 Button 选择器当成所有窗口装饰按钮的覆盖；为了取到标题栏控件而给整个 Window 挂上播放器 FluentTheme。
- 依据：Ursa.Avalonia v2.1.0 的 UrsaWindow.axaml（独立 KEY_URSAWINDOW_DRAWN_DECORATIONS）；本次 artifacts/sample-ui-tests/sample-window.trx 与 artifacts/sample-ui-captures/Sample-ExitConfirmation.png。
- 正式文档：docs/spec/presentation/game-view.md、docs/manual/extensions/customize-sample.md。

## 2026-10-30：调试菜单与装饰层重叠

- 已复现：CrossPlatform 改成 UrsaWindow 并映射相同 Caption 资源后，仍出现深色整行和近乎不可见的按钮。根因是桌面复用了移动 DebugToolsOverlay，其全宽 Menu 内容行盖住装饰层下方；Headless 的 Caption 画刷断言可以全通过，原生窗口外观仍错误。
- 桌面与原 Sample 一样把 DebugMenuView 挂到 UrsaWindow.LeftContent，由窗口提供 OverlayDialogHost；移动继续使用 UrsaView 和 DebugToolsOverlay。菜单背景显式使用玩家 Background，不能假定宿主 Semi 的系统主题与玩家配色相同。
- 开发版内容加载失败不能顺带隐藏日志入口；菜单可见性与游戏是否就绪分开，清空数据/存档入口单独等待 IsGameReady。CrossPlatform 重建 scope 前关闭日志窗口、移除菜单并退订旧模型。
- 验证：CrossPlatformWindowChromeTests 检查明暗宿主的画刷、Ursa 标题栏 hit-test role、窄 LeftContent、失败后的日志与重绑；Windows 原生截图确认黑条消失及按钮可见，最大化状态切换通过。具体证据和未验收边界见跨平台 feature 的 follow-up-window-chrome.md。
