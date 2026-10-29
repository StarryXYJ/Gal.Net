# Avalonia 生命周期结束后重申 benchmark 失败退出码

状态：confirmed
来源：features/F-20261025-01-cpu-startup-benchmark

现象：Sample 初始化失败时 trace 正确记录 Startup.Failed，Complete 中也设置 Environment.ExitCode=1，但关闭窗口后子进程仍返回 0。脚本通过检查 Ready 缺失才拒绝了该运行。
修正：StartupBenchmark 保留 Failed 状态，宿主在 StartWithClassicDesktopLifetime 返回后的 finally 中重申退出码。无效游戏内容的最终集成检查确认进程返回 1，失败 trace 保留，脚本不生成成功摘要。
规则：不要依赖窗口仍运行时设置的 Environment.ExitCode；桌面宿主可能在生命周期结束时覆盖它。脚本同时验证退出码和协议完成标记，不能仅以其中之一判断成功。
