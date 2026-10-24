# CPU 启动 benchmark — 实现总结

已完成 quick 通道的 CPU 事件采集与自动化基线。未进行启动性能优化。

## 实现

- 独立 GalNet.Diagnostics：CpuProfiler.Shared、作用域 Measure、瞬时 Mark、Stop/Snapshot/WriteTrace，有界线程安全缓冲和 Chrome Trace JSON。计时为 CPU 侧 wall time，可跨 await，包含等待；没有 GPU 时间戳。
- Benchmark=true 定义 BENCHMARK；所有宿主埋点显式条件编译。普通宿主不引用 Diagnostics。Benchmark 输出、obj 和 restore lock 与普通构建隔离；默认契约内容使用同模式产物。
- Editor 接入 Main、XAML、服务注册/校验、设置/语言/主题、主窗口、欢迎页创建/导航；Sample 接入相同框架阶段及会话初始化。
- scripts/benchmark-startup.ps1：单独构建、预热、重复启动、自动完成退出，保存 trace/日志、逐次 CSV、Median/P95 和环境/源码元数据。失败、超时、缺失 Ready、事件溢出不计入成功结果。

当前规范：[CPU 性能事件](../../docs/spec/cpu-benchmark.md)。操作与方法内埋点：[测量 CPU 启动](../../docs/manual/cpu-benchmark.md)。

## 验证

2026-10-25，当前 Windows 工作区：

- `dotnet test test/GalNet.Diagnostics.Tests/GalNet.Diagnostics.Tests.csproj -p:Benchmark=true -p:UseSharedCompilation=false -m:1 -nr:false`：4/4，包括嵌套/瞬时事件 JSON、跨线程幂等 Dispose、容量/停止、启动导出/单次完成。
- 同项目 Benchmark=false：1/1，诊断实现类型不存在。
- GalNet.Architecture.Tests：49/49，新诊断项目无内层依赖。
- Editor/Sample 的 Benchmark Release 和普通 Release 均构建成功；普通宿主 deps.json 无 GalNet.Diagnostics。
- Editor 最终采集 smoke 成功：artifacts/benchmarks/editor-final-smoke。
- Sample 最终采集 smoke 成功：artifacts/benchmarks/sample-final-smoke。
- 使用空游戏内容的 Sample 失败检查：artifacts/benchmarks/sample-failure-final；进程退出码 1，trace 有 Startup.Failed，无成功摘要。
- PowerShell 脚本语法解析及 git diff --check 通过。

VSTest 在 sandbox 内无法连接 testhost，实际通过记录来自允许本地进程通信的运行环境。构建中有既有分析器/XAML 警告，以及 NuGet audit 服务暂不可达的 NU1900；无编译错误。

## 本轮测量

Release；每目标 1 次预热，预热排除；Sample 使用预先构建的 GameTestCase/Output，并复制玩家变量到 Temp/cpu-benchmark-profile，未改动原玩家档案。数值单位 ms。

| 阶段 | 样本 | Median | P95 |
| --- | ---: | ---: | ---: |
| Editor Startup.Total | 5 | 2068.31 | 2318.66 |
| Editor.Xaml | 5 | 498.96 | 564.03 |
| Editor.CreateMainWindow | 5 | 251.19 | 290.84 |
| Editor.BuildServiceProvider | 5 | 20.66 | 25.09 |
| Sample Startup.Total | 3 | 3691.36 | 5336.43 |
| Sample.FrameworkInitialization | 3 | 1122.13 | 1235.86 |
| Sample.InitializeSession | 3 | 799.74 | 1521.31 |

完整证据：artifacts/benchmarks/editor-isolated 与 artifacts/benchmarks/sample-baseline，包含各 run trace、measurements.csv、summary.csv、metadata.json 和 build.log。结果目录被 Git 忽略；本表保留便于追溯的样本统计。最终修正失败退出码后各宿主又完成一次成功 smoke；该 smoke 不并入以上统计。

本轮仅验证采集链路并提供初始数据，工作区还有另一项 UI feature 的改动。样本少且 Sample 变化明显，不能据此建立稳定回归阈值。Startup.Total 从 Main 到宿主 ready，不包含 CLR 进入 Main 前、trace 输出、退出清理，也不代表 GPU 首帧完成。阶段存在嵌套，不能求和。已测 Editor DI 校验约 21ms，不是主要已测阶段；框架与调度之间仍有未细分时间。

## 验证中修正与限制

- 条件 ProjectReference 与普通 restore 图混用，曾造成编译成功但运行缺少 Diagnostics；隔离 bin/obj/lock 后实际启动通过。见 .agents/lessons/L-20261025-02-benchmark-restore-graph.md。
- Avalonia lifetime 可覆盖提前设置的 Environment.ExitCode；宿主在 lifetime 返回后的 finally 根据保留的 Failed 状态重申失败码。最终失败集成检查证实为 1，脚本还同时检查 Ready。见 L-20261025-03-avalonia-benchmark-exitcode.md。
- 事件时间戳允许同一时钟刻度；测试按事件名验证协议，不依赖相同时间戳事件的数组顺序。
- Startup 自动捕获每进程一次，完成后停止 Shared。需要进一步运行时采集时可不使用启动自动输出、由调用方导出 Shared，或使用独立 CpuProfiler 实例。
- 当前自动脚本针对 Windows 桌面宿主；Sample 需预构建内容和完成语言选择的测试 profile。CI locked restore 的首次隔离锁准备见手册。GPU、自动方法织入和性能优化未列入本次范围。
