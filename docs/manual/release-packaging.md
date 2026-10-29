# 本地 CI 与发布

先在 Windows x64 安装 `global.json` 指定的 .NET SDK，以及包含 Windows SDK、C++ x64 编译工具和运行库的 Visual Studio Build Tools。SDK 禁止自动滚动到其他版本，保证本地与 runner 使用同一编译器。

## 本地运行 CI

```powershell
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1
```

入口先检查 SDK、十七项发布保护用例与十九项发布发现/过滤器检查，再执行 locked restore、Release 构建、solution 中全部 GalNet 普通测试项目。测试不分块、不按路径选测。桌面构建从 `GalNet.slnx` 自动生成 `.slnf`，保留全部测试和 fixture，排除尚未接入的 `GalNet.Sample.CrossPlatform/` 模板，避免普通 Windows CI 要求 Android/iOS/Browser 工作负载。restore/build/test 使用同一 Configuration；构建完成后才串行启动 no-build 测试，收集全部项目的失败，最后返回非零。真实设备的 Explicit 测试不属于普通 CI。默认 `All` 和 `BuildTest` 均不运行格式检查，格式不作为 PR 或发布门禁。

可以单独运行：

```powershell
# 只检查空白与未使用的 using，不修改文件
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage Format
# 一键修复上述格式问题，会修改源码，运行后检查 diff
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage Format -FixFormat
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage BuildTest -Coverage
```

默认日志保存在唯一的 `out/ci/<run>/`；`ci-results.json` 记录整体结果、源码 commit、整体和每阶段耗时、退出码与完整日志路径，`test-results/` 保存 TRX 与可选 coverage。`-OutputRoot` 可指定证据目录。Actions 的 CI workflow 只在 PR 或手动触发时运行一个 BuildTest job；master 普通合并不重复跑 CI。默认关闭 coverage，手动 Actions 输入或本地 `-Coverage` 可开启；NuGet 依赖按 SDK 和锁文件缓存。后端三平台及 NativeAOT 矩阵保留为手动专项，NativeAOT 需勾选 `native_aot`。`-FixFormat` 只允许搭配 `-Stage Format`，不会在默认 CI 中隐式改写源码。Windows 的 dotnet format/testhost 需要允许子进程与命名管道通信；受限执行沙箱内的管道拒绝不是源码格式错误。

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
- `GalNet-Sample-<version>-win-x64-r2r.zip`：不含游戏内容，将内容放入同级 `pak/` 后双击 `GalNet.Sample.Avalonia.exe`。
- `GalNet-Sample-GameTestCase-<version>-win-x64-r2r.zip`：包含位于 `pak/` 的编译 GameTestCase，双击 `GalNet.Sample.Avalonia.exe`。
- `GalNet-Sample-Contracts-<version>.zip`：与 Sample 匹配的 Core、Contracts.Builtins、GalNet.Project.Contracts 三个 IL DLL，给 Editor 项目使用。
- 上述 Editor、纯 Sample、整合 Sample 各有一个 `-win-x64-r2r-framework-dependent.zip` 版本，使用系统安装的 .NET；Contracts ZIP 两种部署共用。

应用根只保留入口 EXE 与目录：全部托管依赖、runtimeconfig/deps、原生依赖及自包含运行时位于 `lib/`；`docs/` 保存 README、LICENSE 和 release-manifest.json；Editor 的作者 Skill 保留根 `Skills/`，默认作者契约位于 `lib/DefaultContracts/`。SDK 生成的原生 apphost 直接定位 `lib/<app>.dll`，仍是单个应用进程。Contracts ZIP 是作者 DLL 部署材料，保留原有平铺布局。

默认应用 ZIP（包括 Editor 内嵌 CLI）不分发 PDB；构建目录仍保留调试符号，运行日志照常输出。仅保留 VLC 的 win-x64 原生文件，过滤原生 NuGet 附带的 win-x86 / win-arm64 文件。玩家关闭外部调试与启动参数。契约闭包排除 R2R 转换；内容编译直接使用这一份玩家契约，不另外重建一份。Editor 自带的默认契约与 Sample 契约不同，分别放置。

win-x64 发布还将 SkiaSharp 3.119.4 保持原始 IL：当前 .NET 10 R2R 组合在合成系统字体时可触发原生读取崩溃。应用及其他依赖仍使用 R2R；发布验收检查此排除项，并要求真实窗口验证，不能仅凭 R2R 头存在判断可运行。

整合 GameTestCase 经过 `project export-directory` 编译：Graph、Group 和资源均在 `pak/Assets/Paks/*.pak`，剧情分片位于 `pak/StoryPatches/<buildId>/`；内部清单、配置、人物和 Gallery 封装为 `pak/game.pak`，内置文本为 `pak/localization.pak`。验收通过分发 CLI 的 `game inspect <content-directory>` 读取 content 版本 3 身份，并拒绝散装 JSON、loose Graph、散装素材、rawgalgroup 和作者 Contracts 目录。`-Galpak` 仅接受内置全部剧情分片的完整 v4 包，安装到同一 `pak/`，不再收集归档旁的剧情目录或资源补丁。资源覆盖补丁需要显式部署到安装内容的 ResourcePatches。

完整组合目录如下；没有补丁时仍保留扩展目录，纯 Sample 也提供空 pak/I18n、pak/ResourcePatches、pak/StoryPatches（没有游戏就没有 buildId 子目录）：

```text
Game/
  GalNet.Sample.Avalonia.exe
  lib/                         DLL、原生依赖与运行时
  docs/                        说明、许可与发行清单
  pak/
    game.pak
    localization.pak
    Assets/Paks/*.pak
    StoryPatches/<buildId>/     可选剧情分片，缺省可为空
    ResourcePatches/            资源覆盖包，缺省为空
    I18n/                      外置翻译覆盖，缺省为空
```

Sample 默认使用用户 `LocalApplicationData/GalNet/<project identity digest>/` 保存玩家数据和日志，安装目录无需可写权限。profile 不随安装目录或 buildId 改变；旧存档位置不自动迁移。开发版可用 `--profile` 显式指定。

## 无 SDK 合并内容与运行包

发行 Editor 的自包含 CLI 提供 `compose`，`Tools/compose.ps1` 和 `Tools/compose.sh` 转发同一组参数。它们通过相邻 CLI 工作，无需源码仓库或 dotnet SDK；脚本需保留在发行包中的原位置。PowerShell 入口支持 Windows PowerShell 5.1 与 PowerShell 7，sh 入口可由 Git Bash 调用。

```powershell
./Tools/compose.ps1 --runtime 'GalNet-Sample.zip' --content 'MyGame.galpak' --output 'MyGame.zip'
# 也可直接调用 CLI
./GalNet.Editor.Headless.exe compose --runtime 'Sample目录' --content '导出的pak目录' --output 'MyGame.zip'
```

接受带布局元数据的纯 `win-x64` Sample ZIP/目录或 `linux-x64` Sample tar.gz/目录，以及完整 v4 galpak/内容目录；Windows 输出新 ZIP，Linux 输出新 tar.gz。已有输出、已有游戏内容的运行包、损坏或未声明的文件、越界路径、软/硬链接及特殊设备条目、契约不匹配均拒绝。输入不被修改，最终清单重建为 `Sample-Game`，包含内容 projectId/buildId 与所有发行文件 hash。内容目录中显式部署的 I18n JSON 与 ResourcePatches PAK 随包复制；这些覆盖内容仍由运行时执行语义校验。

```sh
./Tools/compose.sh --runtime 'GalNet-Sample-linux-x64.tar.gz' --content 'MyGame.galpak' --output 'MyGame.tar.gz'
```

Linux tar 可包含一个顶层应用目录，也可直接使用应用根；输出包含以输出文件名命名的顶层目录，并保留执行权限与空目录。普通文件/目录只接受 rwx 权限，拒绝 setuid/setgid/sticky。Linux 入口须为 x64 ELF，或以 `#!/bin/sh` 开头、由发行清单 nativeEntryPoint 指向包内 x64 ELF 的启动脚本；两者都须有用户执行位。Linux 目录输入需要 Unix 宿主才能读取实际权限；在 Windows 合并 Linux 包时使用 tar.gz 输入，避免猜测执行位。

应用发布生成 `docs/application-layout.json`（schemaVersion 1），记录 runtimeIdentifier、entryPoint、managedDirectory、contractsDirectory、contentDirectory。所有位置相对发行根，内容不得与程序/契约/清单位置重叠。Sample 从此布局定位默认 pak，与工作目录无关。部署方式与开发能力继续由 release-manifest.json 的 selfContained/sampleFlavor 声明。

内容导出的 `contractFiles` 记录加载该项目 schema 时的完整契约闭包字节身份，并参与 buildId。重新导出前若磁盘契约与已加载 catalog 不一致则拒绝，需重新加载项目；合包逐项核对运行包中同名 DLL 的大小/hash。普通编辑器默认契约和 Sample 契约并不相同，创作项目应使用目标运行包的契约。低层 API 手工构造且没有契约身份的 catalog 可以导出，但其内容不能用于严格合包。

Linux 本地候选归档见下文；macOS `compose` 与签名仍在后续阶段。旧候选中的 CLI 不自动更新，使用包含上述能力的新构建。

## 从应用源码构建并合包

`build-compose` 对显式选择的应用 `.csproj` 执行 SDK 检查、锁定还原、普通自包含发布，再封装标准布局并调用相同的严格 `compose`。它不编译作者项目内容，也不修改旧运行包。需要项目要求的 .NET SDK、原生工具及锁文件；`global.json` 在应用项目目录生效，`--sdk-version` 可额外要求解析结果精确匹配。

```powershell
./Tools/build-compose.ps1 --app-project 'src/Samples/GalNet.Sample.Avalonia/GalNet.Sample.Avalonia.csproj' `
  --content '导出的pak目录' --runtime-id win-x64 --entry-point GalNet.Sample.Avalonia.exe `
  --version 1.0.0 --configuration Release --publish-profile WindowsReadyToRun --output MyGame.zip
```

也可直接运行 `GalNet.Editor.Headless[.exe] build-compose ...` 或 `Tools/build-compose.sh`。只接受 win-x64/linux-x64；配置默认 Release，flavor 默认 Player，可传 `--sample-flavor Developer`。`--publish-profile` 是应用项目中已存在的 profile 名称，不接受路径；构建统一关闭 AOT、单文件、裁剪和 R2R，使用普通自包含 apphost。

应用发布输出必须包含显式 `--entry-point` 文件、对应自包含运行时及 Core/Builtins/Project.Contracts 三份契约。封装统一使用 `lib/docs/pak`；Windows 入口为 `lib/<apphost.exe>`，保持 SDK apphost 与托管程序集相邻，Linux 根目录的 sh 入口设置自己的 loader 路径并调用 `lib/<apphost>`。应用需按 application-layout.json 定位内容和可写用户目录，不能硬编码工作目录。不同布局的定制宿主需自行适配后使用预编译 compose，不猜测任意 csproj 的入口。

Linux 另需 `--media-directory <准备好的目录>`，其中包含 native、licenses、media-manifest.json，格式来自 prepare-linux-media.py；`--miniaudio-native <目标动态库>` 可把已编译的音频库交给项目。Windows 构建 Linux 时必须提供目标库，不能使用 Windows native。示例使用 `--runtime-id linux-x64 --entry-point GalNet.Sample.Avalonia --publish-profile Desktop --miniaudio-native libgalnet_miniaudio.so --media-directory linux-media --output MyGame.tar.gz`。媒体库核对 ELF x64，完整目标系统加载仍需 Linux 实跑。

`--version` 必须与内容导出所用契约版本匹配；修改契约或其版本后需重建契约、重新导出内容，不能通过放宽 hash 校验沿用旧内容。SDK、还原、发布分别保留 stdout/stderr 和退出码，日志目录随 JSON 结果返回；失败不发布最终游戏包，清理自有暂存目录，保留诊断日志。Ctrl+C 停止本次 SDK 进程树并返回 130，普通构建失败返回 5，参数错误返回 2。构建可能产生应用项目正常的 bin/obj 和原生编译缓存，不清除这些项目缓存。

需要联调时，`Publish.ps1` 或 `Release.ps1` 加 `-SampleFlavor Developer`：Sample 仍使用 Release 优化，开启 `GALNET_GAME_DEBUG` 与 `GALNET_LAUNCH_ARGS`，保留 PDB，ZIP 名追加 `-developer`，发行清单记录 sampleFlavor。默认 `Player`。`Test-Release.ps1` 验收开发包也传入相同选项，Editor/Contracts 名称不变。

完整版无需用户安装 .NET；系统 DLL、Avalonia/Skia/HarfBuzz 与 VLC 视频插件都是体积来源。R2R 保留 IL 并增加预编译代码，不能按 AOT 的体积预期评估。DLL 数量主要来自 VLC 插件，不能仅按扩展名删除；当前保留 x64 插件能力集合。Editor 与 CLI 先独立发布，再将字节一致的重名文件合并到同一目录，依赖冲突会明确失败，不覆盖另一程序的依赖。

每个 ZIP 都有版本/commit/文件哈希清单（应用在 `docs/`，Contracts 在根）。流水线还会重新解压、验证哈希、检查 R2R 标记和契约字节身份、核对部署声明及运行时份数，并执行两种分发 CLI 的 help、创建项目和 schema 查询。可单独运行分发验证，`-DesktopSmoke` 额外检查两种 Editor 初始化及实际加载的运行时路径，并验证玩家在含空格路径、不同工作目录下无参数加载 `pak/`（验证机需安装 .NET 10 x64 Runtime）：

```powershell
pwsh -File scripts/release/Test-Release.ps1 -Version 0.1.0-dev.1 `
  -AssetDirectory out/release/0.1.0-dev.1/assets `
  -WorkDirectory out/release-smoke/dev1 -DesktopSmoke
```

桌面 smoke 使用分发 CLI 创建并导出唯一项目，复制分发玩家到测试目录，通过其真实默认策略创建唯一用户 profile，准备已完成首次启动的计数。检查 session 初始化日志后终止测试自己的进程，保留日志副本并清理该唯一 profile；不借用现有游戏数据。它验证播放器和目录策略，整合内容另作清单/PAK 检查，不证明完整 GameTestCase 剧情、存读档和音视频全流程验收通过。

## Linux 本地候选（Ubuntu 24.04 x64）

在 Windows 构建机上，用预先编译的 Linux miniaudio 动态库发布三个自包含程序；不需要在目标机器安装 .NET：

```powershell
pwsh -File scripts/release/Publish-Linux.ps1 -Version 0.1.0-dev.9 `
  -MiniaudioNativePath out/native-linux/libgalnet_miniaudio.so `
  -OutputDirectory out/linux-raw -SampleFlavor Developer
```

随后在 Ubuntu 24.04 x64（可用 WSL）运行，路径使用 Linux 形式：

```sh
python3 scripts/release/prepare-linux-media.py --work-directory out/linux-deps --output-directory out/linux-media
python3 scripts/release/package-linux.py --publish-directory out/linux-raw --media-directory out/linux-media --output-directory out/linux-candidate
python3 scripts/release/test-linux-packaging.py
```

媒体准备使用该 Ubuntu 的 apt 源，在独立目录解析/下载依赖并核对索引中的 SHA256，不安装系统软件；需要网络。打包输出目录必须尚不存在。可选 `--content <完整内容目录>` 生成整合 Sample，核对 PAK 和目标 Sample 的契约字节身份。

输出包含 Editor/CLI、纯 Sample、可选整合 Sample 的 tar.gz、Contracts ZIP、SHA256SUMS 与 linux-release-index.json。用 `tar -xzf` 在 Linux 解包后执行根目录的 `GalNet.Editor`、`GalNet.Editor.Headless` 或 `GalNet.Sample.Avalonia`；启动脚本设置包内媒体 loader/plugin 路径，实际程序位于 lib。保留完整目录及执行权限，不直接启动 lib 中的 apphost。WSLg 检测到 `/dev/dxg` 且调用者没有指定 `GALLIUM_DRIVER` 时选择 d3d12。

这套入口生成本地候选，尚未接入公开 Release/Push。宿主仍需 glibc、ICU、显示/图形驱动、CJK 字体及音频服务；不会打包构建机 GPU 驱动。包内保留 Ubuntu 依赖版本、来源、hash 与版权文件，公开再分发前需审查许可和对应源码可用性。原生 Linux 桌面、其他发行版与 macOS 的验收独立进行。

.NET 的可选 libcoreclrtraceptprovider.so 随运行时保留，只核对架构，不作为游戏媒体闭包的必需依赖；当前基线缺少它所需的旧 liblttng-ust.so.0，未验收 LTTng 系统追踪。相关诊断的额外前置条件见 [微软 PerfCollect 文档](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/trace-perfcollect-lttng)。普通程序与媒体运行不使用该追踪入口。

## AOT 当前状态

Editor 仅发布 R2R。桌面 Sample NativeAOT 尚未通过严格链接和运行验收；`-SampleMode Aot/Both` 会明确失败，不回退为 R2R。可复现试编译：

```powershell
pwsh -File scripts/release/Test-DesktopAot.ps1
```

该诊断构建隔离 RID/AOT 恢复图，不改日常锁文件，最终 ILC/ILLink 警告按错误处理。目前已发现 JSON、反射绑定、主题资源、DI 与第三方裁剪问题。Headless 的 AOT 自检仍见 [NativeAOT 手册](native-aot.md)，不能据此宣称桌面玩家已支持。

## tag、CI 与公开上传

版本 tag 指向已提交 commit：`v1.2.3` 或 `v1.3.0-preview.1`。CI 自动发布同时要求 tag 指向的提交已经包含在主分支 `origin/master` 的历史中；允许为主分支历史提交发版，不要求它仍是最新 HEAD。支持先合并再推 tag，也支持先推 tag 再合并：未合并 tag 暂不构建，master 更新时自动发现这次新合入历史的版本 tag，包含源分支提交上的 tag。没有新版本 tag 的普通合并不发布。

先 tag 后合并应使用保留源提交身份的 merge/fast-forward；squash/rebase 会产生新提交，原 tag 不会自动移动，这种方式应在最终 master 提交上打 tag。CI 不移动或重建版本 tag。

精确 tag 构建要求工作区干净、tag 指向 HEAD；不会使用最近祖先 tag 给新提交命名。

```powershell
pwsh -File scripts/release/Release.ps1 -Tag v1.2.3
```

Release workflow 监听 master 与版本 tag push；发现候选后调用 `release-build.yml`，按 tag 串行执行完整构建和发布，已公开版本直接跳过，未完成 draft 要求用原产物与 journal 恢复。checkout 固定为发现时的 tag commit，并校验精确 tag 与 master 归属；随后跑完整 CI，再由打包层独立构建包清单中带正式版本号的 NuGet 库项目，不重复构建测试项目。普通测试输出可能被后续构建覆盖，不能直接当作正式版本产物。应用不同 RID/部署图仍分别 restore/publish，包消费者和 ZIP 验证保留。build 上传封存目录，publish 下载后上传，不重新编译；ZIP/nupkg artifact 不再重复压缩。`workflow_dispatch` 只生成候选文件，不公开上传，也不要求候选分支已合并。

公开上传需要 GitHub 权限和名为 `NUGET_API_KEY` 的 NuGet 发布凭据；可传入 Trusted Publishing 获取的短期 key。当前 workflow 从同名 GitHub secret 读取，首次发布前需配置。在本地也可上传已经验收的精确 tag 索引：

```powershell
pwsh -File scripts/release/Push.ps1 -Index out/release/1.2.3/release-index.json
```

Push 先检查索引、全部文件哈希、版本和消费者验收状态；GitHub 目标还在创建 draft 前检查七个发行 ZIP 是否齐全。随后创建 GitHub draft，按依赖顺序上传 NuGet、上传 ZIP，再公开 draft，preview 标记 prerelease。每个成功步骤记录在 `push-state.json`，失败时使用原文件和原 journal 恢复；不跳过未知重复版本，不覆盖公开包。NuGet/GitHub 之间没有事务，上传中断可能留下部分公开 NuGet 包。上传脚本本身不会创建或推送 tag。

## 维护者本地应用入口

`Publish.ps1 -Package Editor/Sample/SampleIntegrated/All -Version 1.2.3` 用于维护者本地应用构建，不再从 Editor 内容导出窗口调用。可选 `-Deployment FrameworkDependent` 生成精简版，名称追加 `-framework-dependent`；默认 `SelfContained`。`-Galpak` 使用完整 v4 归档。完整 NuGet/应用组合与索引流程使用 Release.ps1，默认同时生成两种部署。直接运行 Publish.ps1 不会产生可供 Push.ps1 使用的封存索引。`Test-Release.ps1 -Deployments SelfContained` 可单独验收完整版，默认验收两种部署。

### SDL3 桌面输入部署

`GalNet.Input.Sdl3` 使用自己的窄源生成 C ABI 绑定，原生资产由固定 `SDL3-CS.Windows/Linux/MacOS` 3.4.18.1 包提供（SDL 3.4.18），不引用其托管主循环。依赖锁定内容哈希；`ThirdParty/SDL3/native-runtime.json` 记录 native 版本、包来源/提交和各 RID 原生 SHA256。该清单是资产来源记录，不能用所列 RID 推断已经完成平台或物理设备验收。

普通 SDK 输出将私有库放在运行目录或 `runtimes/<rid>/native`；数据源只搜索这两个应用私有位置，不回退到系统 SDL。维护者 Windows/Linux 封装及源码 `build-compose` 将第三方清单和两份许可移动至 `docs/ThirdParty/SDL3/`；对应库仍在 lib。GalNet.Desktop/Avalonia 聚合包通过传递 native 依赖和 buildTransitive notice 内容保留同样信息，分发时应一起交付。

GameHost 绑定窗口时才初始化 SDL，解绑/关闭释放自有租约。设备输入只启用 gamepad/joystick/events，不创建 SDL 游戏窗口。缺库、版本错误或外部 input owner 冲突会记录原因并使手柄不可用，键鼠仍可用。定制宿主应在 UI/main 线程创建、更新、释放 `IStandardGamepadSource`，并将快照交给 GameView 的 `GamepadInputSession`；其他 SDL 输入消费者需要显式协调所有权，不能同时独立消费同一输入事件范围。

Windows/WSLg 已验证正式适配的虚拟设备、焦点、断连、生命周期及缺库降级。物理标准手柄、原生 Linux 桌面、macOS 和移动初始化继续保留未验收；不会以包内存在 dylib 或虚拟设备成功代替这些验收。
