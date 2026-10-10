# Sample 调试重置应避免进程内 Runner 销毁

状态：candidate
来源：2026-09-29 用户确认清空与进程内资源重载在调整导航顺序后仍黑屏

## 现象

Sample 的 `SampleGameRunner.DisposeAsync` 会重置场景展示。清空或重载时，即使先导航标题页，用户仍观察到黑屏。

## 已确认事实

清空玩家状态的磁盘操作由 `SampleSaveSession.ClearAsync` 完成；它会清空存档、重置并持久化玩家变量、删除阅读进度以及重建默认游戏设置。此操作不需要销毁正在显示的 Runner 才能完成。

## 当前做法

调试清空先停止 Runner、完成 `SampleSaveSession.ClearAsync`，但不在当前窗口中销毁 Runner；随后以当前进程路径和原始命令行参数启动替代进程，并通过 Avalonia 的正常关闭流程释放旧进程资源。调试“重新加载游戏资源”也采用重启而非进程内重载。

## 适用范围

`GalNet.Sample.Avalonia` 的开发期调试菜单。不是通用游戏内资源热重载方案。

## 不要做什么

在未隔离并复现页面主机生命周期问题前，不要将此调试入口改回进程内 `SampleGameRunner.DisposeAsync` 加资源重建。

## 证据和关联文档

- `src/Samples/GalNet.Sample.Avalonia/Services/SampleGameRunner.cs`
- `src/Samples/GalNet.Sample.Avalonia/Services/SampleSaveSession.cs`
- `src/Samples/GalNet.Sample.Avalonia/Services/SampleApplicationRestarter.cs`
