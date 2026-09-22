# .NET 构建在受限环境中应禁用共享编译器与 Avalonia 遥测

状态：candidate
来源 Feature：F-20260922-01-entry-instance-runtime

## 现象

在受限 workspace 中构建 `GeneralTest` 时，默认 Roslyn shared compiler 可能因 named pipe 权限失败；Avalonia build telemetry 还可能尝试写入用户 `AppData` 下的日志而失败。并行构建两个引用同一项目的目标还可能同时写入该依赖项目的 `obj`，产生 `CS2012` 文件占用错误。

## 根因

Roslyn 共享编译器依赖当前 sandbox 不允许访问的进程间 named pipe，Avalonia telemetry 的默认日志位置也不在 workspace 可写根目录内。多个独立 `dotnet build` 进程不会协调共享依赖的中间输出，因此即使各自使用 `-m:1`，并行运行仍可能争用同一个 `obj` 文件。

## 如何发现

默认 `dotnet build` 分别出现 shared compiler `UnauthorizedAccessException` 和 Avalonia BuildServices 日志路径写入错误；禁用共享编译并关闭 Avalonia telemetry 后，同一项目构建成功。并行构建 `GalNet.Core` 与引用它的 `GalNet.Primitives.Builtins` 时复现 `CS2012`，改为串行构建后成功。

## 正确做法

在受限环境验证含 Avalonia 构建任务的项目时设置 `AVALONIA_TELEMETRY_OPTOUT=1`，并向 MSBuild 传入 `-p:UseSharedCompilation=false`。为降低并发构建带来的额外干扰，可使用 `-m:1`。存在项目引用关系或共享依赖时，多个项目的构建命令也应串行执行。

## 适用范围

Codex 受限 workspace 中的 GalNet .NET/Avalonia 构建与测试。

## 不要做什么

不要把 named pipe 或用户目录日志权限失败误判为业务代码编译错误，也不要为绕过该问题修改产品代码。

## 证据和关联文档

- `features/F-20260922-01-entry-instance-runtime/phase-plan.md`
- 成功验证命令：`dotnet build test/GeneralTest/GeneralTest.csproj --no-restore -m:1 -v:minimal -p:UseSharedCompilation=false`
