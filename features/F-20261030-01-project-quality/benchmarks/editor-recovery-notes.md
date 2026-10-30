# P4c：恢复快照后台持久化

基于 P4b 的恢复实现，将工作版捕获与持久化分离。调用方仍克隆一致快照；内部 ProjectRecoveryWriter 按接受顺序在后台执行现有 store 的序列化、hash、写盘与裁剪，只接触独立快照。没有新增公共 API、恢复格式或 dirty 缓存。

周期恢复释放 history capture guard 后等待写入，PeriodicTimer 合并等待期间的 tick，不累计周期快照。保存失败、结构错误和主动丢弃重载仍同步等待恢复完成。项目异步关闭先停周期请求，再等待末次快照和 writer；直接 Dispose 只等待纯后台 writer，不等待作者线程 continuation。错误可观察，下一次写入可重试；恢复完成不推进保存基线。

## 调用线程测量

沿用 [P4a 夹具与环境](editor-baseline-notes.md)，1k/10k Entry、20 Group、8 个文本文件合计约 1/10 MB。新增 RecoverySchedule，每次 iteration 先改变 Graph.Name，只提交一个请求，IterationCleanup 等待其写完并检查 LastRecoveryError。这样避免不断排队造成内存和耗时失真。计时不包含 cleanup 等待。

修改生产代码前，RecoverySchedule 调用旧的同步 ReportError；修改后调用 CaptureRecoveryAsync。前者还包含错误属性赋值和无订阅者的 Refresh，后者测量克隆与提交；均不包含 dirty 查询、实际 UI 或 history guard。它测的是恢复请求返回前的同步工作，不是恢复文件可用时间。

| 条目数 | 修改前均值 | 修改后均值 | 同步部分减少 |
| --- | --- | --- | --- |
| 1,000 | 9.037 ms | 0.596 ms | 约 93.4% |
| 10,000 | 41.082 ms | 2.720 ms | 约 93.4% |

原始报告：[修改前](editor-recovery-before.md) / [CSV](editor-recovery-before.csv)，[修改后](editor-recovery-after.md) / [CSV](editor-recovery-after.csv)。修改前 2/2、修改后 4/4 项成功，退出码均为 0。1 launch / 3 warmup / 6 measured iterations；RecoverySchedule 的 InvocationCount=1、UnrollFactor=1。两轮均有 MinIterationTime 警告：一次克隆低于建议的 100 ms。此处为避免队列积累而刻意保持单次调用，少量采样只确认同步工作明显缩短，不作为 CI 阈值或精确 UI 延迟承诺。

同步关键路径 RecoveryThroughSession 仍测到完整恢复结束：1k 为 8.278 ms，10k 为 45.579 ms；P4a 历史基线为 5.853/42.528 ms。本轮均值分别上升约 41%/7%，10k 置信区间重叠。保留这项代价，不宣称同步恢复加速；历史基线与本轮存在运行时机差异，不能把差值全部归因于线程调度。

MemoryDiagnoser 在后台模式下不能用来证明总分配减少：不同线程的分配及 cleanup 与测量区间的关系发生变化。原始分配数字仅保留作证据，不以 RecoverySchedule 的分配下降宣称内存优化。序列化/写盘工作仍然存在，未测峰值内存、真实窗口输入延迟、慢盘尾延迟或完整关闭时间。快照克隆和 dirty 比较仍在作者线程执行。

## 验证与复现

- Editor.Shared：521 项通过。新增六项测试覆盖周期 guard 释放/tick 合并/关闭末次内容、关键恢复排序与独立快照、Dispose 排空且不依赖调用线程上下文、旧恢复与保存/后续编辑交错、真实写盘失败重试、writer 停止及失败后的串行顺序。已有保存失败/外部重载恢复、保存期间编辑、撤销及 dirty 回归均通过。
- Architecture：53 项通过。
- 两规模 benchmark 自检通过，补充验证后台提交写出的最新 revision。Release 基准构建成功，0 错误；保留已有分析器警告，新增测试中有三处 CA1861 数组断言建议。
- 本阶段未做原生窗口验证；正式 docs 同步按用户要求留后。

```powershell
dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --no-restore -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test test/GalNet.Architecture.Tests/GalNet.Architecture.Tests.csproj --no-restore -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build benchmarks/GalNet.Editor.Benchmarks/GalNet.Editor.Benchmarks.csproj --no-restore -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project benchmarks/GalNet.Editor.Benchmarks/GalNet.Editor.Benchmarks.csproj -c Release --no-build --no-restore -- --verify
dotnet run --project benchmarks/GalNet.Editor.Benchmarks/GalNet.Editor.Benchmarks.csproj -c Release --no-build --no-restore -- --filter '*RecoverySchedule*' '*RecoveryThroughSession*' --artifacts out/quality-p4c-after --exporters json markdown
```

修改前测量采用 P4b 生产实现（`0e28f58a`）和新增的单次 iteration 夹具：RecoverySchedule 方法体为 `_session.ReportError(_failure)`，尚无后台自检。不要直接用 P4a 的自动 InvocationCount 数据替代此专项基线。

本地日志：`out/project-quality-p4c-before-benchmark.log`、`out/project-quality-p4c-after-benchmark.log`、`out/project-quality-p4c-editor-final.log`、`out/project-quality-p4c-architecture.log`、`out/project-quality-p4c-benchmark-build.log`、`out/project-quality-p4c-verify.log`。完整 JSON 位于 `out/quality-p4c-before/results/` 和 `out/quality-p4c-after/results/`；out 为忽略目录，表格与 CSV 已保存到 feature。
