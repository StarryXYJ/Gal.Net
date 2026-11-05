# Sample 启动初始样本

Windows，本机 Benchmark Release 桌面宿主，GameTestCase/Output 与独立测试 profile，1 次预热、5 次测量：Startup.Total 中位数 5797.81 ms、P95 6296.38 ms。每 50 ms 采样的工作集峰值范围约 374–408 MiB。结果在 `output/sample-startup-baseline`，该目录不提交；测试 profile 的启动次数随运行增加。此为本机预热缓存样本，不代表冷启动、Android 或发布包性能。

中位数对应的一次 trace：从 Main 到开场派发约 1966 ms，到环境准备约 1995 ms，玩家创建约 2635 ms，Ready 约 5798 ms。Ready 包含开场完成，阶段有重叠，不能相减当作纯计算成本；采集使用的早一版标记名为 OpeningVisible，实际只代表 UI 派发，不证明首帧已显示。后续标记已改名 OpeningDispatched。采集后仅修改了标记名称，最终构建和单次启动冒烟通过；本表不是最终构建的重新采样。

补充初始化/开场标记后，同机同夹具重新采样 1 次预热、5 次测量，`output/sample-startup-phases`：总启动中位数 5485 ms；初始化完成时刻中位数 3216 ms，开场完成时刻中位数 5326 ms。每次初始化完成后仍等待开场约 2017–2218 ms；这是这组预热样本中明确的用户等待，不代表可以在不改变开场体验的前提下消除。玩家创建时刻约 2213–2486 ms；具体成本仍须进一步剖析。没有把两轮结果当成优化前后比较。

空内容失败产生 Startup.Failed 且退出码 1；普通 Release 的 deps.json 不含 Diagnostics。首次启动和 GPU 帧、UI 卡顿、峰值物理内存、真实包体积等仍需后续测量；当前没有做启动优化，也不宣称性能改善。

## Editor 内容操作

当前工作区，Windows 11 / i7-13650HX / .NET 10.0.12，BenchmarkDotNet 0.15.8，1 launch、3 warmup、6 iterations；夹具自检通过，报告在 `output/editor-benchmark-current/results`。四项均成功：

| 操作 | Entry 数 | 平均耗时 | 每次托管分配 |
| --- | ---: | ---: | ---: |
| DirtyClean | 1,000 | 0.307 ms | 760 B |
| CaptureWorking | 1,000 | 1.043 ms | 1,481,578 B |
| DirtyClean | 10,000 | 3.535 ms | 760 B |
| CaptureWorking | 10,000 | 3.501 ms | 14,729,560 B |

合成项目，调用线程测量，内存为累计托管分配而非峰值；不含真实 UI 绘制与用户输入，不能直接推断卡顿。当前工作区包含并行改动，不能当成干净提交的对照；不同机器的历史数值也不能直接用于收益结论。

## Sample 局部 UI

同机、BenchmarkDotNet 0.15.8，Headless Skia、1 launch / 3 warmup / 6 iterations；自检覆盖鉴赏页面、解码、工具栏断点和受控缩略图加载。以下三项完成，报告在 `output/sample-benchmark-current/results`：

| 操作 | 规模 | 平均耗时 | 每次托管分配 |
| --- | ---: | ---: | ---: |
| 鉴赏页面挂载和布局 | 100 项 | 42.37 ms | 4.91 MiB |
| 鉴赏页面挂载和布局 | 1,000 项 | 37.39 ms | 4.91 MiB |
| 工具栏跨断点重排 | 每次调用 20 次操作 | 281.6 µs/操作 | 27.82 KiB/操作 |

页面自检确认两种规模均只创建 12 个卡片；测量不含媒体解码或真实窗口渲染。鉴赏耗时误差较大，不能据此断言规模较大时更快；工具栏每轮耗时低于 BenchmarkDotNet 建议的最短时长，因此只能作初步参考。两者均不代表 GPU 帧时间或峰值内存。

缩略图和加载另测 4 项，结果在 `output/sample-benchmark-media/results`。对生成的 2560×1440 PNG，完整解码平均 14.99 ms，按宽 768 解码平均 21.72 ms；托管分配分别约 1.21/1.51 KiB，不包括 Skia 原生像素内存，不能据此推断总内存或真实素材性能。受控异步加载每项请求延迟 5 ms，顺序激活 8/64 项平均 123.6/992.0 ms；Windows 定时器和夹具会影响绝对时间，但该用例说明等待随数量累积，值得在真实资源下验证按需加载或有限并发的收益，并检查取消与资源释放。
