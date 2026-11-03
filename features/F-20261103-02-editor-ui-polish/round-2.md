# 第二轮：设置窗口与快捷键（进行中）

2026-11-03。P1 局部实现，尚未完成真实桌面和所有退出条件。

- 编辑器设置增加常规/快捷键分页，两个设置窗口默认尺寸调整为 800×600；标题栏 Ursa 按钮和 Dock 绘制按钮增加透明常态样式，实际平台覆盖待验证。
- 快捷键点击捕获，Esc 取消、Backspace 清除；按键成功立即保存。每项图标重置，全部重置先显示确认弹窗；冲突涉及 Global/局部同时生效的上下文，单项重置也检查冲突。持久化异常时保留旧内存绑定和覆盖值。
- `dotnet test ... --filter 'FullyQualifiedName~EntryTypeShortcutTests|FullyQualifiedName~EditorFontSettingUpdatesMountedControlsAndRestoresFromSettings|FullyQualifiedName~OpenFoundationViewsHistoryErrorsAndDialogRefreshAcrossFourLanguagesWithoutChangingContent'`：5/5 通过，包含新加保存失败回滚测试。曾因运行中的 Editor 锁定 exe 失败，用户关闭后同一默认目录重跑通过。
- 未完成：捕获控件实际键盘/指针生命周期、确认弹窗交互、窗口小屏限制、全部主题/原生窗口及真实桌面视觉验证；P1 保持 in-progress。
