# UI 测试必须异步观察异常

状态：confirmed
来源：F-20261013-02-parameter-and-sample-simplification / Phase 4b2

在 Headless Dispatch<bool> 的 UI 回调内使用 NUnit Assert.ThrowsAsync 等待一个需要回到 UI 线程的关闭任务，会同步阻塞 UI pump。挂起收集器定位到 SampleCleanupTests.RealSessionClearsOwnedStateWhenSettingsFlushFails；该关闭流程等待 SettingsCoordinator 的 Task.Yield/UI continuation。

正确做法是在 UI 回调中直接 await 任务，用 try/catch 捕获预期异常，再做普通断言。保留整个异步断言链，不能改 async void 或跳过 flush。Assert.ThrowsAsync 只适合不依赖当前 UI pump 的任务。原挂起 Sequence XML 位于 Temp/F-20261013-02/3f709c29-b7db-4b10-80f6-31a60a0bc9bb。

关联：L-20261013-04-headless-async-void-dispatch.md、L-20261011-01-headless-session-cleanup.md。
