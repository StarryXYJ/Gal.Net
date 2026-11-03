# GalNet 游戏项目

根目录的解决方案包含共享应用、项目契约和 Desktop、Android、iOS、Browser 入口。游戏页面、样式和行为位于共享应用中，可以直接修改。

项目通过 NuGet 引用 GalNet，默认版本在根目录 `Directory.Build.props`。先配置能取得该版本的 NuGet 包源，然后还原并构建目标平台；例如：

```powershell
dotnet restore src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop
dotnet build src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop --no-restore
```

模板不附游戏内容。生成并导出内容时，Editor/CLI 和播放器必须使用同一套实际构建的契约程序集，不能只依赖版本号相同的假设。应用默认从同级 `pak/` 加载内容。

Android 和 iOS 默认使用示例应用 ID `org.example.game...`，发布前必须修改为自己的唯一标识，并配置平台 SDK、原生依赖与签名。图标在平台项目和 `src/Branding` 中；标题等界面文案在共享项目的 `Localization/` 中。更改后在目标设备上验证。

Desktop 的默认 NuGet 原生音频资产目前仅支持 Windows x64；其他平台需要匹配的原生库和工具链。iOS 和 Browser 不因包含工程就表示已经在目标设备上验收。

使用详情见 [GalNet 文档](https://github.com/StarryXYJ/Gal.Net)。
