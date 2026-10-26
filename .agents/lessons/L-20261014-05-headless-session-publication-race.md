# Headless 12.0.5 会话启动的任务发布竞态

状态：confirmed（框架源码与失败位置吻合）
来源：F-20261013-02-parameter-and-sample-simplification / Phase 2c

## 证据与处理

两次 Presentation 全回归各出现一次 DisposeAsync NullReferenceException，分别发生在不同测试，产品断言均通过。Avalonia 12.0.5 的 HeadlessUnitTestSession.StartNew 在 Task.Run 回调里用闭包 task 构造会话，而外部 task 赋值尚可能未完成；DisposeAsync 等待此引用时会空引用。源码：https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Headless/Avalonia.Headless/HeadlessUnitTestSession.cs。

Presentation 测试改用官方 GetOrStartForAssembly 配合 AvaloniaTestApplication 特性，由框架缓存会话，测试不逐项释放缓存 worker。默认 PerTest 隔离仍使每次 Dispatch 重新建立和释放应用 scope。测试必须自己关闭窗口并释放产品资源；不能捕获并吞掉任意产品异常或用成功重试掩盖断言失败。此调整避免反复创建/释放会话，不修改第三方源码，也不声称修复其内部启动竞态。

2026-10-26 引擎重构 R4a：Editor 隔离全回归中的 TimelineKeyDetailsBatchEditInterpolationTangentsUnitsAndPresetsThroughMountedControls 在 HeadlessUnitTestSession.DisposeAsync 发生一次空引用（调用点为测试 await using 收尾）。定向 1 项与完整 Editor 323 项重跑通过，未修改 Timeline 产品代码；原日志 r4a-editor-tests.log 与重跑日志 r4a-timeline-recheck.log/r4a-editor-recheck.log 保留。该现象与上述已知竞态的失败位置一致，不把一次成功重跑表述为框架问题已修复。

2026-10-26 R6/R8：Editor 全回归再次在 CharacterNestedListFieldUsesSemanticEditorAndRenameUpdatesStoredWirePath 的 DisposeAsync 复现相同空引用，319 项通过；该用例独立复核通过。R8 将 Editor 与剩余 Integration 的 StartNew/逐项 Dispose 改为官方 GetOrStartForAssembly，复用各自既有 application builder，保留每项 Dispatch、窗口关闭与产品资源释放。不捕获该空引用、不删除功能断言；完整复核结果见 feature R8。

R8 最终完整复核：Editor 320、Integration 128 项全部通过；缓存 worker 的改动保留了普通用例的完整执行，没有跳过或捕获框架失败。
