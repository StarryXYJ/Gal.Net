# P4a：编辑器内容与恢复基线

## 测量口径

基准工程：`benchmarks/GalNet.Editor.Benchmarks`，BenchmarkDotNet 0.15.8，.NET 10，1 launch / 3 warmup / 6 measured iterations，保持机器电源计划。生产实现为 P3c 后未改动的 Editor.Shared/Editor.Abstraction。基准只引用 Shared，不依赖 Avalonia 窗口。

夹具规模为 1,000 / 10,000 条 Entry，20 个 Group，每条两个 JSON 参数（含嵌套未知字段）。8 个受管理文本文件合计 1,024,000 / 10,240,000 字节。创建真实空工程和 session，再在内存中装入合成作者文档；初始化/打开项目/建立基线/清理不计入测量。文本内容固定为 ASCII a，不使用图片/视频载荷。此夹具测作者文档而非运行时资源或 PAK。

| 方法 | 覆盖范围 |
| --- | --- |
| DirtyClean | 全部内容与保存基线相同，读取 IsDirty |
| DirtyLastEntry | 直接修改最后一个 Group 的末尾 Entry 参数，读取 dirty 后还原 |
| DirtyLastTextByte | 直接修改最后一个受管理文件的末字节，读取 dirty 后还原 |
| CaptureWorking | 克隆 Graph、Group、设置和文本字节，得到独立作者快照 |
| SerializeSnapshot | 对已捕获快照按 recovery 的 JSON 选项序列化；不包含克隆 |
| RecoveryUnchanged | 对已经持久化过的同一快照调用 store；仍含序列化/hash，但应跳过写盘 |
| RecoveryChanged | 每次改变 Graph.Name，store 完成序列化/hash/封装/写盘/裁剪，不含快照克隆 |
| RecoveryThroughSession | 通过实际 ReportError 捕获最新工作版并持久化，包含克隆、序列化、hash、封装、写盘、裁剪及 session 通知 |

自检覆盖 dirty 直接晚修改/还原、快照参数和文本与工作版隔离、相同快照不重复写、恢复载荷逐字节往返、session 恢复包含当前 revision。每项 BDN 独立 setup，恢复存储按默认 3 份循环裁剪；定时器设为 1 天避免干扰。计时发生在基准调用线程，未挂载 GUI、订阅者或真实 history capture guard。

## 复现

```powershell
dotnet restore benchmarks/GalNet.Editor.Benchmarks/GalNet.Editor.Benchmarks.csproj --locked-mode --disable-parallel -m:1 -nr:false
dotnet build benchmarks/GalNet.Editor.Benchmarks/GalNet.Editor.Benchmarks.csproj --no-restore -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project benchmarks/GalNet.Editor.Benchmarks/GalNet.Editor.Benchmarks.csproj -c Release --no-build --no-restore -- --verify
dotnet run --project benchmarks/GalNet.Editor.Benchmarks/GalNet.Editor.Benchmarks.csproj -c Release --no-build --no-restore -- --filter '*' --artifacts out/quality-p4a-editor --exporters json markdown
```

入口检查空 Summary、验证错误、空 Reports 和 report.Success；必须另核对 16 项均有有效结果。首次夹具遗漏独立 store 根目录，导致自检 DirectoryNotFoundException；修正 setup 创建目录后两规模通过。失败进程退出期间曾占用 apphost，后续确认退出并重新构建成功。保留日志 `out/project-quality-p4a-verify.log`、`out/project-quality-p4a-build-final.log`；有效自检 `out/project-quality-p4a-verify-final.log`，有效构建 `out/project-quality-p4a-build-verified.log`。

## 设计约束与下一步

完整基线见 [editor-baseline.md](editor-baseline.md) 与 [CSV](editor-baseline.csv)，16/16 项成功，进程退出码 0；日志 `out/project-quality-p4a-benchmark.log`，完整 JSON 在 `out/quality-p4a-editor/results/`。环境为 Windows 11 24H2 / Intel Core Ultra 7 265K / SDK 10.0.401 / runtime 10.0.12。最终构建零警告/错误、locked restore 通过；首次构建的已有生产分析器警告未修改。

10k 场景：dirty clean 2.815 ms / 625.74 KiB，快照克隆 5.607 ms / 14.05 MiB，序列化 8.903 ms / 20.52 MiB，未变恢复检查 13.427 ms / 23.71 MiB，变更 store 恢复 36.219 ms / 59.57 MiB，session 完整恢复 42.528 ms / 67.81 MiB。各方法在独立 case 中测量，不把差值当作精确步骤剖析。内存数字是每操作累计分配。

dirty 分配从 1k 的 63.24 KiB 增加至 10k 的 625.74 KiB，约每 Entry 64 B。源码中 Entry 参数通过 IReadOnlyDictionary 枚举，需要优先验证是否为装箱分配；P4b 先做这个可独立验收的小改动，仍保持完整内容比较。原后台持久化工作顺延为 P4c。

- 当前 IsDirty 用结构比较，不能仅依据 history 游标/事件缓存结果；公开可变 DTO 的直接修改也必须被识别。见 ProjectContentSessionTests.DirtyComparisonDetectsDirectLateMutationsAndIgnoresDisplayNumbers。
- 当前定时恢复在 history capture guard 内执行完整同步 CaptureRecovery；一致性保护应覆盖读取活文档/克隆，后台持久化只能操作已脱离活文档的快照。
- 保存失败后恢复材料已存在是既有测试保证；外部重载显式丢弃前也先捕获。后台化不能悄悄把这些路径改成任意丢弃/合并的任务。
- 后续 P4c 需要区分可合并的定时请求和必须完成的恢复边界；串行持久化、旧完成不覆盖新状态、失败可观察、关闭时停止新请求并排空。不要让 worker 等待 UI 时 UI 又同步等待 worker。
- 本阶段只测量，不修改恢复格式、dirty 比较、作者保存或关闭行为。正式文档仍由用户另行整理。

## 测量限制

这是合成作者文档的稳态测量，操作系统文件缓存未清空；没有真实窗口主线程轨迹、输入延迟、机械盘或完整项目关闭测量。MemoryDiagnoser 是累计托管分配而非峰值；同步方法耗时仅说明其调用线程被占用的时间，不能直接声称用户可见卡顿时长。单机少量迭代不用于 CI 时间阈值。未测的并发、保存/撤销交错、外部重载和关闭排空属于 P4c 正确性验收，不在本基准中冒充已验证。
