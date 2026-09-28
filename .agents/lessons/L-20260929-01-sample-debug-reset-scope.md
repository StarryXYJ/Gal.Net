# Sample 调试清空应重置玩家状态，而非日志

状态：confirmed
来源：2026-09-29 用户确认的 Sample Avalonia debug 工具需求

## 现象

“清空”与日志面板同时出现时，容易将其理解为清空日志；但玩家期望它用于获得全新的游戏档案。

## 根因

日志缓冲和玩家持久状态是不同关注点：前者只属于调试可视化，后者包含存档、持久玩家变量和阅读进度。

## 正确做法

将日志清空留在日志面板内。Sample 的顶层调试菜单中的“清空游戏数据”必须先由 Ursa 确认，再停止当前流程、清除存档与阅读进度、把玩家变量还原为配置默认值，并返回标题页。

## 适用范围

GalNet.Sample.Avalonia 的可选调试工具及其未来扩展。

## 不要做什么

不要让主窗口的清空入口只清除内存日志，也不要让它绕过确认框直接删除玩家数据。

## 证据和关联文档

- `src/GalNet.Sample.Avalonia/Views/MainWindow.axaml.cs`
- `src/GalNet.Sample.Avalonia/Services/SampleGameSessionService.cs`
