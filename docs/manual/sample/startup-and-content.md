# Sample 启动与内容路径

## 选择示例工程

| 工程 | 职责 |
| --- | --- |
| GalNet.Sample.Avalonia | 桌面入口，项目启动策略及可选开发调试工具 |
| GalNet.Sample.Player | 迁移期间共用的项目配置/资源与契约适配；通用 UI/会话和内容安装已归库层，不是独立可执行程序 |
| GalNet.Sample.CrossPlatform | Desktop/Android/iOS/Browser 平台宿主集合，实际验证范围见平台页 |
| GalNet.Sample.Headless | 不含 Avalonia UI 的终端示例与引擎自检入口 |
| GalNet.Sample.Contracts | 工具和玩家共享的项目作者契约 |

## 桌面加载规则

没有内容参数时，桌面宿主从应用根的 `pak/` 加载，不依赖启动工作目录。发行程序的托管依赖和运行时在 `lib/`；保留完整应用布局。

玩家版默认关闭内容路径参数和外部调试。Debug/Developer 版根据 `EnableLaunchArguments` 与 `EnableGameDebugging` 编译开关接受路径、`--profile` 等参数；重新 restore 后再切换依赖图。源码示例脚本显式传入内容路径和开发 profile，见[快速开始](../../quick-start/run-sample.md)。

默认桌面玩家 profile 为用户 LocalApplicationData 下的 `GalNet/<摘要>/`。已有内容以 projectId 派生摘要，安装路径和 buildId 改变不会换身份；缺少有效描述的开发目录按绝对路径派生。日志在 profile 的 `logs/`；旧 profile 不自动迁移或删除。

CrossPlatform 通过 `PrepareLaunchAsync` 交给平台确定内容与可写目录。Android 使用私有目录安装包内 `game.galpak` 并保存 profile；iOS 具有对应接入代码，实机状态仍须单独验收。浏览器不能直接套用桌面文件系统假设。

缺失或损坏内容应通过启动界面或日志定位。不能通过替换清单 hash、混用旧契约或把散文件丢进 PAK 目录绕过正式加载检查。

后续阅读：[平台支持](platform-support.md)、[玩家操作与设置](gameplay-and-settings.md)、[定制宿主](../extensions/customize-sample.md)。
