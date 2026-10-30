# 默认界面字体不能要求目标系统安装特定 CJK 字体

状态：confirmed
来源 Feature：F-20261029-05-cross-platform-applications

## 现象与证据

Ubuntu 24.04.3 / WSLg 的 Sample 已创建带标题的原生窗口，随后在 TextBlock 初次布局时抛出 Could not create glyphTypeface，所报 family 为 Noto Sans CJK SC 的复合字体。进程 exit 1，不能把“弹出过窗口”视为会话就绪，也不能把这次退出描述为没有创建窗口。

本机 fc-match sans-serif 返回 DejaVu Sans，但原先指定的 CJK 字体与当时 Avalonia 默认回退组合仍无法创建界面字形。没有证据将其归因于微软雅黑、VLC、手柄或先前另一设备的 DeveloperTools 阻塞。

## 正确做法

共享 UI 使用 FontFamily.Default，CJK 名称仅作为字符回退。宿主选择已部署的默认字体；官方 Sample 已包含 Avalonia.Fonts.Inter，并通过 WithInterFont 注册，显式使用 fonts:Inter#Inter。不假设注册字体集合会自动改变默认字体。

相同 Ubuntu、内容与依赖，仅更新字体策略后，真实 Sample 窗口、Session initialized 和确认退出 exit 0 全部通过。Inter 不含中文，中文显示仍依赖系统或应用部署的 CJK 字体；不能以启动通过冒充中文渲染验收。

## 验证与来源

- out/cross-platform-p3b-current/smoke/7906174bbbb84a6282889ee268159d09/：修复前窗口与字形异常。
- out/cross-platform-p3b-current/smoke/c889249d038f46f0b57a4cd9113c3e23/：修复后会话、正常退出。
- out/cross-platform-p3b-current/results/typography-native.trx：两项字体回归通过；沙箱内 testhost 无响应，在支持原生字体/管道的环境重跑。
- Avalonia 12.0.5 的 WithInterFont 只注册集合：https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Fonts.Inter/AppBuilderExtension.cs 。

禁止仅凭窗口存在、headless 选项断言或系统 fc-match 结果宣称原生启动成功；同时检查会话日志和正常退出。
