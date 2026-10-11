# 粒子耗尽清理不能在渲染调用栈内同步修改场景集合

状态：confirmed（通过 presenter 的延后清理与同 ID 替换回归测试验证）

来源 Feature：F-20261010-02-runtime-i18n 验收期间发现；问题位于既有粒子展示链。

## 现象与发现方式

用户报告游戏结尾附近崩溃。`GameTestCase/.galnet/logs/game-20261010.log` 的 2026-10-10 21:59:32 致命异常为 `Visual was invalidated during the render pass`。该次运行没有记录 Engine Completed；栈明确来自粒子耗尽后的场景集合移除，不是 i18n 解析或 Engine 终态释放。

## 根因

`SceneLayerHost.Render` 同步调用 `ParticleEmitter.Update`；耗尽时触发 `Drained`。`AvaloniaParticlePresenter.OnParticleDrained` 注释称移除被延后，但调用的 `IAvaloniaUiDispatcher.Dispatch` 在 UI 线程立即执行。因此 `StopParticleEmitter` 在 Render 内移除 `SceneRenderables`，集合通知触发 `RebuildPlan` 与 `InvalidateVisual`，Avalonia 抛出异常。

## 正确做法

为渲染回调的清理使用保证异步排队的调度，待本次 Render 返回后再修改场景集合。不要只判断是否处于 UI 线程；UI 线程不代表当前位于安全的渲染阶段。排队清理还应核对 emitter 实例身份，避免替换同 ID 后误删新实例，并覆盖 presenter 释放后的回调。`IAvaloniaUiDispatcher.Post` 明确提供该保证；`Dispatch` 仍可在 UI 线程同步执行，不能用于渲染期清理。

## 适用范围与禁止做法

适用于帧更新中触发的 drained/finished 回调及渲染内的集合或属性变更。不要把“Dispatch 到 UI 线程”视为“延迟执行”；不要仅吞掉 InvalidateVisual 异常而继续在枚举中修改集合。Headless 剧情测试和 Avalonia 启动验证不覆盖真实粒子耗尽的 GUI 帧路径。

## 关联位置

- `src/Presentation/GalNet.Avalonia.Rendering/Scene/SceneLayerHost.cs`
- `src/Presentation/GalNet.Avalonia.Rendering/Scene/ParticleEmitter.cs`
- `src/Presentation/GalNet.Avalonia.GameView/Presentation/AvaloniaParticlePresenter.cs`
- `src/Presentation/GalNet.Avalonia.GameView/Presentation/IAvaloniaUiDispatcher.cs`
