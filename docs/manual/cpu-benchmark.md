# 测量 CPU 启动

需要 Windows、.NET 10 SDK 和 PowerShell 7。脚本先以 Benchmark=true 构建，再启动独立进程；构建不计入 Startup.Total。默认 Release、1 次预热、5 次测量。输出包含 Chrome Trace JSON、stdout/stderr、逐阶段 CSV、中位数/P95 摘要和运行环境/源码元数据。

```powershell
pwsh -File scripts/benchmark-startup.ps1 -Target Editor -Iterations 10
pwsh -File scripts/benchmark-startup.ps1 -Target Editor -Configuration Debug -Iterations 10
pwsh -File scripts/benchmark-startup.ps1 -Target Sample -GameDirectory GameTestCase/Output -Profile Temp/benchmark-profile -Iterations 10
```

Sample 的游戏内容需要预先构建，Profile 应是已完成语言选择的测试档案（可复制已有 profile 到临时目录），避免弹窗等待。每次 Sample 启动仍会执行正常启动逻辑，包括更新该档案的启动次数。脚本不会替你构建游戏内容或修改语言选择逻辑。不要将个人正式档案用于 benchmark。

`-Warmup 0` 可以保留第一次运行，但不等于操作系统冷缓存测试。脚本不清理系统缓存。相同配置、内容、profile 初始状态和机器负载下比较结果；5 次测量的 P95 只是 nearest-rank 样本分位数，样本少时通常就是最大值。外部进程探测得到的窗口时间和 Startup.Total 定义不同，不能直接比较。

`-OutputDirectory` 指定结果目录；默认 `artifacts/benchmarks/<target>-<timestamp>`。`-TimeoutSeconds` 默认 60。`-SkipBuild` 仅用于已经使用相同配置和 Benchmark=true 构建的输出，普通程序不会导出 trace。脚本串行运行，仅对自己创建的超时进程执行 Kill，不会结束已有 Editor 或游戏。超时、非零退出、缺失 Ready、事件溢出均中止并保留已生成数据。构建耗时仅代表本次实际构建（可能增量），不等同于 clean build 基线。

在 Perfetto 或 Chrome Trace 查看器中手工打开任一 run JSON；CSV 可直接对比不同结果目录。元数据中的源码状态可能包含未提交文件，测量期间避免同时修改代码、编译或运行其他高负载任务。

## 添加埋点

让需要埋点的项目在 Benchmark=true 时引用 GalNet.Diagnostics，在方法内部显式添加编译保护：

```csharp
public async Task LoadAsync()
{
#if BENCHMARK
    using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Game.Load", "Loading");
#endif
    await LoadAssetsAsync();
#if BENCHMARK
    GalNet.Diagnostics.CpuProfiler.Shared.Mark("Game.AssetsLoaded", "Loading");
#endif
}
```

`using` 在异常返回时也会结束区间。普通构建会移除整个埋点语句，包括参数求值、计时对象和 Dispose。可以在一段代码外加局部块限定区间，也可以跨 await；名称应稳定，避免逐帧字符串拼接。该系统适合阶段诊断，测量本身有锁和分配成本，不用于纳秒级微基准。

启动之外的自定义捕获可创建 `new CpuProfiler(capacity)`，在完成所有区间后调用 Stop 和 WriteTrace(Stream)。不要使用已被 StartupBenchmark 停止的 Shared 实例继续采集。

## 验证

```powershell
dotnet test test/GalNet.Diagnostics.Tests -p:Benchmark=true -p:UseSharedCompilation=false -m:1
dotnet test test/GalNet.Diagnostics.Tests -p:Benchmark=false -p:UseSharedCompilation=false -m:1
```

运行普通宿主构建时不传 Benchmark，或显式 `-p:Benchmark=false`。Benchmark 输出使用每项目 bin/benchmark/<Configuration>，中间产物和锁文件使用 obj/benchmark，避免与普通构建的条件依赖图互相覆盖。切换模式后的首次构建必须允许 restore；-SkipBuild 只复用同模式现有产物。Core/Contracts 不引用诊断程序集。

仓库在 CI=true 时启用 locked restore；全新 CI 工作区需要先准备 Benchmark 隔离锁文件，例如显式执行一次 `dotnet restore <host.csproj> -p:Benchmark=true -p:RestoreLockedMode=false`，再复用该还原目录。脚本不修改仓库的 CI 锁定策略。
