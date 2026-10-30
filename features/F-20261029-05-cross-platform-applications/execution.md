# 执行记录

本文件保留详细历史与失败证据；当前结果见[阶段总结](progress-summary.md)，下一步见[剩余计划](phase-plan.md)。

## 2026-10-30：P6a 共享玩家、内容流与 Android Mono AOT

实现：新增 GalNet.Sample.Player，把原 Sample 的语言选择、本地化、主视图、截图模块及公共组合迁入；原 Sample 只保留桌面/调试组合。CrossPlatform 已用共享会话替换 Greeting 页面，支持 desktop/activity/single-view，Activity 重建重挂载原视图；SamplePlayerSession 按 UI 线程拥有 root/scope，取消待选语言的初始化后异步清理，会话可重建。BundledGameInstaller 从平台资产流校验安装，内容按 ZIP SHA256 分版，profile 按 projectId 保持稳定。Android/iOS 配置资产与私有目录，iOS 未构建。

验证与失败修正：

- 初次共享库/宿主构建有两个普通 using 缺失，补齐后通过；聚焦窗口/语言/截图及会话测试 29 项通过。
- 新增包流测试初次使用默认中文文化却只提供 en-US 文本，导出按正常严格引用检查失败；修正测试输入文化后通过，不放宽导出规则。
- 完整 Debug Integration 初次 9 失败，均为测试仍从旧 App 程序集读取已迁移的嵌入式 UI 资源；改为实际资源所有者 SamplePlayerServices.Assembly。产品公共注册也只指向该共享程序集，没有恢复第二份资源。真实当前 HEAD（含并行 PAK/Core 提交）重新构建后 191/191 通过。
- Android 首次 Debug APK 误收 SDL3-CS.Linux 的 glibc native，产生 XA0141；对桌面 SDL 包顶层显式 ExcludeAssets=all，复建及 APK 清单检查确认已排除。lesson 见 agent knowledge 的 Android native 回退索引。
- Android Release 选择 MonoVM、PublishAot=false、RunAOTCompilation=true，ARM64/x86_64 各 116 个程序集实际 AOT 成功；最终冻结源码重新发布并嵌入完整 galpak，APK 的 assets/game.galpak 与原始导出一致，232 个 AOT native 条目。没有全局压制链接/AOT 诊断。这是本地签名候选和编译证据，不是设备或生产签名通过。
- 共享库增加后初次标准 Release locked restore 因 NoGameDebugging 锁图未更新而拒绝；显式维护锁图后正常 locked restore/build 通过。Windows R2R、四个 Desktop RID、两种原 Sample flavor 及新宿主图均已维护；macOS 仅还原未运行。
- 为避开工作区同时进行的 Assets/Core 和正式文档重组，使用 3078ce0b 源快照叠加本 feature 文件做最终 Release CI、Windows 候选和最终 Mono AOT；源码叠加清单/哈希归档。首次 Windows tar 解压漏掉 3 个中文资源，用 Python tarfile 恢复，不删除这些测试资源。一次验证脚本工作目录未切到冻结根，重新从明确 cwd 构建，未将该次产物当作隔离源码验收。
- 原/新 Windows 自包含宿主 2/2 原生用例通过：不同工作目录、独立 profile、无 SDK 搜索，窗口、会话、私有 CoreCLR/VLC/SDL、退出确认、exit 0 与 launchCount 持久化均核对。
- Linux 新宿主 raw publish 后首次 build-compose 因缺 Desktop.pubxml 拒绝；子工程 .gitignore 默认忽略 *.pubxml，已显式纳入源文件。补齐后原生 SDK 严格合包、973 文件 hash/权限检查、WSLg 会话及正常退出通过。Windows/Linux 都没有把旧运行包当作新宿主。
- 冻结源码标准 Release CI：2,206 通过，2 失败；失败仍是 FoundationViewsHaveNoUnlistedLiteralUserInterfaceText 与 GeneratedCheckboxIsCenteredInTheEditorColumnAtWideAndNarrowWidths，和 P1 历史原因/数值一致。Integration 186/186、架构及其他套件通过；不声明全量 CI 全绿。随后兼容当前已合入 PAK/Core 的真实工作树 Debug Integration 191/191。
- 应用 CI 矩阵纳入新 CrossPlatform 桌面宿主及共享源码触发，真实 Win/Linux raw 输出执行相同 native/SDL/契约检查；Windows 脚本保护 4/4。未重新执行全部远端矩阵，也未声称 macOS 运行成功。

精简证据：out/cross-platform-validation/20261030-shared-player/，包括 CI/TRX、正常/失败日志、native 模块与输出、SDK profile/来源清单、Mono 选项及 APK/内容 hash。最终 APK 在 out/cross-platform-p6-android-final/；旧 dev.9/dev.10 与用户手测安装未改动。P6 继续进行：独立模板生成、Android 实际会话/媒体/生命周期与完整设备运行后续完成；macOS/iOS/手柄验收按用户决定延期，Browser 不扩展。

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

## 2026-10-30：P7a Sample 移动媒体与生命周期

按用户最新决定，独立模板生成延后；先实现 Sample 的功能，再推进 Android/iOS 设备验收。macOS/手柄验收延期不作当前门禁。

实现：移动宿主显式注册 LibVlcFactory，桌面 Default 改为 Lazy，避免构造移动工厂时提前触发桌面配置异常。Android 使用官方 LibVLC 3.7.0-beta、NDK r29/16 KB miniaudio；iOS 增加静态 NativeReference/ForceLoad、主程序符号解析和 LibVLC 3.6.1。剧情/Gallery/缩略图共用 LibVlcFrameOutput，三种 Gallery 主题改为 SceneLayerHost，保留剧情原有图层语义及一份像素复制。

生命周期：共享命令门串行写独立恢复检查点，再停止/释放当前 run；即使保存失败也尝试释放媒体。偏好与已读进度在后台写盘，Gallery 停止并回标题；“继续”按时间选择最新有效检查点/玩家槽，检查点不占用手动槽。新游戏/显式读档/清空状态移除旧检查点。Android 接音频焦点/拔耳机/前后台，系统返回由 Avalonia BackRequested 执行页面 BackCommand；安全区自动应用。iOS 接 AVAudioSession 中断/路由、有限后台执行时间、生命周期和退出菜单能力。恢复语义为稳定剧情状态，不能宣称任意瞬间媒体游标无缝恢复。

验证证据 `out/cross-platform-p7-tools/`：

- Windows：Integration 193/193、Presentation 313/313、GameHost 10/10；真实 VLC 场景/Gallery/缩略图及释放 5/5。
- Linux：复用 Ubuntu 26.04.1 的隔离原生 SDK 10.0.401/既有私有媒体依赖，将明确列出的共享修改覆盖到原有源码快照；构建通过，真实 VLC 场景/Gallery/缩略图及释放 5/5。源码覆盖 hash 在 linux-overlay.json；不是新冻结的整仓库发行或 24.04 最低基线验收。
- Android：完整 GameTestCase 在自有副本中使用当前 Release 三契约导出；双 ABI 实际 Mono AOT 各 115 程序集，无全局压制诊断。APK 逐项核内容 SHA256、所有 native 架构/LOAD 16 KB 对齐、媒体/音频/AOT 存在、无桌面 SDL，并通过 zipalign -c -P 16 4。最终候选和 hash 在 android-package-verification.json。
- 无 Android 连接设备；APK 没有运行，生产签名/AAB 未验。iOS 本轮没有 Mac/Xcode 编译或运行；原生目标名按 dotnet/macios _SanitizeNativeReferences 核对，但不能代替目标 SDK 编译、静态链接及设备验收。

失败与修正：稳定 Android VLC 3.6.5 的 libvlc/libc++ ELF LOAD 是 4 KB，出现 XA0141；没有屏蔽诊断，改用官方固定 3.7.0-beta，实际 native 为 3.0.22-rc1，需后续设备验证/稳定版替换。Android native 过早调用 _ResolveAndroidTooling 会使 SDK ContinueOnError 属性尚未初始化，改为 CoreCompile 前依赖 AndroidPrepareForBuild。首次 Linux 测试把插件目录指向 native/plugins，实际是 native/vlc/plugins；保留失败 log，修正配置后 5/5。首次完整内容导出用 Debug CLI 读取 Release 契约被严格拒绝，改为相同 Release 构建后导出成功，没有放宽字节校验。

补强“已有玩家槽不被检查点改写、Continue 优先新检查点”后，发现 ClearPlayerState 删除磁盘槽后未刷新 UI 列表，CanContinue 仍为 true；增加 RefreshSlotsAsync，并保留首次失败 log（integration-clear-first.log）。

P7a 可验证切片完成；P6/P7 整体仍 in-progress。移动设备/AOT 回调、iOS 编译/链接/签名、触摸/方向/性能和最终桌面完整流程继续追踪，不能把以上测试或 APK 生成升级为五平台完整支持。

## 2026-10-30：P7b Android 真机安装与基础运行（verified 切片）

纯 USB 在此前多次 I/O/EOF 掉线后，本次先通过 1 MiB 随机文件的手机端 SHA256 校验，随后约 3.8 秒完成约 134 MB APK 传输。此前传输故障的根因仍未确定，不归因于 Rider 插件安装。首次安装被 `INSTALL_FAILED_USER_RESTRICTED` 拒绝；用户开启 USB 安装并及时确认弹窗后成功。Rider 另外缺 Android Support 插件，安装后已生成 `XamarinAndroidProject` 运行配置。

设备为 REDMI K80 Pro（24122RKC7C）、Android 16 / API 36、ARM64、4 KB 页。本地签名 Release 候选成功安装，但第一次启动首屏显示 `Extension directories cannot be symbolic links`。Android 私有根改为 `FilesDir.CanonicalPath`；安装器的 `RequireNoLinks` 检查保持启用，并在异常中补具体路径。内容准备早于 profile logger 初始化时，App 回退到标准错误输出完整异常。路径祖先保护回归 9/9 通过。

新候选基于 `352d1678` 加本次修复。使用同一 Release 契约在自有 GameTestCase 副本重新导出，未修改原作者项目或放宽严格契约。双 ABI 各完成 117 程序集 Mono AOT，载荷检查、两架构 native ELF/16 KB LOAD 与 `zipalign -c -P 16 4` 通过，无桌面 SDL；APK SHA256 `4f29e276830748904f386e2a147b0b580976eee42f4b0c648ae1e8426f085c07`，嵌入内容 SHA256 `c5e41778083ba7c5e4c77bf6bd525acb1693309692d9cab71714d7a22fb89bee`。

原 out 下 NDK 工具缓存已被外部清理，本轮没有重新编译 native。两 ABI miniaudio 的现存 obj 工件先与旧 `e1d98c6d` APK 对应条目逐字节核对，再通过本地临时 targets 复用；native 源码未改，未把该临时构建入口加入正式工程。首次受限重建因 Avalonia 构建日志目录无写权限失败，正常构建权限下重试通过；没有压制 AOT/裁剪/native 诊断。

真机复测进入语言选择，随后实际显示剧情视频与叠加立绘，用户明确确认声音和触摸推进正常。应用 PID 日志未发现未处理异常、启动失败或生命周期失败。横屏画面是基础观察，不代表方向重建/安全区完整验收；声音与触摸为用户人工证据。

证据：`out/android-device-validation/` 的 `install-usb-confirmation-retry.log`、`install-path-fix.log`、`first-launch.png`、`path-fix.png`、`title.png`（实际已进入剧情的视频画面）、`playback-logcat.log`、`path-tests.log`、`path-fix-package-verification.json` 与 `device.json`。新 APK 保留在 Android 工程 `bin/Release/net10.0-android/org.galnet.sample-Signed.apk`。静态检查脚本自身仍标 `deviceTested=false`，设备证据单独记录。

本切片仅完成安装、内容初始化、语言页与基础剧情音视频/触摸 smoke。存读档、Gallery、音频焦点/耳机路由、后台检查点/进程结束恢复、方向/安全区/性能、x86_64 运行、生产签名/AAB 与 iOS 均未验收；P6/P7 整体继续 in-progress。

## 2026-10-30：P7c 移动退出策略、Android 全屏与后台重进

用户真机反馈：桌面图标再次打开 Sample 显示空白；上下系统栏长期占用，四周留白偏大。用户随后确定移动端不提供退出按钮，Android 收起状态栏和底部导航栏。实现隐藏 Android/iOS 的 CanExit，Android 标题页系统返回不弹退出框，保存/暂停后 MoveTaskToBack；Activity finishing 仅中断媒体，不永久 Shutdown 仍存活的共享 App。桌面显式退出仍走原确认与释放流程。

Android 挂载后通过 Avalonia InsetsManager 设置 edge-to-edge 和 IsSystemBarVisible=false，继续由 TopLevel 保护刘海/挖孔安全区。真机横屏截图的内容已铺满 1080 像素高度，状态栏和底部滑条不再常驻占位；2400×1080 屏幕仍保留 16:9 画布的侧边留白，没有裁剪或非等比拉伸。系统边缘手势的临时系统 UI 不属于永久隐藏承诺。

首次重进复测仍显示空白背景，保留 `immersive-reopen-{1,2,3}.png` 失败截图，未把 PID 存活/launcher 返回成功当作通过。定位为 GameShell 重复呈现同一 scoped 页面时清空自己的 presenter；随后给 PageHost.Content 设置同一对象不触发更新。修复为相同目标引用直接完成呈现，其他页面仍保留旧 presenter 清理。三种主题的视觉树断言在修复前 3/3 失败；修复后完整 Presentation 非显式测试 313/313 通过，包含重复 None/None/CrossFade 和随后进入设置。

测试环境修正：最初传入 GalNetBuildMiniaudio=false 且未部署 Windows native，完整 Presentation 有 4 项音频文件生命周期失败（309 通过）；补充正常日志/TRX 确认范围后，显式提供现存 win-x64 galnet_miniaudio.dll，313/313 通过。没有改音频业务实现或隐藏失败。共享宿主最终回归 7/7 通过，覆盖无退出能力时返回宿主、不显示确认框和后续可继续导航，以及桌面确认框/后台检查点等行为。

最终 Release APK SHA256 `0f9328c69ca67f9771b0eabeca1e25c081259054a162930c8b570b48f3f6b040`；内容仍为 P7b 的 `c5e41778083ba7c5e4c77bf6bd525acb1693309692d9cab71714d7a22fb89bee`。两 ABI 各 117 程序集 Mono AOT、完整资产/架构/16 KB LOAD 与 zipalign 通过；native 继续按 P7b 的逐字节核对工件复用，未重新编译 native。手机已覆盖安装该候选。

最终真机：cold launcher 启动、两次标题系统返回桌面后 launcher 重进、一次 Home 后 launcher 重进通过；同 PID 复用，截图均保留完整标题页、无退出按钮、无常驻系统栏，应用日志无未处理异常/生命周期失败。使用与桌面图标相同的 MAIN/LAUNCHER/NEW_TASK/RESET_TASK_IF_NEEDED Intent 验证重进，未声称自动点过实际桌面图标。

补充的坐标设置页探针未处于预期标题页面：截图已进入剧情，随后 Back 观察到应用退到后台。此探针未完成预设步骤，不计为设置页真机通过；保留 `immersive-final-settings.png` 和工具结果，设置返回的自动化证据来自共享宿主/主题回归。后续设备操作应先确认当前页面，避免与用户手动游玩相互干扰。

证据在 `out/android-device-validation/`：`icon-start-warm.png`（旧版空白）、`immersive-final-title.png`、`immersive-final-reopen-{1,2,3}.png`、`immersive-final-reopen.json`、`immersive-final-runtime.log`、`repeated-title-before-fix.log`、`presentation-with-native-final.log`、`presentation-results/`、`mobile-back-final-tests.log`、`immersive-final-package-verification.json`。本切片不替代剧情存读档/Gallery/进程被系统杀死恢复、音频焦点路由、完整安全区/方向/性能或 iOS 真机验收。

## 2026-10-30：P6b 通用宿主、视图与内容安装归位（verified）

用户要求为模板取消额外的 Player 项目，允许两个主项目保留类似配置。设计与 P6b–P6e 计划已记录后，按“开始吧”先实施本切片；没有把整个 Player 改名移进框架。

Avalonia.GameHost 现在提供 `AvaloniaPlayerHost` 和 `Views.PlayerView`、LanguageSelectionView/ViewModel；注册回调必须由宿主提供项目页面、主题、本地化、设置与平台后端。通用视图不引用 SamplePlayerServices、固定配色或 Sample 文案程序集。Host 统一管理 scope、初始化取消、后台暂停/导航和异步释放；新增非空旧解析器的恢复回归，释放先恢复借用的 resolver，再销毁本地化服务。SamplePlayerSession 暂时只做配置委托；原桌面 MainWindow 也使用同一通用视图，主题由 Sample 配置施加。

BundledGameInstaller 移入 Storage.FileSystem，保留非可寻址资产流、SHA256 缓存、正式安装校验和调用方流所有权。GameProfilePaths 提取稳定身份计算，沿用原 profile 键且由宿主提供绝对根；SamplePlayerPaths 暂时只保留 OS 默认目录。SceneScreenshotWriter 负责 UI 线程捕获、链接/文件名路径校验、临时文件清理和不覆盖发布；SampleScreenshotModule 继续拥有 ScreenshotSchema、primitive 及命名约束。没有移动/修改项目契约或内容格式。

新增架构门禁覆盖 GameHost/GameView 的直接引用，以及通用宿主/视图/Storage 的传递依赖无 Sample、无循环。Presentation 测试工程直接消费通用宿主，没有引用 Sample 或 Sample.Contracts；测试提供自己的 theme、文本 provider、页面/启动服务及 HostOptions。

实际测试证据：

| 检查 | 结果 |
| --- | --- |
| Presentation | TRX total=328、executed=322、passed=322、failed=0；其余为未运行的显式场景 |
| Integration | TRX total=204、executed=195、passed=195、failed=0；包含原桌面窗口语言/主题、截图、流安装和后台检查点/重建；显式原生场景未运行 |
| Architecture | 54/54；包含新依赖门禁 |
| Storage.FileSystem | 34/34；包含稳定 profile 身份与默认根分离 |
| CrossPlatform Desktop | Windows Debug locked restore/build 通过，无新增构建警告 |
| 发布锁图 | 四 RID × 原 Sample Player/Developer/CrossPlatform Desktop 共 12 组，加原 Sample 两种 Windows R2R，共 14 组 locked restore 通过；仅依赖记录调整，既有包 resolved/contentHash 与框架/RID 键保持一致 |

保留的过程失败：首次测试代码把 IPlayerLocalization 当作具体类型使用索引器，编译失败后改用其 CultureService；没有改产品 API 迎合测试。第一次 Desktop 锁图命令误用 `restore -r`，Linux 目标被已有 RID 一致性门禁拒绝；按既有 L-20261029-16 修正为单数 `-p:RuntimeIdentifier`，全矩阵重新还原再 locked 校验，未放宽门禁或锁定模式。

证据根 `out/cross-platform-p6b/`：`architecture.log`、`storage.log`、`presentation-verified.log`、`integration.log`、`results/`、`cross-desktop-build.log` 和 `locked-*`；历史失败 log 保留。已同步宿主/架构 spec 与 Sample 定制/启动手册，只描述当前实现。

本切片没有重新发布或运行 Android/Linux/macOS/iOS，不将旧 P7b/P7c 真机结果记作本次新代码验收。Player 工程仍保留临时配置、资源、项目 primitive 和委托，P6c 才删除；两主项目独立配置、可选通用调试和新发布产物/设备回归分别留在 P6c/P6d/P6e，独立模板生成继续延期。

## 2026-10-30：P6c 两主项目定制入口与删除 Player（verified）

原 SampleCustomization 不再转发公共 Sample 默认值；CrossPlatform 主项目新增同名配置入口。两边各自注册主题/页面、字段编辑器、内容 catalog、ProjectSchemas、执行实现、本地化及路径策略，允许声明性配置相似。CrossPlatform App 直接创建 AvaloniaPlayerHost，原窗口使用自己提供的资源/样式。两主项目互不引用，共同引用既有库和 Sample.Contracts；内容 schema、严格字节规则、projectId profile 身份未改。

通用 UI 的 en-US/zh-CN 文案移至 GameHost，项目标题/说明由各主项目嵌入资源提供；CrossPlatform 有自己的标题。FromEmbeddedResources 按通用→项目顺序组合；启动期聚合为一个 provider，AttachGame 时仍保留原始精确文化来源。截图 primitive 的 dispatch/取消移入通用 ScreenshotPrimitiveInstance，项目只拥有契约注册/文件名校验和默认输出策略，不复制捕获机制。

Player 工程、生产源码及项目/解决方案/CI 路径已删除；模板调用与现有测试迁移到通用 Host 或各 Sample 定制入口。清理源锁图中的旧节点，并结构化核对实际生成的原 Sample/CrossPlatform Desktop deps.json 没有 Player 运行时依赖；磁盘上的旧 bin/obj 不作为新依赖来源，也未递归清理其他任务缓存。

验证：

| 检查 | 结果 |
| --- | --- |
| Presentation | total=330、executed/passed=324、failed=0；未运行显式用例另计 |
| Integration | total=205、executed/passed=196、failed=0；包含两配置入口独立页面/主题/文案和文化状态、原窗口/截图/后台回归 |
| Architecture | 55/55；两个主项目互不引用、旧工程不存在，源锁图 JSON 无旧节点 |
| CrossPlatform Desktop | Windows Debug locked restore/build 成功；无新增构建警告 |
| 锁图 | 12 组 Desktop（四 RID×原 Sample Player/Developer/CrossPlatform）+2 组 R2R，force-evaluate 后逐项 locked 验证；普通玩家/开发版锁图补齐；既有包版本/contentHash 和框架/RID 键不变 |
| 文档 | spec/manual 的当前入口/依赖/资源归属与 CrossPlatform README 已更新，docs check 通过 |

有价值的失败/修正：聚合 UI provider 的内部默认语言回退被外层误当成当前文化命中，挡住外部英文覆盖；新回归先失败（Project English 而非 External English），保留精确文化来源后通过，见 L-20261030-14。普通 restore/locked 虽通过仍保留已删除 Player 的 Project 节点；通过 force-evaluate 真正重算后移除，并加源锁图门禁，见 L-20261030-15。没有手工删锁节点或放宽校验。

普通过程失败也保留日志：ReadTextColor 测试迁移后遗漏 culture 本地变量导致编译失败；补回其 CultureService 引用后通过。缺键测试先按裸 key 断言，而库实际返回 #key#，修正预期而未改产品逻辑。架构测试重复插入检查块/using 导致编译失败，去重后复测；同时移除 P6b 中重复的已有 allowlist 条目。一次 PowerShell 单元素开关被展开成字符，使用显式参数重跑通过。

证据根 `out/cross-platform-p6c/`：`integration.log`、`presentation-verified.log`、`architecture-final.log`、`results/`、`cross-desktop-build.log`、`fallback-before-fix.log`、`force-evaluate-probe.log`、`force-final-*`、`locked-final-*`。Android/iOS 平台源码改用自身 SampleCustomization，未重新发布/设备运行；Browser 不新增功能，只移除过时引用。通用调试仍留 P6d，最终新产物/设备验证留 P6e，不能据此宣布全平台验收。

## 2026-10-30：P6d 可选调试与双宿主接入（verified）

把原 Sample 的调试日志/菜单/VM/快照及外部会话适配抽至 `GalNet.Avalonia.Debugging`。协议与 Engine 仍在 Shared.Debugging；普通 GameHost/GameView 不引用可选层。GameDebugActions 借用中立 WithSnapshotAsync/RefreshSlotsAsync，沿用正常保存和会话门禁。项目 replay（含 screenshot 忽略）由两个 Sample 各自配置，重启和打开目录动作由宿主回调。原 Sample 保留桌面 shell，CrossPlatform 使用 DebugToolsOverlay；移动不显示打开目录，不创建额外日志窗口。

CrossPlatform 增加调试/参数条件工程引用和独立锁图，默认 Debug 开启、Release 关闭。两主项目调试文案均不再内嵌，按通用 UI→可选调试→项目顺序合并；玩家版无调试模块/协议依赖或资源，普通日志继续工作。菜单确认在容器释放时取消，清除内容和事件；日志只在挂载期间订阅，并阻止旧代次排队回调重复污染重挂载列表。

新 Activity 回归先失败：CreateHostView 只重挂 PlayerView，实际 Content 不是预期 DebugToolsOverlay。修复后重挂完整容器，原 mount 清空且原玩家仍在视觉树。初始化结束后增加 generation/停止检查，避免旧 scope 后续连接邀请。证据 `activity-before.log` 和修复后的完整两种集成结果。

| 检查 | 原始结果 |
| --- | --- |
| Presentation 最终释放回归 | total=339、executed/passed=333、failed=0，6 个显式场景未执行；其中包含并行截图任务新增用例 |
| Integration 调试版 | total=206、executed/passed=197、failed=0，9 个显式 native 场景未执行 |
| Integration 玩家版 | total=198、executed/passed=192、failed=0，6 个显式 native 场景未执行 |
| Architecture | 56/56；可选层无 Sample 引用、普通层无反向依赖 |
| CrossPlatform Desktop 两种调试开关 | 实际 Debug build 成功，两个 deps.json 的可选模块有无符合开关；保留编译分析警告，不宣称零警告 |
| 锁图 | 16 Desktop + 2 R2R + 4 普通桌面 + 6 Android/iOS/Browser，共 28 组 force-evaluate 后 locked 通过 |
| 既有包身份 | 200 份锁图框架/RID 键及保留包 resolved/contentHash 一致；玩家测试图有意移除 DiagnosticsSupport、Logging.Abstractions、RecyclableMemoryStream |
| 文档 | sync_wiki.py check 通过；未发布外部 Wiki |

依赖身份检查首次错误地把预期删除的三项诊断依赖判为失败；核对实际旧/新锁图后记录精确移除白名单，未忽略版本或 hash 变更。全部 restore 原始日志和 `lock-matrix.json`、`lock-package-identities.json` 保留。最终证据在 `out/cross-platform-p6d/`：`presentation-disposal.log`、`integration-activity-final.log`、`integration-player-final.log`、`architecture-final.log`、`results/`、`cross-build-*` 和 `force-*`/`locked-*`。

没有以本切片自动化替代 P6e 新平台发布/运行。Android USB 当前在线；NDK 缓存此前被删除，正在工作区用官方 sdkmanager 准备正常构建工具链，不能继续依赖 P7b/P7c 的临时 native 复用 targets。独立 NuGet/模板交付、移动远程调试、iOS/macOS/真实手柄仍未验收。工作区并行截图/配色改动保持独立，不随本阶段提交。

P6d 提交：`faa9070e`。

## 2026-10-30：P6e 新发布产物检查点（in-progress）

`publish-applications.py` 的 CrossPlatform 角色扩为 Player/Developer，默认共六应用。两宿主均显式传入调试/参数属性；玩家输出拒绝调试协议/实现/DiagnosticsSupport 依赖，开发输出要求完整调试依赖及实际 DLL。两种发行 flavor 都排除 PDB：外部调试走协议而非源码级单步。Core/Builtins 在六应用间比较，Project.Contracts 在四份 Sample 产物间逐字节比较。

Windows 使用 SDK 10.0.401、四核/单 MSBuild worker、独立 Desktop 锁图、普通自包含发布，六角色全部完成且校验通过。初版门禁完整脚本测试 7/7 通过；随后补充实际 DiagnosticsSupport DLL 名称及缺 DLL 拒绝，再以不启动程序的两项篡改测试通过，并用加强后的验证器只读复查六份真实产物。此前 SDK 报告仍为 runtimeTested=false，额外最终检查同样明确不代表运行。

在 `out/cross-platform-p6e/P6e-validation-<随机身份>` 作者副本内替换为四 Sample 共享的实际三份契约，使用新 CLI 完整导出并分别安装到四份输出的 `pak`；原 GameTestCase/用户 profile 不变。导出项目身份专供 Windows 探针，不应拿它覆盖手机已有项目身份。外部调试显式测试现在识别原始 SDK 输出和发行布局两种契约目录；此测试修改尚未重新编译/执行。

Linux 从 `faa9070e` 的无 bin/obj 源码归档和本次发布脚本副本建立 `/tmp/galnet-p6e-7C5iI2/source`，gcc 从实际源码生成新的 miniaudio。首轮保护测试失败：SDK 因系统缺 ICU 而在 --version 阶段终止，错误 native 用例因此未取得 sdkVersion；该轮 7 项中 1 个 error、1 个 Windows-only skip，不算通过。已有本地媒体目录包含 ICU78；已准备在 SDK 构建进程指定该目录的 LD_LIBRARY_PATH，尚未执行修复后的重跑。Linux 应用发布和 GUI 均未完成，不改用 invariant globalization 掩盖依赖。

Android USB 在线；官方 sdkmanager 把 NDK 29.0.14206865 安装到 `out/toolchains/android/ndk/29.0.14206865`，source.properties 与 android.toolchain.cmake 存在。新结构的正常源码 Mono AOT 构建、匹配 Android 契约内容及真机回归仍未开始，不复用旧临时 native targets。并行 Android UI 任务随后提交 `69a88546`；其截图/配色与单 ABI 设备证据属于独立 follow-up，下一次平台快照应纳入已提交修复。Windows 构建使用当时工作区，不能把该产物宣称为无其他改动的 faa9070e 冻结源码。

自动审批服务对 Windows 四宿主 GUI 探针和 Linux 续跑均返回 `You’ve hit your usage limit`，并明确动作未执行、不是不安全判定。没有以其他工具绕开审批；停止外部/原生执行后只做沙盒内的源文件语法、篡改 fixture 和产物只读检查。P6e 代码/记录保持未提交，等待审批服务恢复后完成平台退出条件。

证据：`out/cross-platform-p6e/windows-sdk/application-publish.json`、`windows-final-payload-check.json`、`publisher-tests.log`、`publisher-capabilities-final.log`、`windows-content.json`、`windows-export.log`、四份 `windows-install-*.log`；Linux 原生构建/首轮失败日志、源码归档及 `linux-root.txt` 同目录保留。Windows GUI 探针新增于 [probes/sample-hosts](probes/sample-hosts/README.md)，目前只有语法检查，无运行通过报告。独立模板/macOS/iOS/真实手柄继续延期。

## 2026-10-30：P6e1 权限恢复后的发布、桌面原生与 Android 正常源码候选（verified）

用户恢复执行权限后继续此前未执行动作。Windows 四个实际 Player/Developer 原生进程全部从非应用工作目录启动：确认包内 CoreCLR、SDL、VLC 路径、会话就绪、启动次数落盘；通过自有 HWND 的退出确认退出码均为 0。两个 Developer 使用实际发布 apphost，分别显式执行 3 条 TCP 外部调试用例，覆盖标题连接、start/变量修改/seek/step/stop/重建、旧 run 拒绝以及边查询边正常退出；两份 TRX 均 total/executed/passed=3，failed=0。原始 SDK 和发行布局的契约路径均由测试入口明确识别，没有用零执行显式场景作证据。

Linux 使用 `/tmp/galnet-build-compose-Q4qkZh/sdk` 的已核验 10.0.401，给 SDK 进程配置已有本机媒体中的 ICU78 目录后，保护测试实际执行 6 项通过、1 项 Windows-only skip；六应用 locked restore/publish/载荷检查通过。新源码原生音频由 gcc 编出。四份实际 Sample 分别安装由同一 Linux 产物契约导出的内容，在 Ubuntu 26.04.1/WSLg 启动、会话、进度落盘、退出确认及 exit 0 全通过。Player 不传参数，用独立 XDG_DATA_HOME 测默认 profile；Developer 用独立显式 profile。进程实际加载 SDK 输出的 CoreCLR/SDL 与本机媒体 VLC，未声称新的公开私有闭包、Ubuntu 24.04 最低基线或原生 Linux 桌面。

Android 在无 bin/obj 的 `69a88546` 源码归档中，经 SDK 10.0.401 和官方 NDK 29.0.14206865、CMake/Ninja 正常构建两 ABI 音频；未使用 P7b/P7c 临时 native targets。归档 SHA256 `d9443dc4d85fae2039e5733350f4dd4bc8ad33d9d241cc32f06bd2e2ad7b60a5`。首轮用原 Windows CLI 导出被 Core 严格字节检查拒绝；在相同快照/配置/版本正常构建 CLI 后，逐项核对 Core/Builtins 与 Android 实际三份契约相符，再成功导出 `GameTestCase-current.galpak`，SHA256 `b55113b68100ec0fbe85ee52c8a13be933f24b2203d8d85d392218b588dfe8ad`。未替换运行库或降低校验。

最终通用 APK 两 ABI 各 114 个实际 Mono AOT 程序集，所有 native 架构、ELF LOAD 和官方 zipalign 16 KB 检查通过。第一次覆盖安装被签名不匹配拒绝，旧包/数据不变；实际新证书 9b82c724…，既有手机候选和本机旧开发密钥证书 d2c39063…。显式传入既有签名参数后实际证书一致，见 L-20261030-16。一次按 ABI 拆分开关的输出仍只有双 ABI 文件，未把文件名或大小视作单 ABI 证据；改用单数 android-arm64 RID，在快照中 force-evaluate 再 locked restore/正常发布，确认实际只含 ARM64、114 个 AOT、完整内容及 native/对齐均通过。

| 当前结果 | 证据 |
| --- | --- |
| Windows 六应用 raw publish | `windows-sdk/application-publish.json`、加强后的只读 `windows-final-payload-check.json` |
| Windows 4/4 原生启动/确认退出/落盘 | `windows-native/verification.json`、各进程 modules/stdout/stderr；Player 默认 profile 为独立测试身份 |
| Windows 外部调试 3+3 | `native-debug.json`、`results/native-debug-{Sample,CrossPlatform}.trx` |
| 发布保护 | Windows 最终 7/7；Linux 6 passed + 1 skip；`publisher-tests-final.log`、`linux-publisher-tests.log` |
| Linux 六应用及 4/4 WSLg 原生 | `linux-application-publish.json`、`linux-native.json`；实际根 `/tmp/galnet-p6e-iv2N8V` |
| Android 双 ABI 正常源码 | `android-build.json`、`android-publish.log`、`android-package-verification.json`；114×2 Mono AOT，通用 APK hash b6452b3b… |
| Android ARM64 设备候选 | `android-arm64-publish.log`、`android-arm64-package-verification.json`、`android-arm64-signer.log`、`android-arm64-zipalign.log` |
| Android 覆盖安装与冷标题 | `android-arm64-install.log` Success，`android-device-smoke.json`、`android-cold.png`；没有 uninstall/clear |

ARM64 APK：`out/cross-platform-p6e/android-arm64/org.galnet.sample-Signed.apk`，78,191,374 字节，SHA256 `8ad2dc4ff83965faa32b6648348f31abee85b0daba7af1d7d9a29334120b4eb4`，音频库 SHA256 `7af68003ce4822e1f764b75790137ad60ee7a3aa5b237c47379431c72897468b`。本次是全新 NDK 源码产物，不沿用旧诊断 native。候选保留现有数据安装成功；冷标题显示新内容 SHA 对应目录、保留语言偏好、无退出按钮。窗口 InsetsSource 报告状态栏/导航栏 visible=false；竖屏的等比画布留白不被写成横屏全高通过。

后续捕获前台检查先发现已切离本包，未截取其他应用；之后发现 Dozing/安全锁屏，不能写成 Sample 黑屏。诊断为 Keyguard showing=true/secure=true/trusted=false，ADB 无法解除。已通知用户手动解锁；临时旋转锁恢复 free。本次新候选的横屏、2×Back、1×Home、剧情音视频和触摸复验尚待 P6e2，不复用旧 P7c 或 UI follow-up 结果代替。本阶段不标整个 P6e/P7 完成。

源边界：Linux/Android 及外部调试测试构建基于归档 69a88546，Windows raw publish 使用当时工作区（包含并行 UI 修复）；四组契约在各自上下文内严格匹配，不宣称 Windows/Linux/Android 契约可跨上下文交换。后续 `bad0bc73` 的 GameTestCase 作者化与 `2c7a520e` 等窗口外观改动不在该冻结候选中，也不被这批结果自动覆盖；并行未提交的剧情、窗口和 README 改动保留。当前规范/支持页、发布脚本说明与探针说明已同步，文档和脚本语法检查通过；未更新公开发行版本或外部 Wiki。

## 2026-10-30：P6e2 用户确认与当前范围收尾（verified）

用户解锁后，实际完成横屏标题、2×Back/1×Home 后 launcher 重进，均为 PID 16404；截图显示标题和无退出菜单，没有复现空白页。新游戏的课堂背景/立绘、对白和表情切换、触摸推进均有新候选截图，真实 AudioTrack 日志有 48 kHz 双声道非零采样数据。临时旋转锁在探针 finally 和收尾中恢复 free。

自动视频推进探针最后因前台已回桌面而未完成，保护逻辑没有截取桌面，也没有据此记视频通过。随后用户明确“我都测试过了，可以收尾了”，因此本轮音视频等剩余基础功能按用户人工验收通过收尾，不继续操作手机或补造自动证据。该确认限定本轮 Sample 功能，不推导生产签名、iOS、macOS、真实手柄或完整 P7 专项已通过。

新增证据：`android-device-smoke.json` 中的 landscape/back-1/home/back-2/playback-frame-1/playback-frame-2/touch-advance，相关 PNG/window 文件、`android-playback-runtime.log` 及 `android-device-acceptance.json`。P6b–P6e 当前 Sample 切片已归入 summary.md；剩余计划只保留延期/专项任务。独立审核未新增，review.md 的本次增量是实现自审与验收记录。

## 2026-10-30：preview CI 本地执行

应用 CI 已按当前范围收敛为 Windows/Linux 的 Editor、CLI、CrossPlatform Player、CrossPlatform Developer raw publish，独立 NuGet 外部消费者门禁，以及 Android ARM64 的无调试、内置 GameTestCase Mono AOT。此轮本地复验中，应用载荷检查 7/7、发布 guard 17 项、release plan 19 项、Windows 四个真实 raw publish，以及包含 `GalNet.Avalonia.Debugging` 的六个 NuGet 包和五类外部消费者均通过。

Android 初次执行依次暴露并修正三类不应被绕过的问题：不能以单 ABI 覆盖提交的双 ABI locked restore 图；导出工具和 Android 契约必须使用同一 preview 版本与 `ContinuousIntegrationBuild`，以保持含源生成 Contracts.Builtins 的严格字节身份；CLI 导出文件名必须读取其结构化 `packagePath`，不能假定项目目录名。最终 `out/local-ci-preview-20261030/android-arm64-final8/android-publish.json` 通过：SDK 10.0.401、locked restore、三份内容/Android 运行时契约逐字节匹配、完整 galpak、ARM64-only APK、114 个 Mono AOT 程序集、无桌面 SDL、原生 ABI 与 APK/ELF 16 KB 对齐均已验证。APK SHA256 为 `8aff008dd03d47e7d71b7e439c64557ec3dab254c7d6d73810cb9c4daddb27bb`。

此记录仅证明本地 preview 构建和载荷门禁；Windows/Linux GitHub runner 的 PR 矩阵、正式 release tag 及真实设备/桌面验收仍需分别执行。

## 2026-10-30：preview release 只保留构建与产物门禁

`v0.1.0-preview.3` 已从 `dev` 合并至 `master` 后打 annotated tag 并触发 release。远端先成功校验精确 tag 与 `master` 归属；原 release job 之后运行完整 `BuildTest` 而失败，导致构建、封存与上传被跳过。

用户确认 release 只需构建正式版本与验证可发布产物。故删除 release 中的完整测试步骤；常规 CI/PR 仍负责测试，release 保留 tag/版本、NuGet 与应用产物、消费者、ZIP/载荷验证、封存及上传门禁。此项是发布职责收敛，不把失败测试伪装为通过或降低发布产物校验；详见 L-20261030-19。
