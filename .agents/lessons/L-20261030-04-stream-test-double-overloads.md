# 流测试替身避免 MemoryStream 重载回调递归

状态：confirmed
来源：features/F-20261030-01-project-quality/phase-plan.md，P2。

现象：测试替身继承 MemoryStream，将 Read(byte[], offset, count) 转发给自身 Read(Span<byte>)，后者调用 base.Read(span)，导致测试进程栈溢出中止。

根因：当前 .NET 10 的 MemoryStream 对派生类型走兼容路径，Span 读取可回退到 Stream.Read(span)，进而再次调用虚 Read(byte[], offset, count)。两个重载形成递归，不能假设 base.Read(span) 直接访问底层数组。

正确做法：需要观察读取或注入取消时，让替身继承 Stream 并组合一个独立 MemoryStream；各读取重载最终操作这个内部实例，明确其 Dispose 所有权。

适用范围：覆盖 Stream Span/数组/Memory 异步重载的测试替身。禁止把两个虚重载互相转发且依赖基类绕过虚分派。

验证：首次 bounded 测试运行中止于上述循环；改为组合后 85 项边界测试通过，Assets 全套 202 项通过。失败日志 out/project-quality-p2-bounded.log；成功日志 out/project-quality-p2-bounded-fixed.log。测试源码：test/GalNet.Assets.Tests/BoundedDecompressionTests.cs。
