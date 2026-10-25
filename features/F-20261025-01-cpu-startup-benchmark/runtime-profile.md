# P3 启动调用栈与 ReadyToRun 实验

状态：verified。用户授权：继续启动调用栈分析；JIT 明显时测试 R2R；有效且验证通过独立提交，否则拒绝并回退。

使用 dotnet-trace 10.0.750501（安装在 Temp/dotnet-tools），采集 Runtime 0x1C（JIT/Loader/AssemblyLoader）、SampleProfiler 与显式启用的 GalNet-CPU。普通构建不编译此镜像；常规 Benchmark 默认不触发 EventSource。三份 trace 均为诊断数据，不参与性能收益对照。

TraceEvent 3.1.21 离线读取并配对 MethodJittingStarted / MethodLoad 事件，按 UI 线程裁切阶段、计算区间并集；以 Editor.RunAvalonia 瞬时事件对齐 JSON 时间轴与 EventPipe 时间轴。统计为 wall-clock 区间，不是精确 JIT CPU 占用，不能与采样计数直接相加。

| Capture | Total wall / JIT union | XAML wall / JIT union | RunAvalonia→XAML wall / JIT union |
| --- | --- | --- | --- |
| 01 | 1629.6 / 979.7 ms | 374.6 / 297.6 ms | 419.8 / 104.4 ms |
| 02 | 1606.3 / 996.7 ms | 384.8 / 306.7 ms | 367.3 / 97.1 ms |
| 03 | 1651.6 / 1033.6 ms | 406.9 / 323.5 ms | 364.5 / 99.6 ms |

每份主线程已配对 JIT 方法 13,428 个，没有未配对起点。XAML 阶段初次生成的样式填充方法与类型初始化明显；前置框架间隔还大量采样于 Avalonia.Win32.OpenGl.Angle.AngleWin32EglDisplay.CreateD3D11Display（首份该区域 221 个样本中有 122 个叶帧归属此方法）。EventPipe 没有完整 Windows 原生栈，故只能定位到托管调用边界，不能进一步断言是驱动、硬件还是某个原生子函数。

结论：JIT 在整个启动中占比明显，值得进行 R2R 对照。保持当前 GPU 后端与外观，不通过切软件渲染改变性能目标。

R2R 方法：两版均 Release、win-x64、framework-dependent，同一源代码、同一 Benchmark 开关，仅 PublishReadyToRun 不同。排除 Core、Contracts.Builtins 和固定入口 Contracts，避免转换改写严格二进制身份；发布后检查哈希与 PE native header，并在不启用 EventPipe 时交错比较固定输出。baseline/candidate 位于 Temp/r2r-experiment/il 与 r2r。

原始证据：artifacts/benchmarks/runtime-profile-01、02、03。分析源码：scripts/performance/analyze-startup-trace.cs；复现方式见 docs/manual/cpu-benchmark.md。

## 同轮交错对照

2026-10-25，两版各预热一次，再进行 AB/BA 交错 10 对，无 EventPipe 采样；ready 口径未变。Sample 使用 GameTestCase/Output 与已完成语言选择的 Temp/cpu-benchmark-profile。

| 宿主 | IL Median | R2R Median | 改善 | IL P95 | R2R P95 | 胜出 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Editor | 1446.65ms | 845.37ms | 41.6% | 1487.32ms | 940.01ms | 10/10 |
| Sample | 2214.57ms | 1515.76ms | 31.6% | 2255.08ms | 2060.09ms | 10/10 |

配对差值中位数分别 600.03ms、708.81ms；对配对差值 bootstrap（固定 seed=25，10,000 次）中位数 95% 区间分别 [572.85, 629.01]ms、[642.83, 735.84]ms。样本仍仅为本机预热缓存的新进程启动，不是系统冷启动；不与 P1/P2 跨轮绝对时间比较。

Editor 未压缩发布体积 421.29→452.27 MiB，增加 30.98 MiB；Sample 416.12→439.60 MiB，增加 23.47 MiB。未量化工作集或发布耗时，发布转换本身增加构建工作。

固定输出：Temp/r2r-experiment/il、r2r；Temp/r2r-sample/il、r2r。证据：artifacts/benchmarks/r2r-editor/paired 与 r2r-sample/paired 的 comparison.csv、summary.csv、各次 trace/metadata；identity.json 保留程序集身份检查。原始大文件被 Git 忽略，本报告保留方法和统计。

## 发布配置与完整性

提供 Editor/Sample 的 WindowsReadyToRun.pubxml：Release、win-x64、framework-dependent、不裁剪、不合并单文件。普通命名配置隔离到 bin/publish/WindowsReadyToRun、obj/publish/WindowsReadyToRun（含 lock），避免向普通 packages.lock.json 写入 RID 目标；Benchmark=true 仍走 benchmark 输出。Editor DefaultContracts Content 随模式选择正确的 class library 产物路径。

R2R 会改写程序集；Directory.Build.targets 明确排除 Core、Contracts.Builtins、GalNet.Project.Contracts。实验发布与最终普通发布均检查：被排除 DLL 的 native header 为空、SHA256 与对应编译输入一致；Editor/Sample 主程序集具有 R2R native header。Editor 实际 LoadContractsDirectory 成功返回 45 个定义，未放宽匹配逻辑。

## 验证与诊断复查

- Diagnostics 启用测试 5/5；禁用测试 1/1，包含 CpuTraceEvents 不存在检查。
- Editor 首次显示/预先导航及真实视图主题回归 3/3；日志 artifacts-benchmark-r2r-regression.log。
- Editor/Sample 最终普通发布成功，deps 无 Diagnostics；真实进程分别等到窗口可用、Session initialized. 后正常关闭，退出码均 0。日志 artifacts-benchmark-normal-profile.stdout.log、artifacts-benchmark-sample-normal-smoke.log。没有终止用户的 Editor。
- Editor 最终普通发布契约实际加载通过；Core/Builtins 与默认契约输入逐字节一致。Sample 根契约与同模式源产物一致。
- profile-startup.ps1 实际采集基线与两宿主 R2R；analyze-startup-trace.cs 均成功，丢失事件 0、未配对 JIT 起点 0。PowerShell 脚本语法、git diff --check 通过。本轮仅运行相关测试，不声称全仓测试通过。

R2R Editor 诊断 trace：Total wall/JIT union 929.0/239.0ms，UI JIT 2,857 个；XAML 130.7/28.8ms。R2R Sample：1497.7/289.7ms，UI JIT 3,411 个。这些单次带采样时间不参与上表收益统计。证据在两个 r2r-*/runtime-profile 目录。

后续最大的可见线索仍是 ANGLE/D3D11 初始化的托管边界；当前证据不足以断言原生细分原因，暂不切换 GPU 后端。使用发布配置才能获得本轮收益，普通 Debug/dotnet run 不会自动加速；操作见 docs/manual/ready-to-run.md。
