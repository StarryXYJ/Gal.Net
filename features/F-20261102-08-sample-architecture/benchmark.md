# Sample 鉴赏基准：首轮基线

测量日期 2026-11-02；BenchmarkDotNet 0.15.8、.NET 10.0.12 x64、SDK 10.0.401，Windows 11 25H2，Intel Core i7-13650HX。每项一次 launch、3 次 warmup、6 次实测、每轮 1 次调用，不改变电源计划。源码为 `6d1bffb7` 上的工作树，包含此前未提交的普通页面样式调整；基准不等于干净 commit 的测量。

## 页面挂载与布局

基准构造 Sample 实际 `CgGalleryPage`、真实卡片模板、配色和样式，在 1280×720 Headless 窗口中 Show/UpdateLayout/Close。夹具预先创建已解锁的条目模型，不读取图片、不创建 Bitmap，也不计算内容加载。每次测量重新创建窗口、页面、根样式和卡片：这包括样式初始化及关闭成本，不等同于真实导航中已存活页面的增量切换时间。`--verify` 在当前非虚拟化实现中分别观察到 100/100 和 1000/1000 个视觉卡片。

| 条目 | 平均 | 标准差 | 每操作托管分配 |
| --- | ---: | ---: | ---: |
| 100 | 231.6 ms | 32.65 ms | 21.76 MiB |
| 1000 | 931.7 ms | 31.88 ms | 222.39 MiB |

2/2 个基准完成，退出码 0。结果见 `output/sample-gallery-baseline/results/`。内存是累计托管分配，不是留存量或峰值；未包含原生图片内存、真实 GPU、用户输入及移动设备。100 条的 99.9% 误差为 ±91.55 ms，单轮结果只作为热点线索，不适合声称优化幅度或建立 CI 阈值。

## 图片解码候选对照

同一随机种子生成的 2560×1440 不透明 PNG，分别用当前 `new Bitmap(path)` 和 `Bitmap.DecodeToWidth(stream, 768)`，都在解码后立即释放。它们的结果尺寸分别是 2560×1440 和 768×432；自检实际验证两者尺寸。文件是高熵合成图，文件缓存在暖态，场景没有测真实项目图片或 GPU 上传。

| 方法 | 平均 | 标准差 | 托管分配 |
| --- | ---: | ---: | ---: |
| 原尺寸 | 17.91 ms | 0.93 ms | 1.21 KiB |
| 768 宽 | 30.49 ms | 2.05 ms | 1.51 KiB |

2/2 完成，退出码 0，报告位于 `output/sample-thumbnail-baseline/results/`。每轮单次操作，BDN 对两项报告了最小迭代时间不足 100 ms；两方法分别在独立进程测量，未做交错 AB/BA。缩小后的像素面积约为原来的 9%，但**没有测量原生峰值或留存内存**；此对照不支持宣称总内存已下降或解码更快，也没有验证各种 DPI 的图像质量。当前不替换生产解码。

## 顺序加载的可控等待

基准调用真实 `GalleryThumbnailCollection.ActivateAsync`，将每个已解锁条目的加载器替换为 `Task.Delay(5)`，无文件或 Bitmap，测量整个 activation 结束时间。自检确认返回前所有加载器均已完成；实际导航代码会在 activation 返回后导航，但本基准不挂载导航或计算页面布局。

| 条目 | 平均 | 标准差 |
| --- | ---: | ---: |
| 8 | 122.7 ms | 0.59 ms |
| 64 | 994.7 ms | 2.04 ms |

2/2 完成，退出码 0，报告位于 `output/sample-gallery-loading-baseline/results/`。尽管请求 5 ms，当前 Windows 计时器和调度使每次等待实测约 15 ms；这是**合成异步等待**，不是图片解码速度或实际首屏延迟。未报告跨线程托管分配。结果说明当前串行结构会累加外部等待，不能用其绝对数字预测真实用户体验。

首次自检在初始化 Avalonia Headless 后直接等待延迟任务，因测试进程没有运行 UI 消息循环而停滞；终止自检后将不需要 UI 的加载自检放到线程池，修正后完成。正式 BDN 加载测量独立进程不初始化 UI。没有改动生产同步上下文。

## 游戏工具栏跨断点调整

夹具挂载实际 Sample `GamePage`，显示工具栏并在 450 与 1280 宽度之间交替调整窗口，单次调用包含 20 次调整；`OperationsPerInvoke=20`，下表按每次调整报告。自检验证窄窗口使用紧凑工具栏、恢复宽度后使用完整工具栏。页面没有加载真实场景或媒体。

| 每次调整平均 | 标准差 | 每次托管分配 |
| ---: | ---: | ---: |
| 360.4 μs | 30.34 μs | 27.82 KiB |

1/1 完成，退出码 0，报告位于 `output/sample-toolbar-baseline/results/`。这是 Headless 页面和布局更新总成本，不是 GPU 帧时间，也不是 `MeasureOverride` 的独占成本；单 launch、6 次样本的 99.9% 误差为 ±85.07 μs，BDN 报告迭代时间不足 100 ms。没有对照实验或真实玩家场景证据，不据此宣称工具栏需要性能优化；不为缩短代码而增加缓存/事件订阅。

## 复现

```powershell
dotnet restore benchmarks/GalNet.Sample.Benchmarks/GalNet.Sample.Benchmarks.csproj --locked-mode
dotnet build benchmarks/GalNet.Sample.Benchmarks/GalNet.Sample.Benchmarks.csproj --no-restore -c Release
dotnet run --project benchmarks/GalNet.Sample.Benchmarks/GalNet.Sample.Benchmarks.csproj -c Release --no-build --no-restore -- --verify
dotnet run --project benchmarks/GalNet.Sample.Benchmarks/GalNet.Sample.Benchmarks.csproj -c Release --no-build --no-restore -- --filter '*GalleryBenchmarks*' --artifacts output/sample-gallery-baseline --exporters json markdown
dotnet run --project benchmarks/GalNet.Sample.Benchmarks/GalNet.Sample.Benchmarks.csproj -c Release --no-build --no-restore -- --filter '*ThumbnailBenchmarks*' --artifacts output/sample-thumbnail-baseline --exporters json markdown
dotnet run --project benchmarks/GalNet.Sample.Benchmarks/GalNet.Sample.Benchmarks.csproj -c Release --no-build --no-restore -- --filter '*ToolbarBenchmarks*' --artifacts output/sample-toolbar-baseline --exporters json markdown
dotnet run --project benchmarks/GalNet.Sample.Benchmarks/GalNet.Sample.Benchmarks.csproj -c Release --no-build --no-restore -- --filter '*GalleryLoadingBenchmarks*' --artifacts output/sample-gallery-loading-baseline --exporters json markdown
```

BenchmarkDotNet 会在工程默认 `bin/` 下构建执行子工程，报告写入 `output/`；入口检查空结果、关键校验错误和失败报告。合成 PNG 暂存在 `output/sample-gallery-fixtures/`，正常清理后删除。测量线程不得与其他构建或运行活动冲突；如实际发生文件占用，按仓库约定停下并由用户决定处理。

## 下一步

分页后的真实 Sample 鉴赏页面挂载（不含媒体解码）：100 项平均 76.72 ms、标准差 5.25 ms；1000 项平均 78.32 ms、标准差 3.82 ms；两者每次托管累计分配 4.91 MiB。自检确认两种规模均创建 12 张卡片。报告在 `output/sample-gallery-pagination/results/`；同为单 launch、6 次测量，最短迭代不足 100 ms。

此前基线的夹具没有加载 Semi 和 Ursa 主题，新夹具为实际页码控件添加了这两种主题，因此数字不是严格同条件 A/B，也不代表真实首屏、缩略图解码或原生内存。图片限宽仍需真实素材、原生峰值和显示质量证据。
