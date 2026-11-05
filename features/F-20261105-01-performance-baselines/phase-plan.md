# 阶段

1. verified：桌面 Sample 启动采集接入；Benchmark/普通 Release 构建通过，普通 deps 无 Diagnostics，真实启动 Ready 和空内容失败退出通过。五次预热后采样见 baseline.md。
2. planned：评估 Editor 和 Sample 的现有交互基准，补充有代表性的缺口与内存、体积测量，记录可复现结果。
3. planned：分析启动瓶颈，按测量证据评估优化并完成总结。Android 构建和真机验证由并行工作负责。
