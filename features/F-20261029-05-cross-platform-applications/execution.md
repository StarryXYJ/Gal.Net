# 执行记录

## 2026-10-29：P0 内容/环境检查与 P1 导出改造

### 实现

- Editor 导出面板仅保留 galpak 与内容文件夹；不再发现源码仓库或启动 powershell.exe 发布应用，相关旧选项、配置字段和四语文案删除。
- 目录模式在所选输出目录新建 pak，已存在时拒绝覆盖；两种模式均经过当前项目保存与作者快照捕获，编译在后台完成。
- 唯一 galpak 导出包含本体、localization.pak、game.pak 和全部剧情分片，不生成旁置剧情；目录导出与其逐文件一致。
- installation.json v4 覆盖全部声明载荷。安装器固定 pak/game.pak，拒绝旧索引、缺失/损坏剧情、重复/越界/未声明条目；不保留旧导出/安装分支。
- 重复安装保留已有内容和外置翻译，但仍验证本次源归档 hash。维护者 Publish.ps1 不再从 galpak 邻近目录隐式复制剧情或资源补丁。
- 已同步 content-packaging、file-formats 和 release-packaging 中此次已验证的事实。
- 未改动用户初始化的 CrossPlatform 工程；未安装或升级 WSL 软件。

### 验证

| 检查 | 结果/证据 |
| --- | --- |
| Integration Release build | 通过，0 错误；out/cross-platform-p1-integration-build.log |
| Editor Release build | 通过，0 错误；out/cross-platform-p1-editor-build.log |
| Integration 全量 | 181 项通过；另有 3 项 Explicit 原生/发行测试未执行；out/cross-platform-p1-results/integration-all.trx |
| 新增 Editor 挂载测试 | 压缩包和目录两例通过，覆盖真实面板、仓库外项目、实际导出/安装及目录冲突不覆盖；out/cross-platform-p1-results/editor-content-export.trx |
| Editor 全量 | 354 通过、2 失败，未声明全绿；out/cross-platform-p1-results/editor-all.trx |
| 失败隔离 | 不运行新增导出测试，仅运行两个失败用例仍重现；out/cross-platform-p1-results/editor-existing-failures.trx |
| 自包含 CLI 发布 | Windows win-x64，自包含、关闭 R2R 的候选通过；out/cross-platform-p1-tools、out/cross-platform-p1-cli-publish.log |
| SDK/工具发现隔离 | PATH 仅保留候选工具目录，DOTNET_ROOT/DOTNET_ROOT_X64 指向其自包含运行时，关闭 multilevel lookup；不存在可发现 dotnet 命令时创建项目、export、project export-directory、galpak install 全部通过；out/cross-platform-p1-nosdk-24eae9dd |
| 隔离导出一致性 | 上述独立 CLI 的归档安装与目录导出逐文件 SHA-256 一致；真实剧情分包内容另由 StoryPackageExportTests 覆盖 |
| 文案/脚本 | 四种语言 Export.* 键集合一致；Publish.ps1 PowerShell AST 解析通过；导出 VM/Service 无进程、脚本或 SDK 调用依赖 |
| 文本检查 | git diff --check 通过；Git 提示的 LF/CRLF 转换不是空白错误 |

构建首次遇到 Avalonia telemetry 写入 AppData 权限错误，按已有 lesson 设置 AVALONIA_TELEMETRY_OPTOUT=1 后通过，未改产品代码绕过。初次更新测试夹具时设置默认 JSON 不完整，以及新旧设置字节实际相同，已分别修正为有效配置及真正不同的配置后重新运行集成全量并通过。

### Editor 全量的两处失败

1. FoundationViewsHaveNoUnlistedLiteralUserInterfaceText：ProjectSettingsPanelView.axaml 的 Content="…" 不在测试允许集合中；该按钮存在于 HEAD，此次未修改该页面或对应测试。
2. GeneratedCheckboxIsCenteredInTheEditorColumnAtWideAndNarrowWidths：既有字段复选框中心位置断言失败（预期 258，实际 8）；相关控件与测试此次未修改，独立进程不运行新增导出测试时仍失败。

本次不将这些问题隐藏或改为跳过，保留失败证据供对应 UI 工作处理。

### 状态与待办

- P0：in-progress。内容闭包与本机环境检查完成，见 [环境矩阵](environment-matrix.md)；Ubuntu 24.04 基线、macOS、Android/iOS 与 SDL 探针尚未完成。
- P1：in-progress。实现和相关自动化验证完成，但尚未在干净无 SDK 机器上通过分发 Editor 原生 UI 操作完成导出。自包含 CLI 工具发现隔离与 headless 面板测试不能冒充此原生端到端烟测。
- P2–P8：planned；未实现独立 compose/build-compose、三平台完整媒体发行或五平台模板运行。
- 后续优先补 P1 分发烟测与 P2 布局/无 SDK 合包切片；跨平台项目仍等桌面阶段稳定后在 P6 集中接入。

## 2026-10-29：P2 Windows 布局与严格合包

P1 已提交为 5cc98d23。按用户要求只提交本 feature 内容，CI/release 优化保留在工作区。

### 实现及边界

- docs/application-layout.json v1 声明入口/RID、lib、契约及 pak 位置；发布生成、宿主定位与合包复用，不依赖当前工作目录。部署方式和开发能力复用发行清单。
- compose 接受纯 win-x64 Sample ZIP/目录与 galpak/内容目录，输出新 ZIP；拒绝覆盖、嵌套输出、已有游戏内容、链接、越界/重复条目、未声明文件、错误 hash 或契约身份。
- 内容 contractFiles 捕获实际加载 schema 时的完整闭包并参与 buildId；导出时拒绝已变化的磁盘契约。合包不执行项目代码，只比较闭包字节。手工 catalog 缺身份时不能用于严格合包。
- 自包含契约加载先复用宿主框架程序集；修复 lib 内 System.Text.Json 被私有 resolver 重复加载造成 JsonElement 身份分裂的问题，并严格验证共享 Core/Builtins 字节。
- Editor 分发 Tools/compose.ps1、compose.sh，通过相邻自包含 CLI 工作，无 SDK/源码/编译器调用；不实现 Linux/macOS 归档或 build-compose。
- 真实窗口发现原始 R2R Sample 也会在 SkiaSharp.SKStream.Read / 合成系统字体中 0xC0000005。最小缓解为 win-x64 仅将 SkiaSharp.dll 保持 IL，主程序及其他依赖继续 R2R；发布检查防止误转换。见 L-20261029-12。

### 验证

| 检查 | 结果/证据 |
| --- | --- |
| Editor.Shared 全量 | 494 项通过；out/cross-platform-p2-results/p2-shared-all.trx |
| Integration 全量 | 181 项通过；4 个 Explicit case 未执行（另行执行本次新原生 case）；p2-integration-all.trx |
| Architecture 全量 | 52 项通过；p2-architecture.trx |
| 输入负向与闭包 | 14 个 compose case 覆盖四种输入组合、坏 hash、契约不匹配、无身份、取消、已有输出、未声明载荷和 ZIP 逃逸/重复/链接；包含在 Shared 全量 |
| locked restore | Shared、Headless、Editor.Tests、Integration 普通图、Integration NoGameDebugging、Editor win-x64 WindowsReadyToRun 图通过；锁文件仅增加项目依赖，没有第三方版本升级 |
| SDK 搜索隔离 | 自包含最新 CLI，PATH 仅候选工具/Windows 系统目录；DOTNET_ROOT 指向 bundled runtime；PowerShell 7、Windows PowerShell 5.1 和 Git Bash sh 均合包成功；out/cross-platform-p2-smoke/compose-latest-{ps,ps51,sh}.json |
| 原生对照 | 原始运行包与合包均重现字体崩溃；禁用 R2R 对照通过；只替换同版本 SkiaSharp IL 的诊断副本连续 3 次通过；p2-native-original-1.trx、p2-native-no-r2r-diagnostic.trx、p2-native-skia-il-{1,2,3}.trx |
| 最终真实产物 | 正常 Publish + compose + 解包，未手工替换 DLL；SkiaSharp 为 IL、Sample 主程序集仍为 R2R；out/cross-platform-p2-runtime-fixed、compose-final.json |
| 最终原生启动/退出 | 同一最终包在含空格目录、不同工作目录、SDK 搜索隔离、明确启用 R2R 下连续 3 次通过；核对加载的 coreclr 位于该包 lib，会话初始化后向自有 HWND 正常关闭并得到 exit 0；p2-native-final-{1,2,3}.trx |
| 脚本/文本 | Publish、Test-Release、compose.ps1 AST 通过；git diff --check 通过 |

最终 ZIP：out/cross-platform-p2-smoke/game final.zip；SHA-256 为 432007addb35e17536ccd26725461b324390f3b611a7cca510a3eab7aa32e5df。未修改运行包/内容输入；验证目录与日志均在 out。测试自身使用共享读取活动日志及每次独立 profile，避免日志文件锁和旧就绪标记误判。

初次 Git Bash 在沙箱内创建 signal pipe 被系统拒绝，获准在受支持环境复验通过；未改变脚本绕过。首次原生失败先按隔离条件诊断，后由原始包同样复现与选择性 R2R 对照确定缓解范围，失败日志保留。

### 阶段状态

- P2 verified：Windows 合包切片通过上述验证；验证机安装了 SDK，使用受控搜索路径隔离，不声称使用了全新无 SDK VM。
- P0/P1 仍 in-progress：其他平台探针和无 SDK 分发 Editor 原生 UI 导出尚待补齐。P2 的 Sample 启动不能代替 Editor 操作验收；P1 已知两项既有 UI 测试失败仍保留。
- 开始 P3a 统一 VLC 初始化/会话工厂；P3b 再推进各目标原生部署与完整运行包。P3 整体仍未验收，CrossPlatform 项目继续留待 P6。

## 2026-10-29：P3a 桌面 VLC 初始化与媒体回归

P2 提交为 3898881。P3a 作为 P3 的独立可验证切片完成；P3 整体仍 in-progress。

### 实现

- Media.LibVlc 的 LibVlcOptions / LibVlcFactory 统一绝对路径、私有/系统加载选择、插件环境及版本/失败诊断；首次 native 初始化受进程级锁保护，后续拒绝不同配置，失败信息含 RID/配置路径。不以缺库后自动使用已安装 VLC 掩盖部署问题。
- 剧情视频（含共享 runtime 失败后的回退创建）、Gallery 播放、Gallery 缩略图、Editor 音频预览全部接入同一工厂。DI 默认 TryAdd，保留组合根显式注册；各使用方继续拥有并释放自己的 LibVLC/MediaPlayer。
- Windows 默认从托管入口位置选择 libvlc/<RID>；Linux system loader 仅为开发基线，显式私有目录明确报不支持，等待 P3b 处理启动加载路径。macOS/移动原生部署仍未验收。
- GameView 在已依赖 LibVLCSharp 的媒体适配边界引用 Media.LibVlc；Core/Runtime 不增加平台依赖，架构图/依赖门禁同步。
- Editor 全量发现 P2 严格闭包检查暴露 DefaultContracts 仅部署入口 DLL 的旧缺口。补齐构建/发布的 Core/Builtins，不放宽校验；采用显式 Content 路径保证首次干净构建不会因求值时通配符为空而漏 DLL。已更新 L-20261029-07。

### 验证

| 检查 | 结果/证据 |
| --- | --- |
| Presentation 普通全量 | 302 通过；out/cross-platform-p3a-results/p3a-presentation.trx |
| Architecture | 52 通过；p3a-architecture.trx |
| Integration 普通全量 | 181 通过；p3a-integration.trx |
| Editor 普通全量 | 修复默认契约部署后 354 通过、原有同两项 UI 失败；p3a-editor.trx，不声明全绿 |
| Windows 实际媒体 | 5 项通过，LibVLC 3.0.23：真实 1280x738 视频像素进入场景合成、自然结束/跳过、加载期间隐藏/替换、Gallery 播放/缩略图、独立会话释放及拒绝配置切换；p3a-native-media.trx，out/cross-platform-p3a-video.png |
| Editor 实际音频 | 新建作者项目导入真实 MP3，注入私有工厂，经 Inspector 命令播放、时长回调、暂停、释放通过；p3a-editor-native.trx |
| Sample 分发 | 正常发布、compose、含空格目录解包；SDK 搜索隔离启动，确认 coreclr 与 libvlc.dll 均来自本包、会话就绪后正常关闭 exit 0；p3a-native-application.trx |
| Editor 分发 | Windows 自包含 R2R 发布/CLI 合并成功；DefaultContracts 三份 DLL 完整，Core/Builtins 与宿主 hash 一致；分发 CLI 在 SDK 搜索隔离下用默认契约创建项目、schema 查询通过；out/cross-platform-p3a-default-{create,schema}.json |
| 锁文件 | 普通、Sample NoGameDebugging、Editor WindowsReadyToRun、Sample R2R Player/Developer 图更新并 locked restore 通过；仅项目依赖变化，无第三方升级 |
| 文本 | git diff --check 通过 |

Sample 候选为 out/cross-platform-p3a-game.zip，SHA-256 6fbb86990dc8a3adc03d73c250af0bf5ff94138e17900cac0281acf3517a43d8。Editor 候选位于 out/cross-platform-p3a-editor-runtime/0.1.0-dev.6。它们是当前工作区候选，不宣称精确已提交 tag 发行。

默认契约缺失的首次 Editor 失败日志保存在 out/cross-platform-p3a-editor-before-default-contract-fix.log。该测试未结束时误启动下一次重建，发生 DLL 文件锁；已核实并停止本次自有旧测试进程，随后串行重建/全量完成。此为验证编排错误，没有修改产品规避文件锁；沿用已有“构建/测试不争用输出”经验。

### 下一步与限制

- P3a verified（Windows 配置/媒体切片）；P3b 推进按目标 RID 选择原生载荷、Linux/macOS 构建/发布、应用选择/调试与内容定位。
- WSL 复查仍为 Ubuntu 26.04.1：gcc/tar 与 WSLg 可用，dotnet/cmake/pkg-config 未定位；未安装软件。Ubuntu 24.04 最低基线、macOS 两架构及移动设备证据仍缺。
- P1 的原生分发 Editor UI 两种导出烟测依旧待补；此次实际 Editor 音频使用 headless UI + 真 native 媒体，不能代替原生 Editor 导出操作。
- SDL、build-compose、移动宿主尚未实现。CrossPlatform 工程仍保留原骨架，待 P6 集中同步。

## 2026-10-29：换设备后 Windows 重导出与 Linux 字体修正

用户要求先重导出 Windows Release，再继续开发；macOS 当前无法验证，不阻挡可独立推进的 Windows/Linux 切片。P3b 检查点已在 54c7fa90 中，不沿用旧交接时“尚未提交”的描述。

### Windows 发行

- `Release.ps1 -Version 0.1.0-dev.7 -Target Apps` 完成：两种部署的 Editor、纯 Sample、整合 Sample 以及共用 Contracts，共七个 ZIP，清单/hash/R2R/严格契约与分发 CLI 验证通过。
- 产物为 `out/release/0.1.0-dev.7/`。此候选生成于字体修正之前；字体修正后的 Windows 候选另行生成，不修改已封存文件。
- 字体修正后的 `Release.ps1 -Version 0.1.0-dev.8 -Target Apps` 亦已完成，七个 ZIP 全部通过发行载荷验证；最新可用 Windows 候选为 `out/release/0.1.0-dev.8/`。这是当前工作区的本地候选（dirty=true），不是精确 tag 的公开发行。
- 额外 DesktopSmoke 发现夹具只复制 lib 和 EXE，漏掉 P2 引入的 docs/application-layout.json，导致玩家查找 lib/pak。已修正夹具以保留 docs；原始失败日志为 `out/release-smoke-v7.log`。
- 修正夹具后的两次重试在 CLI 内容导出的临时目录操作时报访问拒绝；短输出目录对照仍复现，不能归因于路径长度，也不能记为原生窗口验收成功。日志为 `out/release-smoke-v7-layout-fixed{,-sandbox}.log`。
- dev.8 的完整 DesktopSmoke 仍遇到导出目录问题（`out/smoke8.log`）。独立诊断宿主捕获 first-chance 异常，将失败定位至 ProjectContentBuilder.BuildPreparedAsync 的 Directory.Move；相同输入六次对照 4 成功、2 失败，见 `out/export-diagnostics/repeat-*.log`，暂未确认具体占用/权限来源，不加入盲目重试掩盖。
- 使用已成功导出的独立内容检查 dev.8 原生启动，沙箱内已通过字体与内容资源加载，随后 profile 文件替换被权限拒绝，见 `out/fontsmoke8/SelfContained/player-stdout.log`。此与 Linux 字形异常不同；不修改产品存储语义来绕过沙箱。
- 同一字体/内容夹具在原生用户环境的 dev.8 自包含 Editor、Sample 启动通过；Sample 会话就绪、60 槽，Editor 加载包内 coreclr，见 `out/fontsmoke8-native/SelfContained/`。这是使用已导出内容的启动检查，不能冒充新的 CLI 导出通过；测试结束按既有 smoke 逻辑终止自有进程，不将其记作正常退出。随后 FrameworkDependent 在创建项目的临时目录重命名时失败，见对应 logs，故整轮 DesktopSmoke 仍未通过。

### Linux 字体与原生启动

- 当前环境是 Ubuntu 24.04.3 / WSLg；CMake 构建 miniaudio、Desktop/linux-x64 自包含发布成功。SDK 恢复首轮因沙箱 DNS 失败，获准联网恢复锁定依赖后发布成功，未放宽锁定图。
- 三份跨平台契约 DLL 与 Windows dev.7 hash 全同，见 `out/cross-platform-p3b-current/contract-identity.json`。首次探针误用默认 Version=1.0.0 的 Linux 构建，已用与 Windows 一致的版本重建，未修改严格校验。
- 修复前原生 Sample 确实创建 `GalNet Avalonia Sample Player` 窗口，初次布局随后抛 `Could not create glyphTypeface`，没有完成会话；见 `smoke/7906174bbbb84a6282889ee268159d09/`。
- 共享 UI 改为 FontFamily.Default，CJK 名称只作字符回退；CreateFontManagerOptions 可由宿主指定默认字体。Sample 使用已分发并注册的 `fonts:Inter#Inter`，避免启动依赖系统安装某个特定字体。
- 同一环境、内容和依赖下，修复后的原生窗口、Session initialized（60 槽）、确认退出及 exit 0 均通过。日志记录完整 scope/root 清理，见 `out/cross-platform-p3b-current/smoke/c889249d038f46f0b57a4cd9113c3e23/`。
- Windows 字体回归 2/2 通过，见 `results/typography-native.trx`；沙箱内 testhost 无响应，核对自有进程后终止，在支持原生字体/管道的环境以 no-build 重跑成功。
- Presentation 普通全量 302/302 通过，见 `results/presentation-native.trx`；Explicit 媒体用例未在本次普通回归执行，不将其计作真实媒体回归通过。
- 启用游戏调试及 DeveloperTools 的 Linux Developer 对照也完成窗口、会话和确认退出 exit 0，见 `smoke/ed4ddb083d4e458abea6e6faa5778cb5/`。当前设备没有重现旧交接中的 Developer 启动阻塞；本次只确认启动，不声称外部调试全流程已经验收。
- Inter 不含中文字形；本机 CJK 显示、外部调试、视频/Gallery/音频全流程、原生 Linux 桌面与正式媒体闭包仍需独立验收。本次修复不能解释旧设备 Developer Sample 在框架初始化前的具体阻塞。

P3b 启动/字体切片已取得上述证据，P3 整体仍 in-progress。macOS 未验收，CrossPlatform 仍按 P6 集中接入，不维护第二套 Sample。

### 用户修订：显式回退统一优先开放字体

用户提出微软雅黑的许可与通用性顾虑后，GameTypography 不再按平台显式选择微软雅黑或苹方，统一优先 Noto Sans CJK SC、Noto Sans SC、Source Han Sans SC / CN。Sample 默认仍为内置 Inter；没有复制或打包 Windows/macOS 系统字体，也没有新增中文字体文件。系统未命中这些回退时仍可自行选择其他已安装字体，因此不声称任何环境都绝不会显示系统专有字体。

已同步 control spec、Themes README 和回归约束。Windows 0.1.0-dev.8 为此项偏好修订之前的候选，未重写既有封存 ZIP；当前源码的回退偏好变更不应误记为已进入该候选。

普通 Presentation 回归 302/302 通过（`out/cross-platform-p3b-current/results/open-font-fallbacks.trx`），包括中文 regular/bold 的字形和布局检查；Explicit 媒体测试不在该数量中。本次未新增或重新发布 Windows ZIP。
