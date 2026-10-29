# Timeline 停止必须同时完成属性轨道和事件收尾

状态：confirmed
来源：features/F-20261027-02-sustained-visual-presets

## 现象与根因

animation.stop / CompleteImmediately 原先只通知 presenter 跳到末帧并移除播放状态。AvaloniaAnimationPresenter 的提前完成分支仅采样轨道，不调用 beforeFrame，因此末帧 effect.stop/layer.hide 没有送达展示层。集中线虽已透明，Effect 与载体图层仍可能存在。定时和持续预设的提前停止回归均覆盖了此路径。

## 正确做法

由中立 Timeline 实例在正常完成/主动完成后补发剩余非 burst 事件，并让停止 Entry 等待 PresentationCompletion。循环的初始化、迭代事件、播放结束清理需区分；不能把初始化和清理跟随每次循环重放。首帧初始化进行中收到停止请求时，必须在启动 presenter 前再次检查停止状态。

点击跳过和脚本停止可以交错。不能在 presenter 任务结束时提前移除播放身份，让随后到达的停止指令绕过仍在执行的 skip 清理；使用稳定的完成信号，等呈现退出和事件清理全部结束后一起释放身份。Stop_during_click_skip_waits_for_the_same_cleanup 在修复前得到 stopping.IsCompleted=true，修复后必须在 Effect 清理门释放前保持 false。

循环 presenter 单独返回 Completed 不是提交末值的充分证据；没有明确停止请求时不能改变恢复后的循环初始值。scope 取消、CatchUp 分离、被替换不能借用正常完成的清理路径。

## 验证

SustainedVisualPresetTests 验证提前停止的 Effect/图层清理、异步初始化交错、清理等待、恢复、循环与同标识重用；既有 PersistentPresentationReplayRestartsEffectsAndParticlesWithSavedAnimationValues 验证宿主直接结束呈现不篡改恢复初值。
