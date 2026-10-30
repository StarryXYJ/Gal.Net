# 组合内容与应用，分发游戏

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

Linux 本地候选归档见[引擎发布](engine-release.md)；macOS `compose` 与签名仍在后续阶段。旧候选中的 CLI 不自动更新，使用包含上述能力的新构建。

## 从应用源码构建并合包

`build-compose` 对显式选择的应用 `.csproj` 执行 SDK 检查、锁定还原、普通自包含发布，再封装标准布局并调用相同的严格 `compose`。它不编译作者项目内容，也不修改旧运行包。需要项目要求的 .NET SDK、原生工具及锁文件；`global.json` 在应用项目目录生效，`--sdk-version` 可额外要求解析结果精确匹配。

```powershell
./Tools/build-compose.ps1 --app-project 'src/Samples/GalNet.Sample.CrossPlatform/GalNet.Sample.CrossPlatform.Desktop/GalNet.Sample.CrossPlatform.Desktop.csproj' `
  --content '导出的pak目录' --runtime-id win-x64 --entry-point GalNet.Sample.CrossPlatform.Desktop.exe `
  --version 1.0.0 --configuration Release --publish-profile WindowsReadyToRun --output MyGame.zip
```

也可直接运行 `GalNet.Editor.Headless[.exe] build-compose ...` 或 `Tools/build-compose.sh`。只接受 win-x64/linux-x64；配置默认 Release，flavor 默认 Player，可传 `--sample-flavor Developer`。`--publish-profile` 是应用项目中已存在的 profile 名称，不接受路径；构建统一关闭 AOT、单文件、裁剪和 R2R，使用普通自包含 apphost。

应用发布输出必须包含显式 `--entry-point` 文件、对应自包含运行时及 Core/Builtins/Project.Contracts 三份契约。封装统一使用 `lib/docs/pak`；Windows 入口为 `lib/<apphost.exe>`，保持 SDK apphost 与托管程序集相邻，Linux 根目录的 sh 入口设置自己的 loader 路径并调用 `lib/<apphost>`。应用需按 application-layout.json 定位内容和可写用户目录，不能硬编码工作目录。不同布局的定制宿主需自行适配后使用预编译 compose，不猜测任意 csproj 的入口。

Linux 另需 `--media-directory <准备好的目录>`，其中包含 native、licenses、media-manifest.json，格式来自 prepare-linux-media.py；`--miniaudio-native <目标动态库>` 可把已编译的音频库交给项目。Windows 构建 Linux 时必须提供目标库，不能使用 Windows native。示例使用 `--runtime-id linux-x64 --entry-point GalNet.Sample.CrossPlatform.Desktop --publish-profile Desktop --miniaudio-native libgalnet_miniaudio.so --media-directory linux-media --output MyGame.tar.gz`。媒体库核对 ELF x64，完整目标系统加载仍需 Linux 实跑。

`--version` 必须与内容导出所用契约版本匹配；修改契约或其版本后需重建契约、重新导出内容，不能通过放宽 hash 校验沿用旧内容。SDK、还原、发布分别保留 stdout/stderr 和退出码，日志目录随 JSON 结果返回；失败不发布最终游戏包，清理自有暂存目录，保留诊断日志。Ctrl+C 停止本次 SDK 进程树并返回 130，普通构建失败返回 5，参数错误返回 2。构建可能产生应用项目正常的 bin/obj 和原生编译缓存，不清除这些项目缓存。

契约身份也受源码字节、构建配置和源码路径上下文影响，不能只看 `--version`。可靠流程是先发布所选应用，把实际发布的 Core/Builtins/Project.Contracts 完整闭包交给 `project create --contracts` 或作者项目，再使用同一源码/配置/版本执行 build-compose。换构建宿主或源码快照后，应重新核对三份 DLL hash；不一致时使用该构建的契约重新导出内容。Windows 与 Linux 原生 SDK 均已有实际源码合包证据，Linux 还验证了遵守上述协议的仓库外独立 csproj；这不承诺任意工程布局或跨宿主重编译工件可互换。

需要联调时，在 `scripts/ci/publish-applications.py` 使用 `--flavors Developer`。Developer 开启游戏调试和启动参数，Player 允许外部内容，Bound 绑定游戏且关闭参数；三种模式均通过正式跨平台封装流程验证，不分发 PDB。见 [应用发布矩阵](../../../scripts/ci/README-applications.md)。

完整版无需用户安装 .NET；系统 DLL、Avalonia/Skia/HarfBuzz 与 VLC 视频插件都是体积来源。R2R 保留 IL 并增加预编译代码，不能按 AOT 的体积预期评估。DLL 数量主要来自 VLC 插件，不能仅按扩展名删除；当前保留 x64 插件能力集合。Editor 与 CLI 先独立发布，再将字节一致的重名文件合并到同一目录，依赖冲突会明确失败，不覆盖另一程序的依赖。

应用封装会核对版本、源码 commit、完整文件哈希、部署方式与契约字节身份，最终使用 `cross-platform-release.py seal` 封存矩阵。具体参数见 [引擎发布](engine-release.md) 与 [应用发布矩阵](../../../scripts/ci/README-applications.md)。归档检查不能代替真实桌面/设备的启动、正常退出、存读档和音视频验收。
