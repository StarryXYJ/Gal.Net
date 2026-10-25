# 设计

`Benchmark=true` 在 Directory.Build.props 定义 BENCHMARK；宿主通过条件 ProjectReference 接入 GalNet.Diagnostics。普通宿主无该程序集依赖；所有埋点以 #if BENCHMARK 围住，避免 no-op scope 与参数求值开销。诊断项目本身的实现也只在该符号下编译。

独立程序集避免向 Core 引入文件系统或影响契约编译闭包。CpuProfiler.Shared 是全局入口，也允许独立实例用于测试/自定义捕获。Stopwatch 单调时钟，完成区间 X 与瞬时 i 事件；Dispose 幂等、并发安全，有限容量并显式记录丢弃数。异步区间标在起始线程，度量 inclusive wall time（含 await），不是 CPU 占用率；重叠异步区间不暗示线程执行栈。

StartupBenchmark 读取显式输出路径，在 Main 开始总区间，在 UI 准备完成后导出并可自动退出。计时不包含 CLR 进入 Main 前、JSON 导出或退出清理。Editor 的 ready 定义为欢迎页 Loaded 后的 Background dispatcher 回调；Sample 为会话初始化返回成功、CompleteStartup 后的回调，不等价于 GPU 首帧完成。

脚本先构建，再串行运行预热和测量进程，预热不计入分位数；保留 trace、日志、逐次数据、构建时长、配置和源码状态，失败/超时不作为成功样本。仅关闭或终止脚本拥有的子进程；不修改当前 PowerShell 的环境。Sample 内容构建与语言初始化准备独立于启动测量。

验证调整：条件引用切换时复用普通 assets/deps 曾导致运行时找不到 Diagnostics；采用各项目 bin/benchmark 与 obj/benchmark（含独立 restore lock），默认契约 Content 同步选择该输出路径。未改动普通构建路径。

P3 扩展：Benchmark 实现可显式镜像阶段事件到 GalNet-CPU EventSource，供 EventPipe 将阶段与运行时 JIT/采样栈对齐；默认关闭，采样数据仅用于诊断。发布实验确认收益后提供 WindowsReadyToRun 配置，使用独立 bin/obj/lock，排除契约入口与 Core/Contracts.Builtins 的 R2R 转换以保留严格字节身份。保留 IL、反射和现有渲染配置；不引入裁剪或单文件发布。

P4 扩展：同一 EventSource 通过 ETW 对齐原生 CPU 栈。Editor 正常 Main 启动两个数据准备 Task，完成后才将设置和本地化数据交给 UI/DI；文化切换与控件不后台化，设计器保持同步回退，失败退出仍观察任务。不建设通用调度系统，也不修改 Sample 的初始化策略。图形后端探测未达到收益门槛，保持原配置。
