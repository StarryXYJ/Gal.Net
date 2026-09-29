# GalNet

GalNet 是一个基于 .NET 的视觉小说（Galgame）引擎与编辑器项目。它将剧情运行时、资源与内容存储、呈现抽象以及 Avalonia 编辑器分层，以支持可扩展的叙事游戏制作与发布。

项目正处于快速迭代阶段：公开 API、项目格式和编辑器工作流都可能调整。请以仓库中的[开发文档](docs/README.md)和当前代码为准；欢迎通过 issue、讨论和贡献参与完善。

## 环境要求

- .NET SDK `10.0.401`，具体版本见 [`global.json`](global.json)。
- Windows 是当前完整开发和 CI 验证环境；纯逻辑项目可在其他支持 .NET 10 的环境构建。
- Android、Browser 和 iOS Launcher 需要对应 .NET workload；Avalonia 构建还需要允许 BuildServices 访问用户级缓存与许可目录。

## 仓库地图

```text
src/
  Shared/          Core、Runtime、Assets、呈现抽象与内置 primitive
  Presentation/    Avalonia 控件、渲染、游戏页面与默认呈现实现
  Infrastructure/  文件系统存储实现
  Editor/          编辑器协议、共享逻辑、GUI 与 Headless CLI
  Samples/         Avalonia 与 Headless 玩家示例
  Launcher/        共享 Launcher 与 Desktop/Browser/Android/iOS 宿主
test/              按生产边界拆分的 10 个测试项目
docs/              当前 spec、长期 design、ADR 与文档索引
features/          按 feature 保存的需求、设计、阶段计划与总结
scripts/           可直接运行的样例脚本
GameTestCase/      示例工程与 smoke test 内容
```

程序集边界、依赖方向和测试职责见[架构文档](docs/spec/architecture.md)。

## 构建与测试

从仓库根目录执行：

```powershell
dotnet restore GalNet.slnx --locked-mode
dotnet build GalNet.slnx --no-restore --configuration Release
```

测试项目共享生产项目的 `obj` 输出，应顺序执行：

```powershell
Get-ChildItem test -Filter *.csproj -Recurse | ForEach-Object {
    dotnet test $_.FullName --configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

提交前可使用与 CI 相同的格式门禁：

```powershell
dotnet format whitespace GalNet.slnx --no-restore --verify-no-changes
dotnet format style GalNet.slnx --no-restore --verify-no-changes --diagnostics IDE0005
```

完整 solution 包含 Android、Browser 和 iOS 平台宿主。如果本机没有对应 workload，先验证 Desktop、Headless 和测试项目，再在具备平台 SDK 的环境执行完整构建。受限沙箱中 Avalonia BuildServices 也可能因无法访问用户目录而失败，这类环境错误应与源码编译错误分开判断。

## 运行示例

以下脚本默认编译并运行 [`GameTestCase`](GameTestCase/README.md)：

```powershell
.\scripts\run-headless-sample.ps1
.\scripts\run-avalonia-sample.ps1
```

Headless 示例在终端中交互；Avalonia 示例会启动桌面窗口。两个脚本都支持通过 `-Project`、`-BuildOutput` 和 `-SkipBuild` 指定已有内容。

## 开发流程

较大的功能、架构调整和跨阶段重构使用 `features/F-YYYYMMDD-NN-short-slug/` 记录需求、设计、实施计划和验证总结。文档职责与推荐流程见 [Feature 工作流与 Agent 知识维护](docs/design/feature-workflow.md)。

GalNet 采用 [MIT License](LICENSE) 发布，允许用于商业和非商业项目。
