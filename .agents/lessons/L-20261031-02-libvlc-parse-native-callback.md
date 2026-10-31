# LibVLC Parse 的完成回调不能直接释放 Media

状态：confirmed
来源：features/F-20261031-01-resource-gallery/summary.md

现象：鉴赏读取 MP3 时长时，`using Media` 配合 `await media.Parse(...).ConfigureAwait(false)` 可稳定让 testhost 在准备时长阶段以原生 0xc0000005 崩溃。原生音频测试阶段日志确认尚未进入播放/切歌。

根因：LibVLCSharp 3.10.0 Parse 内部使用默认 TaskCompletionSource，完成后续代码可以在原生 ParsedChanged 回调内联执行；后续 using 立即释放当前仍在分派事件的 Media。反射 IL 核对其完成源构造未指定 RunContinuationsAsynchronously。不能把这个现象当作普通托管异常捕获。

正确做法：在 Parse 结果与媒体读取/释放之间加 RunContinuationsAsynchronously 完成源，完整转发成功、异常及取消，使 Media 的释放离开原生回调。解析帮助类用自己的原生生命周期和异步 gate，取消及 Dispose 等待不依赖 UI 调度的底层解析退出后再释放 LibVLC。

验证：保持音频切换实现不变，仅加入异步完成转发后，两个样例音频均完成解析、拖动起播、互斥切换、恢复位置及退出测试。首轮崩溃日志、转储与修正后测试在 out/gallery-audio-*（本地忽略目录）。

适用：媒体元数据预解析以及其他由原生事件完成、随后会立即释放原生资源的 Task。禁止用任意延时或异常吞掉代替生命周期修正。
