# BenchmarkDotNet 成功证据

- 状态：confirmed
- 来源：F-20261030-01-project-quality / P3a
- 范围：BenchmarkDotNet 0.15.8 命令行基准入口

现象：子工程 restore/build 失败，报告仍生成且宿主退出码为 0；结果为 NA、executed benchmarks: 0。初始日志在 `out/project-quality-p3-benchmark.log`。

根因：BenchmarkSwitcher.Run 返回失败的 Summary，默认不替应用设置非零退出码。工具运行完成不等于基准完成。

正确做法：应用检查 Summary 数量、HasCriticalValidationErrors、Reports 数量和每份 report.Success，失败时设置 ExitCode=1；验收另核对计划案例数及有效统计。本次修正后 12/12 项完整执行，退出码 0，见 `out/project-quality-p3-benchmark-verified.log` 及 feature/benchmarks 基线报告。使用 DontEnforcePowerPlan 避免基准默认更改用户机器设置。

禁止仅以报告文件存在或 dotnet run 返回 0 宣称性能测量成功；失败报告不得与后续有效数据混用。
