# 设计

沿用条件编译的 GalNet.Diagnostics 与既有 BenchmarkDotNet，桌面 Sample 启动从 Main 到玩家准备完成及开场结束，失败不得视作 Ready。启动采样与真实帧呈现、用户交互、冷缓存分别描述。工作集为进程采样值，托管分配仍由 BenchmarkDotNet 测量；发布体积使用固定配置与内容的文件/归档报告。优化需要在相同条件下比较，不能以历史数据或单次冒烟结果推导收益。
