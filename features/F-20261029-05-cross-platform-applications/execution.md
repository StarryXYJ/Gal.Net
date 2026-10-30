# 执行记录

本文件保留详细历史与失败证据；当前结果见[阶段总结](progress-summary.md)，下一步见[剩余计划](phase-plan.md)。

## 2026-10-30：验收后移与 Linux 原生源码合包

用户决定手柄、macOS、iOS 验收移到后续，不阻挡共享宿主/Desktop/Android；移动允许 Mono AOT，不强制 NativeAOT。需求、设计与 P6 前置条件已修订，延期不记为通过。

当前设备回到 Ubuntu 26.04.1 / WSLg。下载官方 SDK 10.0.401 并核对发布元数据 SHA512，隔离到 `/tmp/galnet-build-compose-Q4qkZh/sdk`；源码为 `774b35f7` 的 git archive，无 Windows bin/obj 和本轮源码补丁，来源 hash 记录到 provenance.json。初次 SDK 启动缺 ICU，改用工作区既有的 ICU 动态库恢复正常，没有安装系统软件或启用 invariant globalization。

- 原生 SDK 发布 Editor、CLI、Sample Developer 全部通过，继续使用 Desktop/linux-x64 已提交锁图。
- 从本轮实际 Sample 发布捕获三份契约，SDK 搜索隔离下创建作者项目并导出 galpak/目录；两个输入分别在原生 SDK 下完成 build-compose（SDK/locked restore/publish 三步 exit 0）。
- 仓库外独立 CustomHost.csproj 只引用存储和契约工程，不引用 Sample 可执行程序。显式准备首次锁图后，实际 locked build-compose 成功；解包的宿主在无 SDK 搜索环境正常启动，读取的 projectId/buildId 与包清单一致。
- 三个归档全部文件集合、大小/hash、入口执行位、三份契约字节核对通过。新增 probes/source-compose 下的源码与原生窗口探针，保留每步日志与结构化结果。
- 两个 Sample 解包到含空格目录，从其他工作目录且不继承开发 LD_LIBRARY_PATH 的环境启动，包内 CoreCLR/VLC/SDL 加载、会话初始化与确认退出后 exit 0。首次窗口识别未匹配，stdout 显示会话已就绪；探针补充重挂载窗口枚举和 UTF-8 `_NET_WM_NAME`，两个产物均通过。首次失败保留在 gui-archive，成功为 gui-archive-v2 / gui-directory-v2。
- 发布保护测试 Windows/Linux 各 4/4 通过；现有非空输出保护、错误 SDK/native 和自有进程树超时/无关进程保留均覆盖。

证据归档：`out/cross-platform-validation/20261030-source-compose/`，含 compose.json、linux-publish.json、SDK/源码来源、构建日志、三包清单、正常/失败 GUI 结果与模块路径。本轮媒体从本设备既有 Ubuntu 26.04 依赖重建私有布局，逐包复核保留 SHA256/版权，provenance 明确只是本地开发探针，不是新的 Ubuntu 24.04 基线或通过公开再分发审查的候选。旧 dev.9/dev.10 未修改，完整媒体、原生 Linux 桌面、P1 原生 Editor 导出和最终统一发行仍待验收。当前 Linux 隔离 SDK/源码/产物暂供紧接的共享宿主阶段复用。

契约来源规则已补到正式发行手册：同一实际应用构建的闭包用于内容导出，源码合包使用匹配的源码/配置/版本；换宿主/源码上下文重新核对并按需重新导出，不以版本号相同放宽 hash。

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

### WSLg 人工反馈与图形驱动对照

用户通过 manual-profile 测试后报告 blur 等效果很卡、其他大致正常，随后明确纠正为声音已经正常。按最新反馈停止音频排查；其他流程仅记录为本次人工观察，不扩写成未逐项执行的正式验收。

- 用户日志 `out/cross-platform-p3b-current/manual-profile/logs/game-20261029.log` 记录会话启动、剧情/选择、存档页导航及正常关闭；原日志没有帧耗时或实际图形驱动信息，不能单凭日志确定 blur 瓶颈。
- 当前 WSL 默认 `glxinfo -B` 为 llvmpipe、Accelerated: no；进程级 `GALLIUM_DRIVER=d3d12` 对照为 Intel UHD Graphics、Accelerated: yes。默认软件渲染是已确认的环境问题，但不能由此证明 blur 开销全部来自它。
- 新增 `scripts/samples/run-wsl-sample.sh`，复用现有 Developer 候选、工作区原生依赖和 manual-profile。在存在 `/dev/dxg` 且调用者未设置 GALLIUM_DRIVER 时，为本次子进程选择 d3d12；打印诊断，不修改全局配置。`--check` 不启动游戏；显式 llvmpipe 对照仍被保留。
- `bash -n`、候选/显示检查和 d3d12/llvmpipe 两种驱动检查通过。独立 profile 的实际 Sample D3D12 启动、会话就绪、确认退出 exit 0 通过，进程 maps 确认加载 libd3d12.so / libd3d12core.so，见 `out/cross-platform-p3b-current/smoke/46625ce92974451c933742fb39b9764e/`。
- 未测 blur 帧耗时，也未由驱动加载结果推定 SceneRenderPipeline 全部走 GPU。用户随后使用新入口确认“ok了”，记录为声音正常、blur 卡顿人工复测通过；P3 不因此整体 verified。

## 2026-10-29：Windows 原子发布与 Linux 独立候选

### Windows 短暂句柄冲突

独立于 GalNet 的最小 Directory.Move 探针在有 FileSystemWatcher 时 100 次失败 1 次、无 watcher 时 100 次失败 2 次；失败后等待 100ms 均成功。确认短暂的 Windows 重命名冲突，未识别具体持有句柄的外部程序，不归因为 watcher 或杀毒软件。

FilePublication 仅在 Windows 的错误码 5/32/33 下重试原生 Move，最多六次、累计等待 620ms，保持原子发布、目标不覆盖及取消语义。ProjectContentBuilder、项目创建、目录/归档导出、组合包和 profile 文件替换共用它；回滚不继承已取消的 token，以便恢复原输出。永久拒绝仍失败，不使用删除后复制降级。

- Storage 全量 32/32、Editor.Shared 全量 494/494 通过；新增真实不共享 delete 的 Windows 句柄测试覆盖短暂/永久占用、取消及保留旧文件。
- Windows `0.1.0-dev.9` 七个 ZIP 载荷验证通过；SelfContained、FrameworkDependent 的完整 DesktopSmoke 均通过，检查包内/系统运行时来源、独立项目创建/导出及真实 Editor/Sample 初始化。该脚本会终止测试进程，不作为正常退出证据。
- 两种分发 CLI 各连续导出 20 次，40/40 成功。
- 精简证据归档在 `out/cross-platform-validation/20261029/`；最终 Windows 候选为 `out/release/0.1.0-dev.9/`。候选包含当时工作区改动，不是精确已提交 tag 的公开发行。

### Linux 候选工具

新增 `Publish-Linux.ps1`、`prepare-linux-media.py`、`package-linux.py`：Desktop/linux-x64 自包含发布、Ubuntu 24.04 私有媒体闭包、保留权限与空目录的 tar.gz，合并 Editor/CLI 并核对重名依赖、严格契约、内容文件大小/hash 及 native ELF/ldd。准备媒体只下载/提取到独立工作目录，不安装或更新系统软件。包内启动脚本设置自己的 native/VLC 路径，并沿用已验证的 WSLg d3d12 选择条件。

Linux 包含纯 Developer Sample、GameTestCase 整合 Sample、Editor/CLI 与 Contracts；仍是本地候选。宿主 glibc、ICU、图形驱动、显示、字体和音频服务属于系统前置条件。记录 Ubuntu 包来源、版本、SHA256 与版权文件；公开再分发前仍需完成对应源码可用性和许可审查。Linux/macOS compose 与源码 build-compose 尚未实现，不能把手工构建整合候选记为通用 compose 已完成。

打包保护测试 8/8 通过（权限/空目录、拒绝链接、依赖冲突、媒体目录与 publish 根目录错误 ELF、输出目录边界）。Linux 实际重新解包及窗口验证结果如下。

| Linux dev.9 检查 | 结果与证据（out/cross-platform-validation/20261029） |
| --- | --- |
| 最终归档 | 三个 tar.gz、Contracts ZIP，逐项索引 size/SHA256、解包后全部发行文件 hash 通过；含空格路径、执行权限、空扩展目录通过，linux-extraction.json |
| 分发 CLI | 自包含、DOTNET_ROOT 指向不存在目录，help、包内默认创作 Skill、指定 Sample 契约创建项目、schema、文件夹/galpak 两种导出、galpak 安装及两次 inspect 全通过，linux-cli-result.json |
| Editor 原生窗口 | 解包后的 Editor 弹出标题窗口，包内 coreclr、宿主 D3D12，WM_DELETE 正常退出 0；linux-smoke/873880aa7a9d4724a8375eb8055d4d6a/result.json |
| Sample 原生窗口 | 整合 Developer Sample 会话就绪、确认退出 0；包内 coreclr/Skia/VLC 及宿主 D3D12，未注入旧 deps；linux-smoke/ac8e74081cee4d90b63fc1b3fae7583a/result.json |
| 视频原生闭包 | ctypes 使用包内 VLC/FFmpeg 解码真实 niko.mp4，取得 5 帧且像素非零；未设置 VLC_PLUGIN_PATH，覆盖托管默认工厂清空该变量后的自动发现路径；linux-media-result.json |

视频探针 stderr 仍输出两次 Failed to create video converter，随后成功得到像素；保留 linux-media-stderr.log，不将此次短探针扩写为所有视频格式、Gallery/缩略图或长期播放已验收。它只证明该包原生依赖和 CPU 解码路径实际可用。

发布根目录的 .so 另核对 ELF 架构与 ldd；可选 .NET LTTng 系统追踪 provider 依赖本机缺少的旧 liblttng-ust.so.0，明确排除其 ldd 必需性检查、仍校验 ELF 并保留原文件。当前不声明支持 PerfCollect/LTTng，未安装旧库或伪造 SONAME 链接。结果见 linux-published-native.json。

首次解包探针把 /proc/maps 按全部空格拆分，截断含空格路径；修正为只拆前五列后重验 Editor/Sample/媒体来源通过。CLI 夹具先误用 project export（应按 help 使用 export），又将 galpak 传给只接受目录的 game inspect；修正为 galpak install 后 inspect，全部通过。这些是探针错误，未修改产品行为规避。

最终 Linux 本地候选：`out/linux-release/0.1.0-dev.9/`。Windows 与 Linux 候选保留原始封存字节，清单中的 sourceCommit 为打包时 HEAD，包含当时尚未提交的本 feature 改动，不宣称精确 tag 构建。

### 清理与尚未验收范围

先保存 TRX、重命名探针、40 次导出结果、Windows 启动/CLI 日志、Linux 解包/窗口/解码/CLI 结果及媒体来源，再清理本次旧版 Windows 候选、重复 smoke 解包、发布 staging、Linux apt 下载缓存/媒体中间目录/原始 publish 与临时 ext4 解包。精确删除清单和字节数在 cleanup-*.json。保留最新 Windows/Linux dev.9、原 `linux-developer`、旧 launcher 依赖与用户 `manual-profile`；未触及用户已有 iOS packages.lock.json 改动。

P3b 的 Ubuntu 24.04.3 / WSLg 本地候选切片通过；P3 整体仍 in-progress。后续包括 P1 原生 Editor UI 两种导出、完整 Linux 媒体/Gallery/调试与原生桌面、媒体公开再分发审查、macOS、SDL、Linux/macOS compose 与 build-compose，以及 P6/P7 移动模板/真机。macOS 当前不可验证不阻挡其他切片。

## 2026-10-29：Linux 无 SDK 合包切片

GameDistributionComposer 复用现有完整内容、发行清单、严格契约和原子发布验证，加入 linux-x64 tar.gz/Unix 目录输入与 tar.gz 输出。保留 Unix rwx 模式、空目录，重建 Sample-Game 清单；验证真实 ELF x64 apphost 和可选 /bin/sh 启动脚本，两者须有用户执行位。拒绝重复/越界条目、软/硬链接、特殊文件与特权权限。Windows 主机可从 tar 元数据保留模式；Linux 目录输入只接受能读取 Unix 权限的宿主。macOS、源码 build-compose 与签名未因此实现。

CLI help 与打包 README 同步；PowerShell 薄入口增加无扩展名 CLI 的定位，继续保留退出码、不提供 dotnet 回退。已使用 galnet-sync-docs 同步 release-packaging 操作说明。

证据位于 `out/cross-platform-validation/20261029-compose/`：

- Editor.Shared 全量 504/504 通过（含新增 10 项及既有 Windows 合包）；editor-shared-compose.trx。首轮局部回归 23/24，唯一失败是测试构建清单时提前调用布局验证，导致故障注入夹具在断言前抛错；改为直接读布局 JSON 的 RID，不改产品校验，完整回归通过。首轮 sandbox testhost 管道无响应，核对并终止本次三层自有进程，在原生环境重跑；沿用既有环境经验。
- Linux CLI 自包含 Desktop/linux-x64 发布 dev.10 成功，未增加锁文件变化。创作/导出使用与目标 dev.9 Sample 匹配的 dev.9 CLI：新 dev.10 CLI 直接加载旧契约会正确拒绝 shared Core 字节不一致，没有放宽严格身份检查。新 CLI 的 compose 只处理已导出内容元数据，不加载项目代码。
- WSL 无 SDK 搜索环境下，tar 运行包 + 内容目录、Unix 运行目录 + 完整 galpak 两组均通过；输出文件 SHA256、逐项发行 payload hash、执行位、空目录、纯运行包未写入内容及暂存清理通过。见 linux-compose-result.json。
- 合包 tar 重新解到含空格路径，真实 Sample 窗口/会话就绪、确认退出 0 通过；加载包内 coreclr/Skia/VLC 与宿主 D3D12。见 linux-smoke/f7c5b02b88aa4e43825fd389fecea844/result.json。
- 真正分发式 Tools/compose.sh 调用新自包含 CLI，含空格参数转发、已有输出拒绝 exit 1 及原文件保留通过；sh-wrapper-result.json。compose.ps1 AST 解析通过，Linux 上的 PowerShell 7 入口本次未实跑。
- Python 打包保护 8/8、git diff --check 通过。未重建整套 Windows/Linux 应用发行包，dev.9 仍保留原封存字节；新的 CLI 本地候选为 `out/linux-compose-cli-dev10/`（含 Tools 两种薄入口与说明），不声明为公开发行。
- 本次 `/tmp/galnet compose linux ...` 下的原运行包/Editor、CLI 副本、两份合包归档及重复解包均已清理，共 3,151,046,488 字节，保留精简证据和 CLI 候选；原 manual-profile、手测安装、dev.9 发行包及用户 iOS lock 改动未动。见 cleanup.json。

P4 Linux compose 子范围通过，P4 整体仍 in-progress。下一段可推进源码 build-compose；原生 Editor UI/完整 Linux 媒体、原生 Linux 桌面、公开分发审查、macOS/签名、SDL 和移动模板/真机仍独立追踪。

## 2026-10-29：源码 build-compose

新增 CLI/薄脚本入口 `build-compose`：显式应用 csproj、apphost、版本、RID/配置/flavor，检查实际 SDK，按匹配的锁图分别 restore 与 publish --no-restore，普通自包含输出封装为 lib/docs/pak 后复用严格 compose。只接受 Windows/Linux x64，不开放任意 MSBuild 参数拼接；文件路径中的 MSBuild 特殊字符转义。Windows 入口放在 lib 内以保持正常 SDK apphost 的程序集相邻规则，Linux 根 sh 入口设置包内 loader 后调用 lib ELF；内容契约必须与构建后的 DLL 字节匹配。

每一步保存独立 stdout/stderr 和退出码，JSON 返回实际 SDK 与日志目录。还原、发布或合包失败不留下最终游戏包，日志保留，自有 staging 清理；Ctrl+C 终止本次 SDK 进程树并返回 130，不结束其他构建。Windows/Linux 工具包发布脚本包含两种 build-compose 薄入口；没有重写已有 dev.9/dev.10 候选。

证据在 `out/cross-platform-validation/20261029-build-compose/`：

| 验证 | 结果/证据 |
| --- | --- |
| Editor.Shared 完整回归 | 515/515，build-compose-all-final.trx；覆盖 SDK 缺失/版本不匹配、还原/发布退出码、契约拒绝、冲突/输入保护、转义、取消和跨主机封装 |
| 真实子进程取消 | 上述全量中的 BuildComposeProcessTests 启动真实 MSBuild/PowerShell 等待子进程后取消，确认整个自有树退出；另验证 SDK 解析和两路日志 |
| Windows 源码发布合包 | SDK 10.0.401，真实 Sample、Developer、Version 1.0.0、WindowsReadyToRun 隔离图（实际 R2R=false）；SDK/还原/发布均 exit 0，合包及解包逐文件 hash 通过，actual-windows-result.json |
| Windows 原生运行 | source-windows-native.trx，1/1；含空格解包路径、独立 profile、无 SDK 搜索，包内 coreclr/VLC、会话就绪及正常退出 0；日志归档 windows-native/ |
| Windows 构建 Linux | Desktop/linux-x64 隔离图，显式 miniaudio .so 和从已验 hash 的 dev.9 包恢复的私有媒体闭包；三步 exit 0，严格契约/合包通过，actual-linux-build.json |
| Linux 解包及原生运行 | source-linux-extraction.json 核对归档/逐文件 hash、权限和空目录；linux-smoke/db2ed1bada8a4ed287a10f9755a431f1/result.json：窗口/会话/确认退出 0，包内 coreclr/Skia/VLC 和宿主 D3D12 |
| 薄脚本 | PowerShell 和 WSL sh 实际转发含空格参数，已有输出拒绝和 exit 1 保留；ps-wrapper.json、sh-wrapper.json。脚本 AST/语法与 Python 打包保护 8/8 通过 |

保留的失败与修正：

- Windows publish 隐式还原使可移植分析器 RID 与 WindowsReadyToRun 锁图不匹配；改为明确 restore 后 no-restore 发布，保持锁定校验。Desktop 还原必须有单数 RuntimeIdentifier 满足应用 RID 检查，而 restore -r 引入的复数 RuntimeIdentifiers 会污染分析器空 RID 图；Desktop 不使用 -r，现有 WindowsReadyToRun 则按既有 Publish.ps1 保留 -r。SDK/所有包版本未放宽，锁文件未修改。详见实际失败 JSON、归档 SDK stdout 和 L-20261029-16。
- Windows 的 *.so* 把 System.Net.Sockets.dll 误送入 ELF 校验；改为 Ordinal 的 .so/.so. 文件名规则，仍严格检查 ELF64/小端/x64。补完整准备/合包回归，详见 actual-linux-before-so-fix.json 和 L-20261029-17。
- 首次 no-build 原生测试使用旧测试 DLL，匹配 0 项，未计作通过；重新编译后明确执行 1 项通过。

清理自有 Windows fixture、SDK 发布副本、媒体缓存、两份测试游戏归档/重复解包、包装脚本夹具和临时 ext4 解包，共约 1.84 GiB；精确路径/大小见 cleanup-windows.json、cleanup-linux.json。完整 SDK 日志复制到 sdk-logs/，retained-log-index.json 对照原路径与保留路径；用户 manual-profile、手测安装、旧候选和 iOS lock 改动未触碰。项目正常 bin/obj 缓存不作为临时 fixture 删除。

本阶段完成的是 Windows 宿主到 Windows/Linux 目标的源码合包切片。Linux 宿主 SDK、其他定制工程、macOS/签名和完整媒体仍有独立验收缺口；P4 整体保持 in-progress。已完成步骤归入 progress-summary.md，剩余计划转向 SDL 探针与桌面验收。

## 2026-10-29：P0 SDL3 与 Avalonia 共存探针

用户进一步要求连续推进整个 feature；每个可验证阶段提交 Git 并简化计划，环境缺失保留明确验收边界，不在每个切片完成后暂停等待“继续”。

新增 `probes/sdl3/`，固定 SDL 3.4.18 官方发行归档及 GitHub release asset digest，使用 SDL 官方镜像下载。Windows 采用官方 x64 DLL；Linux 从同源归档在临时 ext4 目录构建输入专用库，CMake 完整选项与实际功能探测保留。源生成 LibraryImport 窄绑定检查 SDL3 的一字节 bool、四字节枚举/ID，以及 size-versioned 136 字节虚拟设备描述符。只初始化 gamepad，SDL 自动带起 joystick/events；整个测试 video/audio 均未启用。SDL 明确允许状态采集，Avalonia 焦点 gate 抑制后台输出；正式动作取消/重新武装与多 scope SDL 所有权仍待 P5 实现。

证据根：`out/cross-platform-validation/20261029-sdl3/`。

| 检查 | 结果/证据 |
| --- | --- |
| 原生来源/许可 | native-win/native.json、native-linux/native.json；归档/库 SHA256、SDL zlib 许可、Linux configure/build/ldd.log。Linux HIDAPI/virtual 开启，libudev/libusb 关闭；不可据此声明完整物理设备兼容 |
| Windows 最终自包含运行 | windows-font-fixed/verification.json：含空格隔离目录，3 次 exit 0，每次 52 项，包括两次初始化周期、按键/轴、释放、按住时断连、新连接 ID、真实 Avalonia 焦点切换；运行副本自动清理 |
| WSLg 最终自包含运行 | linux-fixed/verification.json：同样 3 次 × 52 项及 exit 0，/tmp 含空格目录，私有 SDL 与自包含 .NET，未从系统加载 SDL，副本自动清理 |
| 错误 native 拒绝 | 上述两组 bad-hash.json，均在 UI 启动前 exit 1；运行器同时检查进程退出码与 JSON passed，不仅看日志 |
| 环境边界 | 当前两端探测不到实际标准手柄；虚拟设备不计为实机通过。macOS、移动/AOT、正式 GameHost 动作/部署仍待后续 |

保留两次失败：首次 Windows 在 Opened 同步完成所有检查后直接 Shutdown，使 Avalonia lifetime StartCore 重入空引用（windows-before-refinement.log）；修正为 Opened 先让出启动调用栈，并修正外层异常必须返回非零。Linux 首次独立探针未注册字体，启动布局时找不到默认 Inter（linux-final/）；按已有字体 lesson 显式打包并注册开源 Inter，最终两端均重建复核。没有修改已修好的正式 Sample 字体策略。

准备脚本在 Windows sandbox 临时目录结束时曾遇 ACL 拒绝；Windows 工作目录改放显式输出的父目录并验证生成路径，Linux 仍用独立 /tmp。原始失败临时目录及自有下载/发布副本在阶段清理时移除，原有手测安装、profile、旧候选和用户 iOS lock 改动保留。P0 Windows/WSLg SDL 子范围完成，P0 整体与 P5 仍 in-progress；接下来替换正式输入适配。

## 2026-10-30：P5 正式 SDL 输入及交付切片

新增 GalNet.Input.Sdl3，使用源生成窄 C ABI（显式 cdecl、SDL bool 一字节），原生库固定为 SDL3-CS.Windows/Linux/MacOS 3.4.18.1，实际 SDL 3.4.18；仅采用 runtime assets，排除上游桌面无关的 mobile main/static framework targets。原生清单记录包来源提交和各 RID SHA256，随两份 zlib 许可交付。中立 snapshot/interface 位于 Presentation.Abstractions，输入信息不进入 Core 剧情状态。

GameHost 替换 XInput，绑定窗口时在 UI 线程初始化并以 32ms timer 更新，失活立即 Cancel，解绑/关闭只释放自有引用。多个 scope 共用输入租约；检测已有外部 input owner 或不同 SDL main thread 时可诊断拒绝，不接管外部子系统。只启用 gamepad/隐式 joystick/events，不创建 SDL 窗口。仅加载应用私有 native，缺库/版本/初始化失败使手柄不可用而不阻塞键鼠。

GameView 维持首个连接直至断开，统一按钮/轴、0.5/0.35 摇杆阈值和 0.5/0.4 扳机迟滞，取消不生成释放提交，激活/重连须先回中立。按实际 face labels 显示按钮名称及物理位置，持久化仍为位置代码；已打开设置面板随可用性和布局更新。既有导航连发/动作路由复用 Cancel。

证据根：out/cross-platform-validation/20261030-sdl3/。

| 检查 | 结果/证据 |
| --- | --- |
| 正式 Windows 原生后端/定时器 | windows-owned/：3 次 × 40 项，exit 0；实际窗口焦点、按住返回防误触发、hotplug、新 ID、标准按钮/轴、多 scope、错误线程、外部 input owner 和 events 引用保留、输入队列与无关 user event 检查 |
| 正式 Linux / WSLg | linux-owned/：相同 3 次 × 40 项，exit 0；使用 NuGet 私有 SDL 的完整构建与自包含 .NET，含空格 ext4 路径；运行副本自动清理 |
| 私有 native 缺失 | 上述两端 run-3.json：实际移走入口库，UI 正常启动/退出 0，手柄不可用且原因明确；未从系统 SDL 回退 |
| 资产/许可 | verify-host.py 核对已发布 SDL SHA256 与随包 native-runtime.json 一致，两份许可存在；当前四类 SDK/包链均未引入 SDL 托管主循环 |
| 共享展示/输入 | presentation-full.trx 313/313；input-ui-fixed.trx 47/47，包括设备选择、失焦/断连与重新武装、死区、按钮提示及动态面板 |
| 架构 | architecture.trx 53/53；Input.Sdl3 只依赖中立 Presentation.Abstractions，Core/Runtime 不接触 SDL |
| 发行合包 | distribution-notices.trx 33/33；源码准备把 ThirdParty 移至 docs，tar/compose 保留通知，不混入 lib/ThirdParty |
| NuGet 真实交付 | nuget/package-validation.json：五个验证用本地候选完成实际构建/封装；四类仓库外消费者 restore/build/run 成功；Desktop/Avalonia native 及清单/两份许可传递并匹配哈希。日志、候选哈希保留于 nuget/，候选包未发布 |
| 发布图 | Player/Developer 的普通、WindowsReadyToRun、Desktop/linux-x64 锁文件及新增 SDL 工程锁图；Linux 既有 dev.6 项目版本元数据保留，未夹带无关锁文件变更 |
| 脚本检查 | Linux 包保护在 WSL 文件系统 8/8；Windows 上相同用例不保留 POSIX 执行位，保留首次失败 log，不削弱断言。PowerShell AST 与 diff 检查通过 |

保留的失败/修正：正式 Windows 首次发现关闭普通 SDL 输入事件后队列仍残留（host-windows-first.json）；改为独占 input 的租约仅清理 0x600–0x6ff，保留其他 SDL 事件，不调用全局 SDL_Quit。见 L-20261030-01。动态面板首次测试未挂主题模板导致找不到 capture 控件（input-final.trx）；按正式主题挂载后 47 项通过。NuGet 首次尝试复用 WorkDirectory 被既有保护拒绝，未覆盖原候选；改用新目录封装已完成构建的最终 DLL，再通过全部外部消费者。

清理自有 NuGet 候选、两轮封装目录、仓库外消费者隔离缓存约 2.00 GiB，保留构建/封装/消费者日志和候选哈希，见 nuget/cleanup.json；正式探针发布副本与包检查缓存按 stage-cleanup.json 清理。旧 dev.9/dev.10、本体手测安装、manual-profile 及用户 iOS lock 未触碰，项目正常 bin/obj 缓存保留。

此切片证明实现与 Windows/WSLg 自动化交付。真实手柄、原生 Linux 桌面、macOS、移动、完整应用发行矩阵及 GUI 核心流程仍待验收；P5 整体 in-progress。完成项已从计划归并到总结，继续推进后续可独立实施项。

## 2026-10-30：P4/P5 应用发布矩阵与真实 Linux SDK

新增 scripts/ci/publish-applications.py：精确检查 global.json SDK，在匹配的原生宿主用 CMake 构建 miniaudio，或显式接收经目标头检查的 native；Desktop/RID 隔离锁图，分别 restore/publish --no-restore，真实发布 Editor、CLI、Sample Player/Developer，验证 apphost/CoreCLR/所需 native 的 PE/ELF/Mach-O 头、契约闭包、SDL SHA256 与通知、同宿主 Core/Builtins 字节一致性。每步记录退出码/时间/日志，输出逐文件 hash 的 application-publish.json。新增路径触发的 Application portability workflow，覆盖 Windows x64、Ubuntu x64、macOS Intel/ARM；仅保存报告和日志，不公开分发候选。

本地执行结果归档于 out/cross-platform-validation/20261030-applications/：

- Windows：四种实际普通自包含发布通过。首次显式生成 Desktop/win-x64 锁图，随后默认 locked restore 的完整四种发布通过，windows-publish.json 与 windows-logs/。
- Linux 原生 SDK：按官方发布元数据下载精确 10.0.401 并核 SHA512，解到独立 ext4 临时目录；从已提交 6a6c3fb8 的 tar 快照起步，无 Windows bin/obj，记录 source.tar SHA256 和唯一 SDK 名称补丁。复用既有包缓存，使用 native Linux SDK/CMake 完成四种 locked restore/publish，linux-publish.json、linux-logs/、sdk-source.json、linux-source-archive.json、linux-source-patches.json。
- Linux 首次失败：Editor 声明 Microsoft.NET.SDK，原生 resolver 找不到大小写敏感的 Microsoft.NET.Sdk 目录，MSB4236。修正标准大小写后完整矩阵通过；Windows 及 Windows cross-publish 不能发现此类宿主问题。首次失败保存在 linux-first-failure-logs/，见 L-20261030-02。
- macOS：两架构 Editor/CLI/两 Sample flavor 的明确 Desktop 锁图还原完成；无实际 macOS 机器，尚未执行 macOS 发布、GUI、媒体、签名或该 CI job，不能把依赖还原写成目标平台成功。

这些是 raw SDK 输出验证，不包含完整私有视频闭包、最终发行布局/归档、macOS .app/签名、内容安装和 GUI/device runtime。报告明确 runtimeTested=false、privateVideoClosureTested=false。各宿主内部的共享契约 hash 一致，但 Windows 工作树与 Linux Git archive 的 source/git/换行上下文不同，所得 DLL hash 不同；目前不把这两组当作跨宿主可互换工件，不降低严格合包检查。预编译内容和 source build 仍必须使用对应构建的契约，跨宿主发布的统一契约工件验证保留到剩余计划。

Windows 两轮 raw publish 自有副本清理时保留报告/日志与后续 P1 所需的三份 Sample 契约；Linux 隔离 SDK、源码与 raw 发布暂供紧接的原生 build-compose 闭环使用，阶段结束一起清理，避免重复下载/构建。用户原包、手测 profile 和 iOS lock 不动。P4/P5 整体仍 in-progress，已验证部分归入总结。

脚本保护验证：新增 test-application-publish.py 的输出保护、SDK/native 拒绝和自有进程树取消测试在 Windows 通过 4/4，记录在 windows-guards.log。超时仅停止测试自建父子进程，无关对照进程继续存活。首次运行请求因自动审批服务额度限制未执行；本次重新通过审批后运行成功。实际 Windows/Linux 四应用发布证据来自上述已完成运行；其后脚本加入逗号转义、可注入超时参数和进程退出检查，新增保护测试通过不等同于重新执行完整发布矩阵，Linux 保护测试仍待补。
