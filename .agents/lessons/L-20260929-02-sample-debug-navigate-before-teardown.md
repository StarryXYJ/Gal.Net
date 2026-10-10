# Sample 调试重置/重载应先导航再释放游戏展示

状态：superseded
来源：2026-09-29 用户在 Sample Avalonia 调试清空流程中报告的黑屏

## 现象

从游戏页执行“清空游戏数据”时，若先停止引擎并释放游戏展示对象，主窗口可能在标题页完成导航前显示黑屏。

## 已确认事实

`SampleGameSessionService.DisposeEngineAsync` 会重置游戏场景展示；`IGameNavigationService.ResetToAsync<TitlePageViewModel>` 可以在游戏引擎仍存活时先完成无动画标题页导航。

## 原假设

曾假设 Sample 的顶层调试操作应先 `ResetToAsync<TitlePageViewModel>(NavigationTransition.None)` 并等待页面切换完成，再在会话生命周期锁内停止运行、销毁引擎或重新加载资源。

## 失效原因

用户确认采用该顺序后，清空与进程内资源重载仍会黑屏。该顺序不足以保证当前页面主机与 Runner 销毁之间的稳定呈现；后续调试操作改用受控进程重启，见 `L-20260929-09`。

## 适用范围

`GalNet.Sample.Avalonia` 中会销毁当前引擎或场景展示的调试重置、资源重载和未来会话切换入口。

## 不要做什么

不要从游戏页直接先调用 `DisposeEngineAsync`，再依赖异步、未等待的导航修复画面；也不要让会话服务自行耦合页面导航。

## 证据和关联文档

- `src/GalNet.Sample.Avalonia/Services/SampleDebugServiceCollectionExtensions.cs`
- `src/GalNet.Sample.Avalonia/Services/SampleGameSessionService.cs`
- `src/GalNet.Avalonia.GameView/Page/GameShell.axaml.cs`
