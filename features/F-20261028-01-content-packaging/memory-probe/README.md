# 内容分包内存观测

从仓库根构建并运行：

```powershell
dotnet restore features/F-20261028-01-content-packaging/memory-probe/MemoryProbe.csproj --locked-mode
dotnet build features/F-20261028-01-content-packaging/memory-probe/MemoryProbe.csproj -c Release --no-restore
dotnet features/F-20261028-01-content-packaging/memory-probe/bin/Release/net10.0/MemoryProbe.dll features/F-20261028-01-content-packaging/memory-probe/result.json
```

第二个应用参数可指定 MiB 阈值，例如在结果路径后加 `64`。不传时使用当前默认值 32 MiB。程序在自建临时目录中生成并清理数据，不使用用户素材。

152 MiB 随机 payload 模拟资源 I/O，带 sprite metadata，但不执行图片解码。每个 1 MiB 块单独随机填充，避免重复块被 Brotli 高比例压缩而掩盖成本。内存记录为当前进程 `PeakWorkingSet64`，包含导出、安装及逐包归属检查；不代表引擎实际画面运行内存，也不构成硬上限。

比较与结果解释见 [P6](../phase-6.md)。三个 JSON 保留 64/32 MiB 对照和最终缺省值复跑，随机数据和运行调度使耗时及峰值可变化。
