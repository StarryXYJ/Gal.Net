# 条件引用必须匹配 restore 图

状态：candidate
来源：features/F-20261025-01-cpu-startup-benchmark

现象：Benchmark=true 的 Editor 编译通过，但启动在 Main 抛 FileNotFoundException: GalNet.Diagnostics；输出目录缺 DLL，deps/assets 也不含它。
已确认的直接原因：编译器看见了条件 ProjectReference，运行时依赖清单却来自不含该引用的还原结果。工作区存在普通模式构建，无法仅凭日志断言哪次 restore 覆盖了 assets。
正确做法：切换影响依赖图的属性时允许 restore，不能用 --no-restore 证明运行时成功。Benchmark 单独使用 bin/benchmark、obj/benchmark 和独立锁文件；固定路径 Content 也必须读取同模式产物。修改 BaseIntermediateOutputPath 时继续排除整个 obj/bin，避免把另一模式生成代码编入源码。
不要只凭编译成功交付条件依赖构建；实际启动并验证 trace。不要归因于尚未证实的其他进程。
