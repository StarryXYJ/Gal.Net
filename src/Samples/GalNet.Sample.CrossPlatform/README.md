# GalNet cross-platform sample

这是由 Avalonia `avalonia.xplat` 模板生成的独立 sample 子解决方案，包含：

- `GalNet.Sample.CrossPlatform.Desktop`：Windows、Linux、macOS 桌面宿主
- `GalNet.Sample.CrossPlatform.Android`：Android 宿主
- `GalNet.Sample.CrossPlatform.iOS`：iOS 宿主
- `GalNet.Sample.CrossPlatform.Browser`：WebAssembly 宿主

版本基线与仓库保持一致：

- .NET SDK：`10.0.401`（由仓库根目录 `global.json` 固定）
- Avalonia：`12.0.5`
- CommunityToolkit.Mvvm：`8.4.1`

模板包为 `Avalonia.Templates@12.0.5`，跨平台模板短名称为 `avalonia.xplat`。不要直接接受模板更新到更高版本；升级时应先同步根目录的 Avalonia 版本，再重新生成或手工迁移工程。

在仓库根目录执行：

```powershell
dotnet build src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.slnx
dotnet run --project src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop
```

Android 和 iOS 需要对应的 .NET workload 与平台 SDK：

```powershell
dotnet workload restore src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.slnx
dotnet build src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Android
dotnet build src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.iOS
```

当前工程是模板基线，尚未接入现有 `src/Samples/GalNet.Sample.Avalonia` 的游戏启动和内容加载逻辑；平台宿主、移动生命周期和共享 UI 的接入应在后续 feature phase 中完成。
