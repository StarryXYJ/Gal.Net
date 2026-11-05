# Sample 启动初始样本

Windows，本机 Benchmark Release 桌面宿主，GameTestCase/Output 与独立测试 profile，1 次预热、5 次测量：Startup.Total 中位数 5797.81 ms、P95 6296.38 ms。每 50 ms 采样的工作集峰值范围约 374–408 MiB。结果在 `output/sample-startup-baseline`，该目录不提交；测试 profile 的启动次数随运行增加。此为本机预热缓存样本，不代表冷启动、Android 或发布包性能。

中位数对应的一次 trace：从 Main 到开场派发约 1966 ms，到环境准备约 1995 ms，玩家创建约 2635 ms，Ready 约 5798 ms。Ready 包含开场完成，阶段有重叠，不能相减当作纯计算成本；采集使用的早一版标记名为 OpeningVisible，实际只代表 UI 派发，不证明首帧已显示。后续标记已改名 OpeningDispatched。采集后仅修改了标记名称，最终构建和单次启动冒烟通过；本表不是最终构建的重新采样。

空内容失败产生 Startup.Failed 且退出码 1；普通 Release 的 deps.json 不含 Diagnostics。首次启动和 GPU 帧、UI 卡顿、峰值物理内存、真实包体积等仍需后续测量；当前没有做启动优化，也不宣称性能改善。
