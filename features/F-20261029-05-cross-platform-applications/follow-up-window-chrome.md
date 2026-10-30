# 两个 Sample 的 Ursa 桌面窗口对齐

- 日期：2026-10-30。
- 范围：用户要求 CrossPlatform 参考 Avalonia Sample，统一使用 Ursa 的窗口按钮、拖动方案和样式；窗口设计留在项目内，开发者可自行替换，不新增共享窗口基类。
- 本记录只覆盖窗口对齐和启动失败诊断，不改变 feature 整体的 in-progress 状态。

## 实现与原因

最初提交 `2c7a520e` 只设置 `WindowDecorations=None`，不能满足三按钮和拖动要求，已由本次 UrsaWindow 接线取代。仅换成 UrsaWindow 并复制 Caption 资源仍不足：CrossPlatform 桌面复用了移动端 DebugToolsOverlay，其全宽菜单占据内容顶部，系统深色 Semi 背景与玩家浅色按钮叠加，产生黑条和低对比度按钮。用户截图与原生窗口均复现此差异，早先的 Headless 画刷断言没有捕获它。

- CrossPlatform 的 XAML 根和代码基类统一为 UrsaWindow，Caption/TitleBar/WindowBackground 资源与原 Sample 一致；调色板在窗口构造时安装。
- 桌面使用窗口自带的 Ursa OverlayDialogHost 和窄 LeftContent 调试菜单，不再嵌套移动调试容器或插入全宽菜单行。日志和确认沿用已有 Ursa 日志窗口/MessageBox；移动继续使用 UrsaView 与 DebugToolsOverlay。
- 两边调试菜单背景显式跟随玩家配色，不从系统主题推断。启动前及失败后继续显示日志入口，清空数据/调试存档由 IsGameReady 单独控制。
- CrossPlatform 在 scope 重建和关闭前退订模型、关闭旧日志窗口并拆下菜单；清空数据失败时不提前拆下当前诊断 UI。
- 窗口接线与定制仍分别归两个 Sample，通用调试模块只补菜单配色及游戏就绪状态，不拥有窗口样式策略。

## 验证

| 范围 | 结果与证据 |
| --- | --- |
| Debug Integration | 9 项通过：CrossPlatformWindowChromeTests、SampleWindowThemeTests、SampleCustomizationTests。明暗主题下检查 Caption 资源、Ursa 装饰配置、标题栏 hit-test role；两个桌面 Sample 均检查窄 LeftContent、失败后日志、就绪菜单状态及 CrossPlatform 重绑清理。TRX：`artifacts/window-chrome-tests/results/window-chrome.trx` |
| Release 玩家 Integration | 同一筛选关闭 EnableGameDebugging 后 6 项通过；TRX：`artifacts/window-chrome-tests/results/window-chrome-player.trx` |
| 移动调试容器 | GameDebugToolsTests 4 项通过，包含内嵌日志、确认、返回处理、解绑与外部协议适配；日志：`artifacts/window-chrome-tests/presentation.log` |
| Desktop 构建 | Debug 隔离输出和 Release 玩家版 locked build 均成功；未全局关闭既有编译/分析器警告 |
| 玩家载荷 | CrossPlatform Desktop 的 deps.json 无 GalNet.Debugging、GalNet.Avalonia.Debugging、AvaloniaUI.DiagnosticsSupport |
| Windows 原生 CrossPlatform | 隔离输出加载 GameTestCase/Output、独立临时 profile；截图确认黑条消失、三按钮与浅色顶区对齐原 Sample，实际点击最大化后切换为还原状态 |
| Windows 原生 Avalonia Sample | 故意使用不存在的内容目录；错误页面仍显示 Debug，菜单可打开包含 DirectoryNotFoundException 的日志窗口，清空/存档禁用。通过退出确认结束，正常释放并返回 0 |

首轮构建中的窗口链式赋值类型错误和 NUnit 惰性枚举计数断言已修正，随后重跑通过；没有跳过失败测试。

原生完整拖动/还原/最小化/边缘缩放/全屏/多屏矩阵未完成；本次只验证标准 Ursa 标题栏 role 和上述 Windows 切片。Linux/macOS 原生窗口、Android/iOS 设备未在本次重新验证，不把 Headless 或库配置检查当成跨平台交互已通过。日志打开的原生证据采用菜单路径，不将快捷键作为成功证据。

正式操作说明已同步 CrossPlatform README 与 `docs/manual/extensions/customize-sample.md`；复用经验补充到现有窗口装饰 lesson，不覆盖其他进行中的阶段记录。
