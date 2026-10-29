# 使用 Windows ReadyToRun 发布

Editor 和 Avalonia Sample 提供 `WindowsReadyToRun` 发布配置：Release、win-x64、依赖已安装的 .NET 10 运行时、启用 ReadyToRun、关闭裁剪和单文件。它预编译发布程序集以减少启动 JIT，不改变 Debug 或普通 `dotnet run` 的启动方式。

```powershell
dotnet publish src/Editor/GalNet.Editor/GalNet.Editor.csproj -c Release -r win-x64 -p:PublishProfile=WindowsReadyToRun -o out/r2r/Editor
& out/r2r/Editor/GalNet.Editor.exe

dotnet publish src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj -c Release -r win-x64 -p:PublishProfile=WindowsReadyToRun -o out/r2r/Sample
& out/r2r/Sample/GalNet.Sample.Avalonia.exe GameTestCase/Output --profile out/r2r/game-test-profile
```

必须运行发布目录中的程序才能获得预编译收益。发布配置保留完整 IL 和反射元数据，兼容现有 XAML、DI 和项目 schema 加载；不调整渲染后端或窗口效果。

普通命名配置的编译与还原中间结果放在各项目的 `bin/publish/WindowsReadyToRun` 和 `obj/publish/WindowsReadyToRun`。RID 依赖图使用各项目根目录下独立跟踪的 `packages.lock.WindowsReadyToRun.json`，避免改写日常开发的 `packages.lock.json`，同时让全新环境可以执行 locked restore。Editor 默认契约 Content 从同模式的 DefaultContracts 输出复制。显式 Benchmark=true 时继续使用既有 bin/benchmark、obj/benchmark。

CI=true 时仍尊重仓库 locked restore。Editor 和 Sample 的发布依赖闭包各自使用一组 R2R 锁文件；更新 SDK、RID 图或依赖版本后，需要显式重新生成并提交变更：

```powershell
dotnet restore src/Editor/GalNet.Editor/GalNet.Editor.csproj -r win-x64 -p:PublishProfile=WindowsReadyToRun -p:RestoreLockedMode=false
dotnet restore src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj -r win-x64 -p:PublishProfile=WindowsReadyToRun -p:RestoreLockedMode=false
```

日常发布和 CI 使用 locked restore；解锁只用于有意更新上述锁文件。Restore 输出仍隔离在命名配置的 `obj` 路径中。配置不关闭仓库的 CI 锁定策略。

## 契约字节身份

ReadyToRun 会改写参与转换的 DLL，不能直接把转换后的 DLL 当作原契约。Directory.Build.targets 在 PublishReadyToRun=true 时排除 GalNet.Core.dll、GalNet.Contracts.Builtins.dll、GalNet.Project.Contracts.dll，保留固定契约入口及编译闭包的 IL 字节身份。项目契约和 App 调试匹配规则不放宽。

## 已测收益与代价

本机各 10 对交错测试（两版各预热一次），同样的 Release/win-x64/framework-dependent 发布，关闭采样：

| 宿主 | 普通发布 Median | R2R Median | 改善 | P95 普通 → R2R |
| --- | ---: | ---: | ---: | ---: |
| Editor | 1446.65ms | 845.37ms | 41.6% | 1487.32 → 940.01ms |
| Sample | 2214.57ms | 1515.76ms | 31.6% | 2255.08 → 2060.09ms |

两个宿主均 10/10 配对更快。时间口径为 Main 到宿主 ready；Sample 使用预构建内容与完成语言选择的测试 profile。它们是预热缓存下的新进程启动，不代表系统重启后的冷缓存。性能随机器和内容变化，不与早期其他实验的绝对时间拼接比较。

发布目录未压缩体积：Editor 约 421.3 → 452.3 MiB（增加约 31.0 MiB）；Sample 约 416.1 → 439.6 MiB（增加约 23.5 MiB）。预编译也增加发布阶段的工作量，不能把它计作启动收益。此次未量化工作集内存变化。

原始 trace、摘要、身份校验及检查见 features/F-20261025-01-cpu-startup-benchmark/runtime-profile.md。后续使用 [CPU benchmark](cpu-benchmark.md) 对同一发布形式复测。
