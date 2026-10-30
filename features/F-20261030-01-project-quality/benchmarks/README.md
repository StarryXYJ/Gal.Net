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

## P3b：provider 输入所有权（2026-10-30）

在 `3b007f4b` 基础上，provider 将其独占的新数组交给 Archive，Archive 用偏移/长度保存数据区，不再复制整块。公共 Deserialize 仍复制数据区。源文件每次打开仍读取并验证全包，没有跨次缓存；非 seek 流仍被完整读取和关闭。

沿用原夹具和运行配置，仅将命令中的 filter 改为 `'*Read*'`、artifacts 改为 `out/quality-p3b-read`。4/4 项成功，退出码 0。原始报告见 [p3b-read.md](p3b-read.md) 和 [p3b-read.csv](p3b-read.csv)；日志 `out/project-quality-p3b-benchmark.log`。以下字节数来自 BDN JSON 的 BytesAllocatedPerOperation。

| 场景 | 均值前 → 后 (ms) | 托管分配前 → 后 (B) | 分配减少 |
| --- | --- | --- | --- |
| 16 MiB / 小条目 | 9.896 → 9.190 | 33,674,768 → 16,874,417 | 49.9% |
| 16 MiB / 四条目 | 46.184 → 49.743 | 151,162,967 → 83,963,393 | 44.5% |
| 64 MiB / 小条目 | 44.296 → 37.887 | 134,344,353 → 67,200,175 | 50.0% |
| 64 MiB / 四条目 | 246.320 → 184.961 | 604,156,283 → 335,656,565 | 44.4% |

分配下降与每次少复制一份包的数据区相符。16 MiB 四条目均值增加 7.7%，两次置信区间有重叠，不能声称所有读取场景都更快。主要收益为减少分配；耗时未做交错 A/B 重复验证。内存索引、缓存命中与导出实现没有改动，本轮不重复测它们；两尺寸六操作自检仍全部通过。

验证：新增 8 个快照/完整性组合，覆盖公开输入后续写入、同步/异步文件打开后源包变化与下次拒绝、恢复正确源后重试、并发读取、取消和条目 hash。Assets 210 项、Architecture 53 项通过；既有非 seek 流所有权/关闭/取消、overlay 测试一并通过。基准 Release 构建零警告/错误；首次编译提示私有方法 CancellationToken 应为末参，调整后复测通过。测试日志为 `out/project-quality-p3b-assets-final.log`、`out/project-quality-p3b-architecture.log`，自检见 `out/project-quality-p3b-verify.log`。

限制：每次 provider 打开仍读取整个包并重建索引；stream provider 仍有累积 MemoryStream 和 ToArray 的成本；公共输入保持复制；本阶段未优化导出，也未增加常驻缓存。整包缓冲现在包含小量表头字节，生命周期仍限定于 archive 的可达时间。P3c 继续处理导出分配。

## P3c：导出缓冲（2026-10-30）

在 `88de0f86` 基础上，把现有格式的准备与写出分开。内部 PreparedArchive 保存表头和有序数据块；内存导出创建准确长度的最终数组，文件导出直接写入 FileStream。复用同一数据准备与输出实现，没有公共 API/格式/压缩算法变化。全部资源读取、长度、内容和元数据校验仍在打开目标文件之前完成，文件访问/共享模式保持写入 + FileShare.Read。

同一夹具、运行环境和 job，filter 为 `'*Export*'`，artifacts 为 `out/quality-p3c-export`。4/4 项成功、退出码 0；[首轮报告](p3c-export.md)、[CSV](p3c-export.csv)。日志 `out/project-quality-p3c-benchmark.log`。字节数取完整 JSON 的 BytesAllocatedPerOperation。

| 场景 | 均值前 → 后 (ms) | 托管分配前 → 后 (B) | 分配减少 |
| --- | --- | --- | --- |
| 16 MiB / byte[] | 64.619 → 83.300 | 132,411,879 → 99,766,003 | 24.7% |
| 16 MiB / 文件 | 78.157 → 78.805 | 132,387,081 → 82,962,249 | 37.3% |
| 64 MiB / byte[] | 265.923 → 238.448 | 531,900,968 → 401,769,437 | 24.5% |
| 64 MiB / 文件 | 285.689 → 251.604 | 531,901,524 → 334,641,112 | 37.1% |

16 MiB 首轮波动较大，追加同配置复测：filter `'*Export*(PayloadMiB: 16)*'`，artifacts `out/quality-p3c-export-repeat`，2/2 项成功、退出码 0。byte[] / 文件均值为 71.16 / 68.85 ms，分配约 95.15 / 79.17 MiB，见 [复测报告](p3c-export-repeat.md)、[CSV](p3c-export-repeat.csv)，日志 `out/project-quality-p3c-benchmark-repeat.log`。两轮都保留；小包 byte[] 仍慢于历史基线，不能宣称普遍加速，也尚未用交错 A/B 排除环境影响。此次以减少整包扩容/复制的分配收益验收，未设置耗时门禁。

验证：新增 13 项回归在旧实现先通过，记录无压缩固定样本 SHA256 后将其作为格式守卫。覆盖四种压缩模式、空包、256/257 字节边界、Unicode、排序/映射、实际 hash、成功覆盖截断，以及重复 ID/元数据不符/缺失映射/源读取失败/长度不符时已有输出保持原样。Assets 223 项、Architecture 53 项、StoryPackageExportTests + ExportPreparationTests 9 项通过；最终调整文件共享方式后再次通过 13 项导出回归。16/64 MiB 六操作自检通过，基准工程零警告/错误。日志：`out/project-quality-p3c-before.log`、`out/project-quality-p3c-assets.log`、`out/project-quality-p3c-architecture.log`、`out/project-quality-p3c-export-final.log`、`out/project-quality-p3c-verify-final.log`。初次集成构建遥测日志权限失败记录在 `out/project-quality-p3c-integration.log`；设置进程环境 AVALONIA_TELEMETRY_OPTOUT=1 后，成功记录在 `out/project-quality-p3c-integration-retry.log`。原有测试分析器警告未处理，本阶段文件没有新增警告。

边界：单条目压缩仍使用原有 MemoryStream + ToArray，所有压缩条目仍保留至写出结束；这里测得的是累计托管分配，不是峰值内存。文件写出中途 I/O 失败仍可能留下不完整目标，与原 API 的非原子写入语义相同；编辑器自己的 staging/publish 机制保持不变。编辑器使用 byte[] 路径可获得最终缓冲收益，本轮未迁移其导出 API，也未测量完整游戏导出的耗时/峰值。原导出 API 无 CancellationToken，本阶段不扩展取消契约。
