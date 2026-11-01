# 使用 Windows ReadyToRun 发布

Editor 提供 `WindowsReadyToRun` 发布配置：Release、win-x64、依赖已安装的 .NET 10 运行时、启用 ReadyToRun、关闭裁剪和单文件。它预编译发布程序集以减少启动 JIT，不改变 Debug 或普通 `dotnet run` 的启动方式。

```powershell
dotnet publish src/Editor/GalNet.Editor/GalNet.Editor.csproj -c Release -r win-x64 -p:PublishProfile=WindowsReadyToRun -o out/r2r/Editor
& out/r2r/Editor/GalNet.Editor.exe

```

必须运行发布目录中的程序才能获得预编译收益。发布配置保留完整 IL 和反射元数据，兼容现有 XAML、DI 和项目 schema 加载；不调整渲染后端或窗口效果。

普通命名配置的编译与还原中间结果放在各项目的 `bin/publish/WindowsReadyToRun` 和 `obj/publish/WindowsReadyToRun`。RID 依赖图使用各项目根目录下独立跟踪的 `packages.lock.WindowsReadyToRun.json`，避免改写日常开发的 `packages.lock.json`，同时让全新环境可以执行 locked restore。Editor 默认契约 Content 从同模式的 DefaultContracts 输出复制。显式 Benchmark=true 时继续使用既有 bin/benchmark、obj/benchmark。

CI=true 时仍尊重仓库 locked restore。Editor 的发布依赖闭包使用独立 R2R 锁文件；更新 SDK、RID 图或依赖版本后，需要显式重新生成并提交变更：

```powershell
dotnet restore src/Editor/GalNet.Editor/GalNet.Editor.csproj -r win-x64 -p:PublishProfile=WindowsReadyToRun -p:RestoreLockedMode=false
```

日常发布和 CI 使用 locked restore；解锁只用于有意更新上述锁文件。Restore 输出仍隔离在命名配置的 `obj` 路径中。配置不关闭仓库的 CI 锁定策略。

## 契约字节身份

ReadyToRun 会改写参与转换的 DLL，不能直接把转换后的 DLL 当作原契约。Directory.Build.targets 在 PublishReadyToRun=true 时排除 GalNet.Core.dll、GalNet.Contracts.Builtins.dll、GalNet.Project.Contracts.dll，保留固定契约入口及编译闭包的 IL 字节身份。项目契约和 App 调试匹配规则不放宽。


CrossPlatform Sample 使用普通 Desktop 发布配置，见 [应用发布矩阵](../../../scripts/ci/README-applications.md)。旧 Sample 的性能数据仅保留在 features/F-20261025-01-cpu-startup-benchmark/runtime-profile.md，不代表当前宿主。
