# CPU 启动 benchmark — 实现总结

P1 已完成 quick 通道的 CPU 事件采集与自动化基线；P1 交付时未进行启动性能优化。后续 P2 优化实验见 experiments.md。

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

## P2 持续优化

三项实验均已结束，ready 定义未变。完整结果见 [experiments.md](experiments.md)。

| 实验 | 同轮基线 Median | 候选 Median | 处置 |
| --- | ---: | ---: | --- |
| 窗口显示前准备欢迎页，20 对 | 1938.59ms | 1834.01ms | 改善约 5.4%，保留并提交 4bb1dbc |
| 暂停首屏加载 Dock/ColorPicker，10 对 | 1907.65ms | 1845.32ms | 约 3.3%，波动覆盖零，未达到门槛；回退 |
| 关闭 Acrylic，10 对 | 1473.09ms | 1477.35ms | 无总启动收益；回退 |

第一项 14/20 配对更快，P95 2321.63 → 2124.10ms。新增首次显示/预先导航的 2 个行为用例与既有真实视图主题用例通过，共 3/3；Debug 输出被正在运行的用户 Editor 锁定后，使用 Release 验证且保留该进程。比较工具及额外里程碑另行提交 828bf9f。

两个被拒绝探测的 XAML 已按实验前快照逐字节恢复，git diff 确认无产品代码残留。恢复后 Benchmark Release 再构建成功（0 errors），真实启动/导出/自动退出 smoke 成功：artifacts/benchmarks/optimization-final-restored-smoke。本次只运行相关检查，不声称全仓测试通过。

同一个固定提前首页快照在不同轮次的基线可从约 1908ms 变化到 1473ms，说明缓存与机器负载影响明显；只能使用各轮固定二进制的交错 A/B 判断收益。最终 smoke 不是额外的优化幅度证据。

## P3 调用栈与 ReadyToRun

已完成，完整方法和证据见 [runtime-profile.md](runtime-profile.md)。增加显式启用的 Benchmark EventPipe 镜像、运行时采集/离线分析脚本，并将交错比较扩展到 Sample。三次调用栈发现大量启动 JIT，随后保持相同源代码/RID/部署形式进行无采样对照，各 10 对全部更快：Editor Median 1446.65→845.37ms（41.6%），Sample 2214.57→1515.76ms（31.6%）。

保留两个宿主的 WindowsReadyToRun 发布配置，隔离 RID 还原图，排除严格契约闭包的转换并验证字节身份；不裁剪、不改变渲染或匹配规则。代价为 Editor/Sample 未压缩目录约增加 31.0/23.5 MiB。收益限于发布程序；Main→ready、预热缓存的新进程测试不代表冷缓存、GPU 首帧或普通 Debug。

启用/禁用诊断测试 5/5、1/1，Editor 相关回归 3/3，两个普通发布的真实启动/正常退出与无诊断依赖通过，Editor 发布契约实际加载 45 项。手册已同步 CPU 调用栈和 ReadyToRun 操作；未运行全仓测试或测量工作集。
