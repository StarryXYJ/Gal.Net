# CPU 性能事件

`src/Shared/GalNet.Diagnostics` 是独立、不依赖 Core、UI 或日志库的诊断程序集。全仓 `Benchmark=true` 构建属性定义 `BENCHMARK`；Editor 和 Avalonia Sample 只在此属性启用时引用诊断项目。Benchmark 的 bin/obj/restore lock 均位于各项目的 benchmark 子目录，Editor 默认契约内容也从同模式产物复制。普通构建不包含宿主埋点代码或该程序集引用；诊断程序集本身的实现也被条件编译移除。

`CpuProfiler.Shared.Measure(name, category)` 返回幂等 `IDisposable` 区间，`Mark` 记录瞬时事件。允许独立实例。时间使用 Stopwatch 单调时钟，单位为微秒；区间是 inclusive wall-clock duration，包含等待、调度、JIT 等开销，不是处理器执行时间或占用率。跨 await 区间归属开始时的 managed thread ID，不能据此把重叠异步区间解读成真实线程调用栈。没有 GPU timestamp 或 PIX 对接。

采集和快照线程安全，每实例默认最多 100,000 个事件；超过容量后丢弃后续事件，导出 `droppedEvents`。`Stop` 阻止之后新增事件，包括尚未结束的区间；快照只包含已结束区间与瞬时事件。`WriteTrace(Stream)` 导出 Chrome Trace Event JSON（区间 X、瞬时 i），流所有权属于调用方。

`StartupBenchmark.Start()` 在 Main 入口调用。存在 `GALNET_BENCHMARK_OUTPUT` 时创建 `Startup.Total` 区间；`Complete(success)` 结束它、记录 Ready/Failed、停止采集，并通过同目录临时文件导出 trace。每进程只完成一次。`GALNET_BENCHMARK_EXIT=1` 使 Complete 返回 true，由宿主正常关闭窗口。失败或导出错误设置非零进程退出码；未设置输出路径不会自动退出或写文件。Benchmark 构建中的手工 Measure/Mark 仍可用于自定义采集。

Editor 完成点：欢迎页 Loaded 后的 Background dispatcher 回调。Sample 完成点：会话初始化返回后、CompleteStartup 更新 UI 后的 Background dispatcher 回调；初始化失败不是 Ready。它们不保证 GPU 首帧完成或所有惰性初始化已执行。Startup.Total 不含 CLR 进入 Main 前、trace 写出及进程关闭；脚本另记 ProcessLifetimeMs，后者包含启动和退出，不能当作纯启动时间。嵌套阶段的时长不能直接求和。
