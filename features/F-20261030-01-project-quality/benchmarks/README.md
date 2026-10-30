# PAK 性能测量

## P3a 基线（2026-10-30）

生产代码基线：`1047cc0a`（P2）。基准工程见 `benchmarks/GalNet.Assets.Benchmarks`；BenchmarkDotNet 0.15.8，锁定依赖。原始结果见 [baseline.md](baseline.md) 与 [baseline.csv](baseline.csv)，包含均值、误差、标准差和分配字节。12/12 项成功，进程退出码 0；Release 构建零警告/错误，两种尺寸的夹具自检通过。

环境：Windows 11 24H2，Intel Core Ultra 7 265K，SDK 10.0.401，.NET 10.0.12 x64。每项一次 launch、三次 warmup、六次测量；不修改机器电源计划。

复现（仓库根目录）：

```powershell
dotnet restore benchmarks/GalNet.Assets.Benchmarks/GalNet.Assets.Benchmarks.csproj --locked-mode
dotnet build benchmarks/GalNet.Assets.Benchmarks/GalNet.Assets.Benchmarks.csproj --no-restore -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project benchmarks/GalNet.Assets.Benchmarks/GalNet.Assets.Benchmarks.csproj -c Release --no-build --no-restore -- --verify
dotnet run --project benchmarks/GalNet.Assets.Benchmarks/GalNet.Assets.Benchmarks.csproj -c Release --no-build --no-restore -- --filter '*' --artifacts out/quality-p3-baseline-verified --exporters json markdown
```

BDN 会另建子工程并还原依赖，需要正常的 SDK/NuGet 访问环境。入口拒绝空结果、验证错误和失败报告。首次受限环境运行子工程构建失败、0 项执行却返回成功，其 NA 报告不属于基线；修正入口后完整复跑。有效本地日志为 `out/project-quality-p3-benchmark-verified.log`，完整 JSON 在 `out/quality-p3-baseline-verified/results/`。

夹具：固定 Random(42)，16 个 bulk 资源合计 16/64 MiB，再加四个 4 KiB 资源；默认 Brotli。随机数据近似已压缩媒体，避免零填充样本产生虚假压缩收益。文件 provider 使用预期整包 SHA-256，AssetManager 通过 ResourceOverlayProvider 访问。准备、清理不计入测量；自检覆盖内容往返及两种导出逐字节一致。

| 方法 | 测量范围 |
| --- | --- |
| OpenMemoryIndex | 从已有 byte[] 解析索引；不含磁盘与整包 hash |
| ReadOneVerified | 每次打开文件、验证整包、读取一个 4 KiB 条目 |
| ReadFourVerified | 通过 overlay/manager 顺序读取四个不同 bulk 条目；无保留句柄 |
| HeldHandleCacheHit | 另有存活句柄时的 manager 缓存获取/释放 |
| ExportBuffered | 从 GameFile 构建完整 byte[] PAK |
| ExportToFile | 从同一 GameFile 集合写文件 |

## 数据驱动的下一步

- 64 MiB 下 ReadOneVerified 为 44.30 ms / 134,344,353 B，ReadFourVerified 为 246.32 ms / 604,156,283 B。源码中 File.ReadAllBytes 后 Archive 再复制数据区，解释了单个小条目读取约两倍包大小的分配。P3b 首先复用 provider 独占的输入缓冲，保持公开 Deserialize 的输入快照和每次重新验证源文件的行为；不引入常驻包缓存。按偏移访问/索引共享仍需独立证明完整性和生命周期收益。
- 64 MiB 两种导出均约 532 MB 分配；P3c 再处理输出缓冲。读取和导出分开验证、提交。
- 句柄缓存命中约 65 ns / 296 B，当前优先处理整包级分配。

## 解释边界

未清空 OS 文件缓存，不能称为冷磁盘启动；MemoryDiagnoser 是每操作托管总分配，不是峰值工作集。尚未测量读取字节数、进程峰值、并发读吞吐或完整游戏导出。样本是合成媒体，单机单次 launch、后台桌面负载和较少迭代可能影响耗时；以固定夹具分配变化为首要依据，耗时仅报告当前环境结果，不据此设置 CI 阈值。不能为了基准跳过整包 hash、降低内容验证或缓存可变文件。
