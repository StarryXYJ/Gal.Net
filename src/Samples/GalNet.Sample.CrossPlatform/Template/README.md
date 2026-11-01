# 独立 GalNet Sample

这里是唯一维护的 Sample 源码及其必要构建附件，没有内置 C# 库源码。应用页面、VM、导航和组合入口位于 `src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform/`；项目契约在相邻的 `src/Samples/GalNet.Sample.Contracts/`，Desktop/Android/iOS 等目录是平台入口。

## 选择依赖版本并构建

在每次还原、构建、发布时指定同一个已可取得的 GalNet 包版本。先配置实际 NuGet 包源；开发候选可使用本地包源和 `--configfile`。不需要编辑 csproj：

```powershell
dotnet restore src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop -p:GalNetPackageVersion=<version> -p:Configuration=Release
dotnet build src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop -c Release -p:GalNetPackageVersion=<version> --no-restore
```

`GalNetUsePackages=true` 由交付根配置提供。`Directory.Packages.props` 将四个 GalNet 包固定到所选版本，其余依赖版本来自 `packaging/Repository.Packages.props`。默认保留源模板的 Debug/Release 行为；`-p:GalNetPlayerMode=Developer` 显式选择带调试入口的开发版，Player/Bound 行为遵循包内既有规则，切换模式后重新 restore，不复用不同图的 `--no-restore` 输出。

## 定制与内容

- 改按钮和工具栏：直接编辑 Views 中的 XAML；改业务动作：编辑对应 ViewModels。
- 加页面：添加 View/VM，再在 SampleCustomization 注册映射和导航。无需新的主题或播放机制。
- 配色、350ms 加载停留和公告时长：编辑 SampleCustomization。单按钮可使用自己的 Avalonia ControlTheme。
- 项目契约重新构建后，用应用实际输出的完整契约集合交给 CLI/Editor，再导出内容。不要混用 Editor 默认项目契约，也不要假设不同 OS 重新编译会逐字节一致。

启动时默认读取应用同级 `pak/`；模板不附带游戏内容。内容准备失败会显示应用错误界面，仍能退出。稳定 profile 与存档由现有宿主负责。

## 平台与文件清单

`template-manifest.json` 记录复制来源、文件哈希及源码是否有未提交修改。目录层级保留用于现有 Import、Branding 和 native 相对路径，移动文件时同步自己的工程配置。

当前默认 NuGet 后端的 miniaudio 资产仅 win-x64；其他桌面目标仍需部署匹配 native 库和私有 VLC。移动入口已附正常 NDK/Apple 编译所需的 miniaudio 源码，但仍需要各平台 SDK/工具链、内容、签名及对应原生依赖。编译或目录存在不表示真机、macOS/iOS、Browser 或原生 Linux 桌面已经验收。

完整规范和历史证据在 [GalNet 源码仓库](https://github.com/StarryXYJ/Gal.Net)。这里不提供模板生成、重命名或自动升级合并机制。
