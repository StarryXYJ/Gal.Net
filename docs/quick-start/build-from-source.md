# 首次源码构建

先满足[环境要求](requirements.md)，以下均在仓库根目录运行。只想看示例时可以直接使用[运行 Sample 脚本](run-sample.md)。

## 构建并启动 Editor

```powershell
dotnet restore src/Editor/GalNet.Editor/GalNet.Editor.csproj --locked-mode
dotnet build src/Editor/GalNet.Editor/GalNet.Editor.csproj --no-restore
dotnet run --project src/Editor/GalNet.Editor/GalNet.Editor.csproj --no-build
```

Editor 打开后可新建项目或打开已有项目。源码启动需要当前构建部署好默认作者契约；若默认契约不可用，按[契约扩展说明](../manual/extensions/actions-and-contracts.md)准备完整闭包，或使用自带默认契约的 Editor 发行包。

## 构建并查看 CLI 帮助

```powershell
dotnet run --project src/Editor/GalNet.Editor.Headless -- help
dotnet run --project src/Editor/GalNet.Editor.Headless -- help --json
```

成功时返回当前命令帮助。CLI 与 Editor 共享作者服务；完整命令及参数以当前构建的帮助为准。

桌面完整构建、测试和工作负载边界见[开发验证](../manual/development/build-and-test.md)。本页只给出首次构建路径，不代表已运行全部平台与媒体验收。
