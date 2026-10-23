# 调试 Step 返回不等于异步呈现完成

- 状态：confirmed
- 来源：F-20261021-02-image-variants P5 原生 Sample 测试，2026-10-23。
- 现象：外部调试 Step 已返回，立刻检查 sample.screenshot 目标文件却不存在；等待其原子发布后五个检查点全部通过。
- 根因：GameEngine.DebugSession.StepAsync 推进指令范围并返回，AsyncPrimitiveInstance 的呈现任务仍可能执行；协议响应不是外部 I/O 的完成信号。
- 发现方式：复现原生发行包测试，对照 DebugStepping 与 SampleScreenshotModule 的异步实现，加入有截止时间的文件完成等待后复跑通过。
- 正确做法：先以 seek/pause 定位到作者范围，再 Step；根据具体任务的完成信号等待（截图为原子发布的目标文件），并设取消与超时。截图前也需给异步图片加载和布局完成的机会。
- 适用范围：外部调试驱动截图、媒体或其他 AsyncPrimitiveInstance 的集成测试；普通同步图集 select 不需要此等待。
- 禁止：把 Step 返回当作所有呈现任务完成；用无限等待掩盖呈现失败；在 CatchUp 中期待有外部截图副作用。
- 验证：ImageVariantsNativeSampleTests.Packaged_variants_render_in_native_sample_and_close_cleanly；artifacts/image-variants-p5/p5-native.trx。
