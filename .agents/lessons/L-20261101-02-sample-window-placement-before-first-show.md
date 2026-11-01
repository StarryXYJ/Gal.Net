# Sample 桌面窗口尺寸需在首次显示前读取

状态：candidate
日期：2026-11-01

## 现象

CrossPlatform Sample 桌面窗口先按 XAML 默认尺寸显示，随后才切到 profile 中保存的窗口尺寸。

## 根因

桌面入口在启动 Avalonia lifetime 前已知 profile 路径，但窗口记录原先等宿主视图挂载、异步准备启动路径后才读取。此时 `DesktopWindowPlacement` 看到窗口已可见，会立刻恢复尺寸，形成首次显示后的尺寸变化。

## 正确做法

- 桌面入口在启动 lifetime 前提供 `window-placement.json` 路径。
- 在赋值 `MainWindow` 前创建 placement owner，并在窗口尚未显示时应用有效的保存宽高。
- `Opened` 时仍按屏幕工作区校正过大的尺寸；已最大化的窗口恢复时不要先切回普通状态。
- 移动端没有桌面 profile 窗口记录，不接入此路径。

## 证据与边界

本次修改位于 `App.axaml.cs`、桌面 `Program.cs` 和 `DesktopWindowPlacement.cs`。Sample Release 构建通过；尚未做桌面运行时首帧观测，因此保持 candidate，不能把构建结果当作可视行为验收。

关联说明：[Sample 桌面窗口手册](../../docs/manual/sample/gameplay-and-settings.md)。
