# 本地 CI 与发布

先在 Windows x64 安装 `global.json` 指定的 .NET SDK，以及包含 Windows SDK、C++ x64 编译工具和运行库的 Visual Studio Build Tools。SDK 禁止自动滚动到其他版本，保证本地与 runner 使用同一编译器。

## 本地运行 CI

```powershell
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Coverage
```

入口先检查 SDK 和七项发布保护用例，再执行 locked restore、空白与 using 检查、Release solution 构建、solution 中全部 GalNet 测试项目。restore/build/test 使用同一 Configuration，fixture 工程也列在 solution 内。构建完成后才启动 no-build 测试。测试阶段收集全部项目的失败，最后返回非零；真实设备的 Explicit 测试不属于普通 CI。

可以单独运行：

```powershell
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage Format
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage BuildTest -Coverage
```

默认日志保存在唯一的 `out/ci/<run>/`；`ci-results.json` 记录整体结果、各检查退出码和完整日志路径，`test-results/` 保存 TRX 与可选 coverage。`-OutputRoot` 可指定证据目录。Actions 的 CI workflow 调用相同脚本，分为 Format / BuildTest 两个 job。Windows 的 dotnet format/testhost 需要允许子进程与命名管道通信；受限执行沙箱内的管道拒绝不是源码格式错误。

## 本地生成完整发行文件

```powershell
pwsh -NoProfile -File scripts/release/Release.ps1 -Version 0.1.0-dev.1
```

默认仅在本地生成和验收，不需要 GitHub/NuGet 凭据。已存在的最终版本目录不会被覆盖；试不同候选时增加 dev.N，或指定独立 `-OutputRoot`。

最终目录：

```text
out/release/<version>/
  nuget/                 五个 nupkg
  assets/                七个 ZIP
  SHA256SUMS
  release-index.json
```

构建、打包、验收全部成功后才出现这个最终目录。中间文件与日志保留在同输出根的 `.work/<version>-<runId>/`，失败不会留下可误发布的完整索引。只构建 NuGet 或应用可用 `-Target NuGet` / `-Target Apps`。

五个 NuGet：

| 包 | 内容 |
| --- | --- |
| GalNet.Contracts | Core、内置作者契约 |
| GalNet.Engine | 引擎、默认指令、内容与宿主抽象、调试/诊断 |
| GalNet.Desktop | 默认文件存储、PCM 音频、Miniaudio、LibVLC 托管适配 |
| GalNet.Avalonia | Avalonia 控件、渲染、默认页面、主题与宿主 |
| GalNet.SchemaGenerator | 编译期字段/schema 生成器 |

聚合包保留原 DLL 和命名空间。普通游戏引用 GalNet.Avalonia；自定义契约工程引用 GalNet.Contracts，并显式引用 GalNet.SchemaGenerator（`PrivateAssets="all"`）。所有 GalNet 包使用同一版本，依赖约束为精确版本；第三方依赖通过正常 NuGet 引入，不复制到聚合包里。Miniaudio 的预编译 win-x64 native 资产随 Desktop 包分发，消费者不需要 MSVC。LibVLC 原生分发包仍由宿主按平台选择。

默认打包后，在仓库外临时目录及隔离 NuGet 缓存验证四种消费工程、生成器和原生资产传递，测试期间不借用源码 ProjectReference 或中央版本配置。现有包缓存也可作为第三方依赖的离线 feed。`-SkipPackageValidation` 只适合打包调查，产生的索引不允许公开上传。

Windows x64 应用提供两种部署方式，均保留 R2R：默认完整版附带 .NET；`-framework-dependent` 精简版要求用户安装 Windows x64 **.NET 10 Runtime 的最新修补版本**，不需要 SDK、ASP.NET Core Runtime 或 Windows Desktop Runtime。Avalonia/Skia、Miniaudio 和 VLC 等应用原生依赖仍随两种版本提供。下载地址：https://dotnet.microsoft.com/download/dotnet/10.0 。

共七个 ZIP：

- `GalNet-Editor-<version>-win-x64-r2r.zip`：自包含 Editor，附同目录的 `GalNet.Editor.Headless.exe` 和 `Skills/galnet-project-authoring/`。Editor 与 CLI 共用一份运行时和字节相同的依赖；应保持整个目录完整，不单独移动 EXE。
- `GalNet-Sample-<version>-win-x64-r2r.zip`：不含游戏内容，`Run-Sample.cmd <展开内容目录>` 启动。
- `GalNet-Sample-GameTestCase-<version>-win-x64-r2r.zip`：包含编译并展开的 GameTestCase，运行 `Run-GameTestCase.cmd`。
- `GalNet-Sample-Contracts-<version>.zip`：与 Sample 匹配的 Core、Contracts.Builtins、GalNet.Project.Contracts 三个 IL DLL，给 Editor 项目使用。
- 上述 Editor、纯 Sample、整合 Sample 各有一个 `-win-x64-r2r-framework-dependent.zip` 版本，使用系统安装的 .NET；Contracts ZIP 两种部署共用。

应用 ZIP（包括 Editor 内嵌 CLI）不分发 PDB；构建目录仍保留调试符号，运行日志照常输出。仅保留 VLC 的 win-x64 原生文件，过滤原生 NuGet 附带的 win-x86 / win-arm64 文件。玩家关闭外部调试接入。契约闭包排除 R2R 转换；内容编译直接使用这一份玩家契约，不另外重建一份。Editor 自带的默认契约与 Sample 契约不同，分别放置。

整合 GameTestCase 经过 `project export-directory` 编译：资源在 `Assets/Paks/*.pak`，剧情在 `Graph/groups/*.galgroup`；Graph 索引、配置和本地化 JSON 按运行时格式保留，不是作者项目副本。验收要求非空 PAK、已编译剧情，并拒绝散装素材、rawgalgroup 和作者 Contracts 目录。

完整版无需用户安装 .NET；系统 DLL、Avalonia/Skia/HarfBuzz 与 VLC 视频插件都是体积来源。R2R 保留 IL 并增加预编译代码，不能按 AOT 的体积预期评估。DLL 数量主要来自 VLC 插件，不能仅按扩展名删除；当前保留 x64 插件能力集合。Editor 与 CLI 先独立发布，再将字节一致的重名文件合并到同一目录，依赖冲突会明确失败，不覆盖另一程序的依赖。

每个 ZIP 都有版本/commit/文件哈希清单。流水线还会重新解压、验证哈希、检查 R2R 标记和契约字节身份、核对部署声明及运行时份数，并执行两种分发 CLI 的 help、创建项目和 schema 查询。可单独运行分发验证，`-DesktopSmoke` 额外检查两种 Editor 初始化及实际加载的运行时路径，并验证两种桌面玩家使用展开内容完成初始化（验证机需安装 .NET 10 x64 Runtime）：

```powershell
pwsh -File scripts/release/Test-Release.ps1 -Version 0.1.0-dev.1 `
  -AssetDirectory out/release/0.1.0-dev.1/assets `
  -WorkDirectory out/release-smoke/dev1 -DesktopSmoke
```

桌面 smoke 创建独立临时 profile，启动测试自己的进程，检查 session 初始化日志后终止该进程。它不证明完整剧情、存读档和音视频全流程验收通过。

## AOT 当前状态

Editor 仅发布 R2R。桌面 Sample NativeAOT 尚未通过严格链接和运行验收；`-SampleMode Aot/Both` 会明确失败，不回退为 R2R。可复现试编译：

```powershell
pwsh -File scripts/release/Test-DesktopAot.ps1
```

该诊断构建隔离 RID/AOT 恢复图，不改日常锁文件，最终 ILC/ILLink 警告按错误处理。目前已发现 JSON、反射绑定、主题资源、DI 与第三方裁剪问题。Headless 的 AOT 自检仍见 [NativeAOT 手册](native-aot.md)，不能据此宣称桌面玩家已支持。

## tag、CI 与公开上传

版本 tag 指向已提交 commit：`v1.2.3` 或 `v1.3.0-preview.1`。精确 tag 构建要求工作区干净、tag 指向 HEAD；不会使用最近祖先 tag 给新提交命名。

```powershell
pwsh -File scripts/release/Release.ps1 -Tag v1.2.3
```

Release workflow 在 tag 上先运行同一套 CI，再调用同一个本地发布入口；build job 上传已经验证的文件，publish job 下载后上传，不重新编译。`workflow_dispatch` 只生成候选文件，不公开上传。

公开上传需要 GitHub 权限和名为 `NUGET_API_KEY` 的 NuGet 发布凭据；可传入 Trusted Publishing 获取的短期 key。当前 workflow 从同名 GitHub secret 读取，首次发布前需配置。在本地也可上传已经验收的精确 tag 索引：

```powershell
pwsh -File scripts/release/Push.ps1 -Index out/release/1.2.3/release-index.json
```

Push 先检查索引、全部文件哈希、版本和消费者验收状态；先创建 GitHub draft，按依赖顺序上传 NuGet、上传 ZIP，再公开 draft，preview 标记 prerelease。每个成功步骤记录在 `push-state.json`，失败时使用原文件和原 journal 恢复；不跳过未知重复版本，不覆盖公开包。NuGet/GitHub 之间没有事务，上传中断可能留下部分公开 NuGet 包。上传脚本本身不会创建或推送 tag。

## 兼容原有本地应用入口

`Publish.ps1 -Package Editor/Sample/SampleIntegrated/All -Version 1.2.3` 继续可用，默认仍生成旧式 ZIP 文件名，供 Editor 的仓库内导出窗口使用。可选 `-Deployment FrameworkDependent` 生成精简版，名称追加 `-framework-dependent`；默认 `SelfContained`。整合自定义目录或 galpak 的参数保持原语义；完整五包与新索引流程使用 Release.ps1，默认同时生成两种部署。直接运行旧入口不会产生可供 Push.ps1 使用的封存索引。`Test-Release.ps1 -Deployments SelfContained` 可单独验收完整版，默认验收两种部署。
