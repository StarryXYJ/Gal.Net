# GalNet Agent Knowledge
- 2026-10-30 跨平台推进：用户将手柄/macOS/iOS 验收后移，允许移动 Mono AOT，不以 NativeAOT 或延期平台阻挡共享宿主/Desktop/Android。当前 Ubuntu 26.04.1/WSLg 以核验 SDK 10.0.401、干净源码快照完成 Linux 原生 build-compose（galpak/目录及仓库外独立宿主）；三包 hash/权限/契约、两种 Sample 原生会话与确认退出、Windows/Linux 保护测试各 4 项通过。媒体仅本机开发探针，24.04 基线/完整媒体仍待验收；证据见跨平台 feature 执行记录及 probes/source-compose。
- [SDK 名称大小写](lessons/L-20261030-02-sdk-name-case.md)：Microsoft.NET.Sdk 使用标准大小写；Windows cross-publish 不能代替 Linux 原生 SDK。隔离 SDK/无 bin/obj 源快照揭示并修复 Editor 的 MSB4236。
- 2026-10-30 桌面手柄：GameHost 已用自有窄绑定 SDL3 替换 XInput，固定 native 包 3.4.18.1 / SDL 3.4.18；UI 主线程共享 input 租约，失焦/断连 Cancel、回中立后重新武装，标签不改位置绑定。Windows/WSLg 正式探针各 3×40 与缺库降级、外部 NuGet 资产/许可传递通过；真实手柄、macOS/移动仍待验收。见 control.md 与跨平台 feature 总结。
- [SDL 输入事件所有权](lessons/L-20261030-01-sdl-input-event-ownership.md)：状态轮询也须清理自有 joystick/gamepad 输入事件；只清 0x600–0x6ff，保留无关事件，GalNet 主线程共享租约，拒绝接管外部 input owner。
- [源码发布的 profile 还原图](lessons/L-20261029-16-source-build-restore-graphs.md)：SDK/restore/publish 分步记录；Desktop 使用单数 RuntimeIdentifier，现有 WindowsReadyToRun 沿用 -r 锁图，不关闭 locked restore。
- [跨主机 so 筛选](lessons/L-20261029-17-cross-host-shared-object-filter.md)：Windows 的 *.so* 会误匹配 System.Net.Sockets.dll；用 Ordinal 的 .so/.so. 文件名规则再验 ELF。

- [Windows 原子发布句柄冲突](lessons/L-20261029-15-windows-publication-sharing.md)：独立 Move 探针确认短暂冲突；FilePublication 仅 Windows 5/32/33 有界重试，保持原子性与取消，回滚不继承取消。真实句柄回归及两种分发 40 次导出通过。

- [WSLg 图形驱动选择](lessons/L-20261029-14-wsl-renderer-selection.md)：当前 Ubuntu 24.04.3 默认 llvmpipe，进程级 GALLIUM_DRIVER=d3d12 已确认 Intel UHD 硬件驱动及 Sample 启动/退出；scripts/samples/run-wsl-sample.sh 保留原 manual-profile。用户随后确认新入口 blur 人工复测通过、音频正常；尚无帧耗时测量。

- Linux 本地候选入口：scripts/release/Publish-Linux.ps1、prepare-linux-media.py、package-linux.py；Ubuntu 24.04.3 / WSLg 已验证 tar 权限/hash、独立 Editor/Sample 正常退出、包内 VLC/FFmpeg 像素与 CLI 两种导出/安装。许可及对应源码可用性、原生 Linux 桌面/完整媒体仍待验收；可选 .NET LTTng 追踪不在支持范围。见 docs/manual/release-packaging.md。

- [跨平台默认字体](lessons/L-20261029-13-portable-default-font.md)：UI 跟随宿主默认字体，显式 CJK 回退统一优先 Noto/思源黑体（用户要求避免专有字体依赖）；Sample 显式选择已注册的内置 Inter，不能假定 WithInterFont 自动改变默认。Ubuntu 24.04.3 / WSLg 已验证窗口、会话及确认退出；Inter 不含中文，中文显示仍需 CJK 字体。

- 2026-10-29 跨平台 P3a：剧情像素视频、Gallery 播放/缩略图、Editor 音频预览均通过 Media.LibVlc.LibVlcFactory 初始化；singleton 配置冻结 native/plugin 路径，各播放器仍独立释放。Windows 私有 VLC 已原生验证；Linux 默认 system loader 仅为开发基线，Core.Initialize(directory) 不支持 Linux，不能据此声称私有部署成功。Editor 默认契约目录须显式部署 Core、Builtins、Project.Contracts 完整闭包。

- 2026-10-29 跨平台发行工具：compose 支持 win-x64 ZIP/目录、linux-x64 tar.gz/Unix 目录与完整内容，保持布局 v1 和严格契约；build-compose 显式 csproj/apphost/版本，经 SDK、锁定还原和普通自包含发布封装后复用 compose。Windows 宿主到 Windows/Linux 目标的真实 Sample 均已解包、验 hash、启动并正常退出；Editor.Shared 515 项通过，取消会停止自有 SDK 进程树。Linux 宿主 SDK、定制工程矩阵和 macOS 仍待验收；旧 dev.9/dev.10 候选未更新。已完成总结和剩余计划见 features/F-20261029-05-cross-platform-applications/。
- [SkiaSharp 与 R2R 原生字体验收](lessons/L-20261029-12-skia-r2r-native-fonts.md)：当前 win-x64/.NET 10/SkiaSharp 3.119.4 发布仅将 SkiaSharp 保持 IL，应用继续 R2R；真实窗口连续启动/正常退出不能由 PE 头检查替代。

- 2026-10-29 跨平台 feature P1：用户明确不考虑向后兼容；galpak/目录统一包含全部剧情分片，installation.json v4 索引基础 PAK、localization.pak 和剧情 PAK，content/GPAK 仍 v3。安装器只接受完整 v4，不再提供旧归档安装或旁置剧情导出。Editor 仅提供 galpak/内容文件夹，目录模式在所选输出目录创建 pak；资源覆盖补丁显式独立导出。早期 v3 封装记录中的旧归档兼容与旁置剧情规则已被本项取代。见 features/F-20261029-05-cross-platform-applications。
- [重复安装的源完整性](lessons/L-20261029-11-reinstall-source-integrity.md)：命中正确安装目录仍须验证本次归档 payload hash，不能以目标缓存命中替代源完整性校验。

- [模态背景静态标题](lessons/L-20261029-10-disabled-title-foreground.md)：禁用背景容器会改变标题继承的前景画刷；静态标题显式绑定 TextPrimary，弹窗验证须挂载真实主页并检查 Color/Opacity，不能只截空背景。

- 2026-10-29 CI 发布门禁：release.yml 的 validate-source 在 tag push 后先 fetch master，Test-ReleaseRef.ps1 校验规范版本 tag、checkout HEAD 和 origin/master 祖先归属；build 依赖门禁，publish 只允许 tag push 且 build 成功。主分支普通 push 只跑 CI，手动运行只生成候选。Test-ReleaseGuards.ps1 的 17 项用例覆盖真实临时 Git 图和模拟七 ZIP 上传；Push 在创建 draft 前检查七 ZIP。见 docs/manual/release-packaging.md。

- 2026-10-29 内置文档封装：新 galpak v3 固定 pak/game.pak 入口，含 content v3、installation 索引、settings、Gallery、characters、layout 六文档；localization.pak 每文化一个平铺文本条目。使用独立 document GPAK catalog，不加入资源覆盖。新安装 I18n/<culture> 仅外置覆盖；作者与 content v2 旧安装仍用 I18n/External。导出/安装保留 I18n、ResourcePatches、StoryPatches/<buildId> 空目录，ZIP 显式保存；兼容 v1/v2 galpak 和 content v2 读取。只读 CLI game inspect 供脚本读取 packed/legacy 描述；Sample profile 身份从 game.pak 读取 projectId。见 docs/spec/file-formats.md、docs/spec/i18n.md、features/F-20261029-02-packed-game-documents。
- [损坏内容与启动探测](lessons/L-20261029-08-invalid-data-startup-fallback.md)：InvalidDataException 不派生 IOException；profile 身份探测显式回退，正式内容加载仍报错。

- [自包含平台程序集](lessons/L-20261029-07-self-contained-platform-assemblies.md)：自包含 lib 混放 framework 与业务 DLL；契约依赖收集按 runtimepack deps 识别平台归属，不能按 corelib 同目录排除 Core/Builtins。

- 2026-10-29 桌面发行布局：应用根只留入口 EXE 和目录；SDK apphost 直接指向 lib/<app>.dll，运行时与原生依赖都在 lib，清单/说明/许可在 docs。Sample 默认加载同级 pak，用户 profile 按内容 projectId 的摘要定位；玩家关闭普通参数，EnableLaunchArguments 默认跟随 EnableGameDebugging，开发发行加 -SampleFlavor Developer。galpak 统一 pak/ 外层、内部路径相对内容根；安装显式目标不追加第二层 pak。见 docs/manual/release-packaging.md、docs/spec/file-formats.md 与 features/F-20261029-01-desktop-package-layout。
- [原生退出确认](lessons/L-20261029-06-native-exit-confirmation.md)：WM_CLOSE 只打开确认遮罩；测试须在自有 HWND 确认退出，再验证零退出码和落盘，不能用强杀替代。

- [预览效果像素边界](lessons/L-20261029-04-bounded-scene-capture.md)：外层缩图不限制 CPU 效果纹理；逻辑尺寸与像素尺寸分开，正式截图仍保持完整分辨率。
- [存档缩略图通知](lessons/L-20261029-05-save-thumbnail-notifications.md)：整表一次 Reset，保存单槽 Replace，其他行与 Bitmap 复用；异步 I/O 不会消除重复解码卡顿。

- [PowerShell 宿主 API 兼容](lessons/L-20261029-03-powershell-runtime-api-compatibility.md)：Windows PowerShell 5.1 使用 .NET Framework；现代 System.IO.Path API 不能假定存在，需在 powershell.exe 与 pwsh 分别验证。

- [窗口装饰与玩家配色](lessons/L-20261029-02-window-decoration-theme-resources.md)：Ursa 2.1 关闭按钮在独立 WindowDrawnDecorations；窗口共享调色板资源，完整 Fluent 样式保留在播放器根。Headless 不生成平台装饰，资源断言不能替代原生截图。

- [持久字段与保存基线](lessons/L-20261029-01-content-baseline-field-completeness.md)：新增字段必须进入手写相等比较；仅改 Group.storyPackage 应 dirty，局部保存其他配置不能抹掉它。

- 2026-10-28 内容打包：项目唯一 Graph，graph/group 为保留 ID 资源；GPAK v3、content v2、save v5。本体闭包先行，各剧情包只减本体；默认 128 MiB 未压缩软阈值（用户后续调大），整片缓冲不保证内存上限。资源矩阵定义在 settings 的 galnet.resourceOverrides，矩阵修改经项目事务即时持久化且撤销/重做落盘，独立导出不 Save All，运行 priority/hash 降序显式映射、会话冻结。图集旧档按覆盖来源重新解析选择意图。文本/人物整体在本体，存档保守要求准备时全部剧情包；Gallery 补丁延期。见 docs/manual/content-packaging.md、features/F-20261028-01-content-packaging/summary.md。

- [可选参数与二进制 API](lessons/L-20261028-04-optional-parameter-binary-api.md)：新增可选参数仍改变 CLR 签名；公开契约保留旧签名转发，并用旧 consumer 装载当前 Core 验证。

- [内部快照内容身份](lessons/L-20261028-03-internal-snapshot-content-identity.md)：新增存档身份须覆盖调试 replay、缓存和精简工厂；用非空构建身份验证，不放宽外部恢复门禁。

- [PAK 索引与 payload 验证](lessons/L-20261028-02-pak-index-payload-verification.md)：MetadataIndex 不校验普通 payload；独立覆盖包准备需逐项验证，当前文件 hash 排序不能代替已知校验和。

- [原生发布载荷](lessons/L-20261028-01-native-publish-payload.md)：RID/托管符号开关不能保证 native NuGet 内容自动裁掉多余架构与 PDB；在 manifest 前过滤最终应用载荷，并验收所需 x64 native 文件仍在。

- 本地 CI 入口为 `scripts/ci/Invoke-CI.ps1`（默认 All，可 Coverage），Actions 调用同一脚本；SDK 精确固定为 global.json 版本。`scripts/release/Release.ps1` 生成并验收 5 个聚合 NuGet 与 Windows x64 R2R 的 Editor、纯/整合 Sample、Contracts 四 ZIP；包归属见 packaging/packages.json，操作见 docs/manual/release-packaging.md。桌面 Sample AOT 尚未通过严格链接，不能从 Headless 自检推导支持；F-20261027-04 的 summary 保留未完成项。

- [Solution fixture 配置](lessons/L-20261027-11-solution-fixture-configuration.md)：solution 外引用工程可能回到 Debug，测试却读取 Release；契约 fixture 已加入 GalNet.slnx，验证构建日志和实际输出配置。
- [源生成 init 默认值](lessons/L-20261027-12-generated-init-defaults.md)：音轨/Bus 精简 JSON 会丢失 init 属性初始化值；显式 JsonConstructor 可选参数保留容量/增益默认值，不放宽校验或改成可变 DTO。

- [开放 shader 参数](lessons/L-20261027-10-open-shader-parameters.md)：已知 shader 的额外参数是合法、不解释的 JSON，保留且不 warning；声明字段仍校验，未知 shader 与封闭契约仍诊断。

- [属性逻辑路径与 wire 边界](lessons/L-20261027-06-property-wire-boundaries.md)：JSON Pointer 不能穿过 JSON 字符串；引用识别、比较、复制与回写须同时迁移类型化路径。JsonArray 递归修改返回原节点时不能再次挂载该实例。

- [数值模板与作者草稿](lessons/L-20261027-05-numeric-template-drafts.md)：NumericUpDown 的模板/Value 初始化不能成为作者输入；借用主题时由字段上下文拥有原始文字与提交，TextBox.PropertyChanged 同步捕获，验证挂载无草稿及输入后立即 Enter。

- 2026-10-27 Group Problems 按作者历史/相关依赖自动检查内存工作版，代次阻止旧结果发布，合法待提交不计条目错误。Editor numeric 默认保留文字的上下框，显式 numericupdown/numerictext 与 slider+精确输入；long/ulong/decimal 保留文本。前导小数点仅在类型化数字编辑入口规范化，JSON 源码仍严格。规范见 docs/spec/editor-groups.md、control.md；证据见 features/F-20261023-01-editor-uiux/follow-up-group-diagnostics-numeric-20261027.md。

- 2026-10-27 音频创作：类型 ComboBox/目录参数表单，apply 隐藏自动身份，stop/update 选择剧情位置之前的直接挂载实例；显式 all 与参数 patch 更新已实现。复用 Scene 作者语义、Graph/前缀分析和项目查询，AudioEffect 域按 scope/target/instance 限定，AudioEffects/AudioComplete/AudioRevision 独立于视觉候选。见 docs/spec/editor-scene-targets.md、docs/manual/audio-mixing.md、features/F-20261027-01-audio-mixer/authoring-summary.md；既有 Timeline Flyout 测试保留。

- [构建完成后再启动 no-build 测试](lessons/L-20261027-04-build-before-no-build-tests.md)：session_id 表示仍在运行；同一输出根的构建/测试重叠会锁定 DLL 并可能验证旧产物，必须确认 build exit_code=0 后再启动测试。

- [Timeline 停止与事件收尾](lessons/L-20261027-03-timeline-stop-event-cleanup.md)：完成轨道不等于执行末帧事件；停止需等待完整收尾，循环初始化/迭代/结束清理分别处理，初始化期间的停止也必须生效。

- 2026-10-27 音频 PCM：实例拥有 gain/淡入淡出/结束处理，Track 管理顺序/随机和循环队列，Bus 实际汇总后执行共享效果。Media.Audio 为跨平台混音/DSP，Media.Miniaudio 为 Windows x64 设备/解码；LibVLC 仅保留视频/Gallery。默认项目 section v2（audioBuses/busId），音频状态 tracks/seffects v2、channels v1；SEffect scope/targetId 与真实滤波/混响见 docs/manual/audio-mixing.md。实现和验收限制见 features/F-20261027-01-audio-mixer/summary.md。

- [分数延迟环绕边界](lessons/L-20261027-02-fractional-audio-delay-wrap.md)：float 负分数位置加长度后可能舍入到长度本身；使用 double 并复核上边界，默认参数与非默认参数均验证。

- 2026-10-27 默认视觉库：模板部署为项目 shader 资产，defaultVisualPrograms 仅用于创作填充；预设保存显式 program/blurProgram GUID 并编译为 timeline.play。CPU/GPU 层链注入当前 layerZ，texture 参数租约归效果实例。project visuals install 显式补入且不覆盖已有绑定/占用路径。见 docs/manual/default-visuals.md 和 features/F-20261026-03-default-visual-library；[采样像素与视觉 QA](lessons/L-20261027-01-shader-sampling-visual-qa.md)。

- 2026-10-26 Sample 扩展边界：默认会话通过 IAvaloniaPlayerSettings/IPlayerStartup 消费项目偏好；默认页面 VM 可扩展、DI 使用 TryAdd。AttachPlayer/AttachRun 接入项目借用绑定并优先异步释放。外部剧情变量走 Engine.SetVariablesAsync/UpdateVariablesAsync，同步提交显式 scope 值并合入稳定快照；业务模块自行定义复杂状态恢复。AvaloniaPlayerLocalization 归 Avalonia.GameHost，中立 JSON 查询归 GameHost，Sample 不再共享 GameLocalizationProvider 源文件。见 features/F-20261026-02-sample-extensibility 与 docs/manual/customize-avalonia-sample.md。

- [Ready 与宿主活动状态](lessons/L-20261026-06-ready-is-not-session-end.md)：Ready 是恢复/Seek 的中间准备状态，默认宿主保留已有 IsPlaying，不应触发返回标题。

- [原生 Sample 窗口与显式用例验证](lessons/L-20261026-05-native-sample-window-validation.md)：Process.MainWindowHandle 可能选择无标题 native helper；仅对自有 PID 的明确 Sample 窗口发 Close，并核对显式测试实际执行数。

- [配置 schema 与 JSON 字段身份](lessons/L-20261026-04-configuration-json-casing.md)：精确 ID 校验后须同样按大小写精确解码 DTO，避免未知大小写字段覆盖已验证值；原始未知字段继续保留。

- [查询事实与写入上下文](lessons/L-20261026-03-query-result-and-write-context.md)：候选结果版本不能代替写入方身份/基线版本；异步准备按局部作者依赖与资源代次续租，相关变化再撤销不能复活旧选择。

- [作者代次与视图生命周期](lessons/L-20261026-02-author-incarnation-view-lifetime.md)：作者删除/换类型使旧绑定失效；页面关闭只释放表单，不推进作者 incarnation，项目草稿须跨页面继续共享并可保存。

- [基线对照恢复与增量时间戳](lessons/L-20261026-01-baseline-restore-timestamps.md)：恢复源码备份可能保留早于基线 DLL 的时间戳，候选验证需独立输出、Rebuild 或显式失效增量构建，并核对源字节。

- [空本地化引用边界一致性](lessons/L-20261025-10-empty-localization-references.md)：作者索引、启动加载和运行解析都将空引用按空文本处理；空对象/数组不触发 opaque，非空未知结构仍保留诊断。

- [资源结构与缓冲重基](lessons/L-20261025-01-variant-buffer-structural-rebase.md)：ManagedContent 的写盘内容来自项目文档 Data；移动时内部 folder 要分别重基工作版和保存版，Undo/Redo 保留之后保存的其他配置。

- [必填字符串与 schema 默认值](lessons/L-20261024-01-required-string-defaults.md)：Required 字段有默认值仍会被 Normalize 补齐；要求显式提供且空串合法时不声明 schema 默认，并显式 AllowNull=false。DTO 构造函数赋值不导出为 schema 默认。

- [契约与 Git 构建元数据](lessons/L-20261023-02-contract-git-metadata.md)：SDK 的 InformationalVersion 可包含 commit；严格 DLL 哈希需要稳定非功能元数据，并用不同 SourceRevisionId 重建比较验证。

- [调试 Step 与异步呈现](lessons/L-20261023-02-debug-step-async-presentation.md)：Step 响应不保证异步截图等呈现任务已完成；按具体完成信号有限等待，CatchUp 不产生截图副作用。

- [启动日志前的调试失败](lessons/L-20261023-01-debug-launch-before-logging.md)：契约校验可早于日志初始化；App 顶层需 stderr/非零退出码，Editor 需监控自己启动的进程，不能只等待 TCP。

- [选择器准备与作者代次](lessons/L-20261022-02-selector-preparation-generation.md)：同代后台准备可推进结果 Revision；等待完成后按不可变作者快照身份与资源代次复核，再更新选择快照。

- [变量桥接原键与 UID](lessons/L-20261022-01-variable-bridge-original-key.md)：Runtime 去作用域前缀，回写已有玩家/系统变量必须保留桥接服务原键；仅 Runtime 改值通过不能证明重开/落盘正确。

- [窗口关闭晚于 scope 释放](lessons/L-20261021-01-window-closed-after-scope-disposal.md)：异步 Closing 先释放 scope 时，Closed 不能再 GetRequiredService；窗口绑定应在释放前幂等解绑，并用真实进程正常退出验证。

- [复合分析原子发布](lessons/L-20261020-04-composite-analysis-publication.md)：容错 Timeline 分析可能返回半份结果；复合条目先在临时事实中验证本次完整性，成功后发布，保留上游不确定性。

- [跨宿主 DTO 表单样式](lessons/L-20261020-01-schema-form-host-style.md)：新 Dock/Flyout 要显式提供 SchemaForm 模板；Host.Diagnostics 不替代实际字段与子诊断检查。

- [隔离构建的契约产物](lessons/L-20261019-03-isolated-contract-artifacts.md)：默认契约 Content 和 schema fixture 固定读取普通 bin；全新 artifacts 快照要用同源码产物补齐，不能借用旧 DLL。

- [精确关键帧边界](lessons/L-20261019-02-timeline-exact-key-frame.md)：Step 段需验证内部键及前后分数帧；Editor 直接共用 Runtime sampler，不用时间偏移掩盖边界错误。

- [延迟模板祖先类型解析](lessons/L-20261019-01-template-ancestor-type-resolution.md)：带命名空间的祖先反射绑定必须实际挂载验证；已复现表达式使用 RelativeSource + x:Type 修复。

- [查询上下文随项目 scope 失效](lessons/L-20261018-04-query-scope-disposal.md)：旧服务仍可能持有 IsLoaded 文档；查询服务释放时清缓存并推进代次，不能以可读数据代替有效会话。

- [L-20261018-03 派生控件主题键](lessons/L-20261018-03-derived-control-theme-key.md)：复用 Expander 等基类模板时显式 StyleKeyOverride；挂载后验证实际交互入口，属性赋值测试不能证明按钮可用。

这是 GalNet 项目的长期 agent 上下文入口。这里只保存经过验证、以后工作仍然有用的项目事实、边界和经验索引；不要写入对当前任务没有复用价值的对话流水账、秘密或未经确认的猜测。

## 使用方式

开始工作时先读取本文件，再按当前任务读取仓库 `.agents/lessons/` 中相关的经验和 `.agents/skills/` 中匹配的 skill。细节以仓库 `docs/` 下的正式文档为准。

经验在发现时立即记录，feature 结束时再整理和晋升；不要等到上下文结束才回忆。

## 项目事实（已从仓库文档确认）

- 宿主分层已落地：GameHost 提供 GameSession/IGameRun/GameRun、PlayerSession、PlayerPreferences/PlayerStartup；Avalonia.GameHost 提供默认页面会话、运行绑定、展示组合、资源预加载和设置/输入/窗口适配。Sample 仅保留项目配置、启动适配、本地化和调试。中立应用接口在 Presentation.Abstractions.Application，共享音频混音/DSP 在 Media.Audio，Windows 适配在 Media.Miniaudio；项目删除与 ProjectTools.Windows 已移除。见 docs/spec/architecture.md。

- 解决方案入口是 `GalNet.slnx`。
- `docs/spec/` 描述已经实现的稳定事实；`docs/manual/` 描述二次开发路径；`features/` 保留实现过程和历史。
- Runtime/Core 应保持与 Avalonia、WPF、Skia、具体文件系统和其他平台实现解耦。
- Entry 契约与执行已分离：IEntryModule 只含 schema/composite，IPrimitiveModule 按领域共享状态并贡献完整指令 ID；GameRegistration 独立注册，AddBuiltins 为完整内置运行入口；独立 GalNet.Contracts.Builtins 只依赖 Core，CreateAllModules 返回 14 个组件模块，包含 custom.invoke 的固定双字符串 schema。可选 schema、部分内置实现或全自定义；仅完整 ID 排他。同前缀和 schema 模块名可重复。音频用 AudioRuntime + IAudioPlaybackHost 组合，不强制继承。见 docs/spec/runtime.md、docs/spec/entry-types.md 和 docs/manual/。
- 当前 GameSnapshot 版本为 4，不兼容旧版；持久数据在注册式 GameState 表中，快照为 StateId → version/JSON payload；读档先验证，再取消旧 scope 并替换整个状态根。Engine 不拥有执行 view，宿主先释放 Engine，再异步释放 view 与后端。
- Engine 提供终态 StopAsync 和 StateChanged，覆盖玩家推进、Choice 与阻塞实例自然完成；GameHost 仅协调应用会话，不复制 Engine 执行机制。AvaloniaGamePresenters 只组合端口，AvaloniaGamePresentation 管理平台资源。见 docs/spec/runtime.md 和 docs/spec/architecture.md。
- Presentation.Defaults 已删除，空 NullGameView 位于展示抽象的 View 命名空间；当前程序集布局见 docs/spec/architecture.md。GameContent.Configuration 保存版本化 section 文档；默认作者模型位于可选 Contracts.Builtins，项目/安装加载器通过 GameContentLoadOptions 接收 schema 与文化代码投影。
- 旧的 `GalNet.Control` 与 `GalNet.Control.Abstraction` 已完成迁移并删除；当前共享 Avalonia 游戏页面唯一入口是 `GalNet.Avalonia.GameView`，页面架构见 `docs/spec/control.md`。
- 持久条目先在 `GameState.Update` 中修改唯一数据记录，再通知展示层；模块通过 PrimitiveRegistry.State/Restore/ValidateState 声明状态、恢复依赖和校验，宿主使用展示抽象 StateRestoreSession 统一恢复、等待和逆序清理。旧 SceneState/Active* 镜像与 Builtins 中央 replay 已删除。
- 视觉扩展应通过展示抽象和渲染端口接入；不要把平台类型或渲染资源泄漏进 Core/Runtime。
- 新增 feature 的长期实现意图记录在其自身 feature 目录；只有已验证的稳定事实才合并到 docs/spec/。

- 玩家变量桥接实现为 Runtime.Variables.PlayerVariableService，存储通过 IPlayerVariableStore 注入；Variable.Clone 保留 Uid，VariableValue 不可变，可共享。Runtime loader/SaveManager 不执行文件 I/O。见 docs/spec/architecture.md。
- Runtime.I18n.GameTextResolver 提供 culture-first、父文化与默认文化回退，只依赖宿主查询委托；中立 JSON 文本源归 GameHost.Content，DynamicLocalization 适配归 Avalonia.GameHost.AvaloniaPlayerLocalization。见 docs/spec/i18n.md。
- Assets 的 AssetFileMaterializer 经 IAssetManager acquire 字节实体化媒体；Core 的 AssetFileLease 持有文件租约。Sample 将同一实例供音频/视频/Gallery 使用，最后租约释放才删除文件。见 docs/spec/assets.md。
- 已读进度 MarkRead 只写 Runtime ReadProgressIndex；文件实现默认 2 秒批量提交，失败保留 dirty revision 并重试。Engine StopAsync、Sample 存档和进度对象销毁均等待 flush，失败向调用者传播；后台失败可读 LastPersistenceError。见 docs/spec/runtime.md。

- 核心重构当前边界：Runtime 仅引用 Core；场景/音频/Gallery/差分图片契约与纯算法归 Contracts.Builtins，音频执行与按 GameState 根隔离的场景管理归 Primitives.Builtins。Core 的 keySpace 身份索引按状态注册选择，不认识场景类型。GameContent/加载配置与 Gallery 玩家组合归 GameHost。见 docs/spec/architecture.md。
- 项目模型与运行输入：ProjectConfiguration 提供版本化 section/普通 DTO，Editor 通用表单、命令、历史、保存、导出和安装使用项目 schema，未知 JSON 保留。宿主通过 SelectProjectOptions 显式投影，Engine 只消费 PlaybackOptions，音频只消费 AudioConfiguration/AudioMixOptions，文本直接接收速度。变量声明只保存在作者 Graph。见 docs/manual/new-presentation-layer.md。
- AudioConfiguration/AudioMixOptions 位于 Contracts.Builtins.Audio，允许任意 Bus/Track 及空/未路由 Track；混音为 master/bus/track，快进保留由显式 PreserveDuringCatchUp 控制。Sample/模板显式选择三 Bus 示例，默认模块不注入固定轨道。
- EntryDefinition.DynamicParameters 是唯一参数源，Parameters/Defaults/Options 副本及旧 ABI 适配已删除；具体编辑器投影位于 Editor.Abstraction.Schemas，Effect/SEffect 基础约束共用 ValueValidation，领域 stage/scope 继续分别拥有。

## 文档分层

- `features/<feature-id>/`：一次实现的需求、设计、阶段计划、审核和人类可读总结。
- `.agents/lessons/`：agent 专用的踩坑记录。
- `.agents/skills/`：项目 skill 的可版本化源文件；可复用经验晋升后在这里形成独立 skill。
- Codex 的用户级 skill 发现目录保存运行时副本；源文件变化后使用 `galnet-sync-skills` 同步，避免 agent 使用旧版本。
- `docs/spec/`：当前系统事实。
- `docs/manual/`：二次开发流程与框架接入指南。

## 当前工作

- 2026-10-19 P6e3b 已验证：属性展开为内联值曲线，右键插值/左右侧切线模式、持久自动贝塞尔重算、联动/断开和精确输入弹窗取代独立详情区；共享 Avalonia 控件直接调用 Core sampler，Step 精确内部键边界已修正。editorTangents 只属于作者模式，Runtime 消费烘焙斜率；值/柄手势预览、松手单历史与过期取消，关闭弹窗释放绑定和草稿。隔离候选六项目 810 项、弹窗收尾 Editor 184 项、构建/格式/指纹通过，六项媒体未执行；主工作区另外的控件改动和首次三个非 Timeline 失败保留。P6e3 verified，P6e4/P6f 后续，见 features/F-20261015-02-editor-authoring-workspace/phase-6e3.md。

- 2026-10-19 Group 属性编辑器改造已验证：统一响应式字段行、标题左上/重置同行右侧，去掉常驻 presence 操作；Transform 成员与类型工厂、ColorPicker、Timeline Dock 按钮，列表按元素类型分派。CanReset/HostContext 经 DTO scope 转发，Timeline 无默认定义禁止通用重置；子项 patch 保留未知 JSON，变换按语义相等保留无关更新下的草稿。802 项相关回归、解决方案构建、格式/源码指纹通过，六项媒体未执行，四语 727 keys。见 [实施记录](../features/F-20261015-02-editor-authoring-workspace/phase-group-properties.md)。动态结构 JSON 回退、八层/200 元素保护及 P6e3b/P6e4/P6f 范围保留。

- 2026-10-19 P6e3a 已验证：选中关键帧的值/出段 Step/Linear/Hermite/入出切线、混合值、多键单历史、平坦/线性/平滑/对齐一次性预设与值/秒显示换算；保存仍值/帧，切 Step/Linear 保留切线。详情按文档代次与选择失效，保留未知 JSON/大小写/首帧，越界/斜率溢出整批拒绝；布局预留不因选择移动画布。解决方案构建、Editor/Core/Architecture 332 项、格式/指纹通过，四语 705 keys，见 [phase-6e3](../features/F-20261015-02-editor-authoring-workspace/phase-6e3.md)。P6e3b 图形曲线/拖柄仍 planned，P6e4/P6f 随后；没有新增 Runtime 的极值拒绝规则或实时预览。开工已有的 feature.md/Group 属性设计改动保留未提交。

- 2026-10-18 P6e2b 已验证：ProjectSceneTargets 是指定 Group/Entry 位置的查询服务，缓存作者快照并随项目 scope 释放失效；SceneTargetCatalog/Selection 适配候选与 Locator 解析，控件自己管理弹窗。Assets/普通字段/Timeline 共用 ResourcePickerPanel。Timeline 顶部“＋”只选对象，对象行左侧“＋”逐项选属性；未知初值待明确输入才创建，空对象仅面板草稿。整个对象替换保留键/未知 JSON、单历史。初轮六项目 905 项、用户两步交互修订后最终 461 项相关回归、解决方案构建及格式/指纹通过；6 项原生媒体用例未执行，四语 691 keys。见 [scene-locator-plan](../features/F-20261015-02-editor-authoring-workspace/scene-locator-plan.md)。下一步 P6e3 曲线/切线，P6e4 剩余语义/事件/引用；实时预览不列阶段。

- 2026-10-18 Timeline 交互简化已验证，取代 P6e2 原保秒重定时/裁剪方案：已有轨道持续显示绝对值/叠加偏移模式，切换保留数值与切线；结束帧/FPS Enter/失焦独立提交、FPS 保留帧号、结束帧下限覆盖键/事件，越界拖动与延长合为单历史。旧应用/裁剪/保秒生产路径移除，四语各 678 keys。初轮六项目 901 项通过，模式拒绝回显补充后的最终 457 项、解决方案构建、格式/指纹通过，6 项媒体环境未执行；见 [timeline-simplification](../features/F-20261015-02-editor-authoring-workspace/timeline-simplification.md)。下一步 P6e3 曲线/切线。

- 2026-10-18 P6e2 用户反馈修正已验证：播放头拖动只更新 DisplayFrame 预览，松手才定位/采样一次，取消/版本失效不提交；Timeline 复选框居中、NumericUpDown 实际模板按钮长按 250ms 后每 30ms 连发。457 项受影响回归、解决方案构建、格式/指纹通过，6 项原生媒体环境未执行，见 [phase-6e2 修正记录](../features/F-20261015-02-editor-authoring-workspace/phase-6e2.md#用户反馈修正播放头与控件交互)。

- 2026-10-18 P6e2 已验证：共享 Avalonia TimelineCanvas/Model 与 Editor 无损 JSON 修改分层，树/画布共用扁平可见行和纵向滚动；关键帧多选/框选/整帧移动、首键保护/明确碰撞替换、时长/FPS/保秒重定时与裁剪均单历史，源变化取消旧手势。六项目 901 项回归、解决方案构建、格式/源码指纹通过；6 项原生媒体环境未执行。见 [phase-6e2](../features/F-20261015-02-editor-authoring-workspace/phase-6e2.md)。下一步 P6e3 曲线/切线，P6e4 事件/剩余状态与引用，实时预览不列阶段。

- 2026-10-18 P6e1 已验证：Editor.Shared 的 SceneTargetAnalysis 对作者快照做纯有限事实分析，Group 入口固定点、确定交/可能并、条件/未知语义/循环预算与跨类型身份冲突保守处理；ProjectSceneTargets 整代缓存，不执行游戏。Timeline 对象/属性树支持建/移轨道、未知初值和偏移首键、旧行失效；GUI 创建者自动内部 ID，Entry 粘贴按语义槽位重映射内部引用。解决方案构建、同源码 656 项回归、格式/指纹通过；一次未修改文件面板指针测试偶发已记录，隔离及完整重跑通过，根因未确认。见 [phase-6e1](../features/F-20261015-02-editor-authoring-workspace/phase-6e1.md)、[当前查询规范](../docs/spec/editor-scene-targets.md)。下一步 P6e2 画布/关键帧，P6e3 曲线；P6e4 事件/剩余语义、通用 Locator、Graph/转场复制和缺目标核验。只提供固定内置能力子集，未分析 Timeline/转场不当无影响；实时预览不列阶段。

- 2026-10-18 P6d 已验证：Timeline 默认底部单实例 Dock 跟随活动 Group Entry，项目 scope 的 EntrySelectionContext/TimelineEditSession 不借用页面字段 lease；已有轨道 NumericUpDown 按当前帧覆盖/插键，保留切线/未知 JSON，单次项目历史。旧行在切换/删除/换类型/关闭后失效，保存和切语言保留未确认输入；切 Entry/帧/根值及关闭 Dock 明确取消。644 项相关回归、解决方案构建、格式与源码指纹通过。见 [phase-6d](../features/F-20261015-02-editor-authoring-workspace/phase-6d.md)。下一步 P6e 目标/属性能力、对象树、创建轨道与画布/曲线；P6f animate 自定义曲线，P7 统一关闭协调。当前仅 Timeline 行隐藏 handle，Group JSON 仍是修复入口；实时预览不列阶段。

- 2026-10-18 P6c 已验证：AnimationPlan 全面统一为 Timeline 类型/播放接口，Entry 为 timeline.play、根参数 timeline，转场与循环恢复同步；builtin.animations section v2，PrimitiveRegistry.State 可显式 version（其他默认 1）。1081 项回归、解决方案构建、格式与源码指纹通过，6 项原生媒体环境未执行。见 [phase-6c](../features/F-20261015-02-editor-authoring-workspace/phase-6c.md)。下一步 P6d 实现跟随活动 Entry 的 Timeline Dock/项目编辑会话，P6e 对象树/关键帧/曲线，P6f animate 自定义曲线；实时预览不列阶段。

- 2026-10-18 P6b 已验证：普通生成 DTO 对象/列表、presence/null/default、JSON 回退、懒创建/释放接入共享层和 Group 页，879 项回归通过，6 项原生媒体环境用例未执行，解决方案构建/格式/源码指纹通过。详见 [phase-6b](../features/F-20261015-02-editor-authoring-workspace/phase-6b.md)。剩余 P6 为未知 Entry JSON 修改、状态查询/Locator 和 AnimationPlan 等复杂定制；后者按用户要求留后续阶段。

- [F-20261015-02 Editor 创作工作区](../features/F-20261015-02-editor-authoring-workspace/phase-plan.md)：P1a/P1b/P2/P2i/P3/P4a/P4b 已验证，P4b 隔离构建与 383 项相关回归通过。项目单栈、Graph、文件事务/双版本文档协调、metadata/引用保护、文件树/网格、路径块/全局文件名搜索、项目文本缓冲与四语已接通；P6a 已提前完成共享 DTO UI 基础，610 项相关回归通过，另 6 项媒体环境用例未执行；三层注册、默认表单、多 DTO 面板与两个宿主已接入。P5a Group 交互已验证（搜索/键盘插入、批量、折叠拖放、尾部取消选择），614 项相关回归通过，6 个媒体环境用例未执行。P5b 多选剪贴板/批量删除与移动已验证，493 项相关回归和解决方案构建通过，含异步剪贴板保护与集合补偿故障测试。P5c 独立变量面板及声明/引用单历史已验证，611 项相关回归和解决方案构建通过；玩家/存档页签、草稿/应用、说明、引用诊断与补全刷新已接通。P5d Entry 字段与新增入口迁移已验证，614 项相关回归及解决方案构建通过；字段历史按稳定 ID/作者 JSON 执行，不保留页面，旧表单在删除/换类型及撤销后不复活。P5e GUI 作者草稿保存已验证，635 项相关回归、解决方案构建与格式/源码指纹通过；Authoring 保留未知/不完整条目 JSON，默认 Complete 与 CLI 校验/编译仍严格。P5 整体 verified，后续继续 P6 状态查询/Locator 和复杂值功能、P7 验收。游戏预览另立 feature。当前实现见 docs/spec/editor-graph.md、editor-groups.md、editor-variables.md、editor-files.md 和 editor-history-and-save.md。

- F-20261015-03 模块化游戏状态：GameSnapshot v4；状态 catalog 随 view 组合，Engine 接入；中立 Layer/EffectInstance/ParticleEmitterInstance 直接作为唯一可变数据记录，属性变更必须在 State.Update，编码形成隔离快照。LoopAnimationState 保存类型化 request/plan；停止控制与播放 identity 仅运行期持有。音频 AudioTrackState/AudioSEffectState 包含暂停、队列与空闲效果，所有轨道按稳定检查点从头恢复；不恢复 seek/淡化时钟，玩家设置不回滚。恢复首帧不重复一次性事件，暂停声音在 Resume 前不启动后端。七个相关项目 665 项测试、解决方案构建与 CLI 实际项目校验/编译通过；新档验证见 [feature summary](../features/F-20261015-03-modular-game-state/summary.md)。


- [F-20261015-01 primitive 注册简化](../features/F-20261015-01-primitive-registration/summary.md)：PrimitiveImplementation 已移除，IPrimitiveModule.Register(PrimitiveRegistry) 在 BuildView 时直接登记 ID → 实例工厂；Register<T> 校验 DTO 并逐次解码独立 Options，Immediate/Async 保留短回调。注册结束或失败后冻结；RegisterDeclared 按 catalog 交集筛选并保留最终契约校验。复杂 Builtins 实例接收 context/Options，场景重放仍使用 request 构造路径。

- [F-20261013-02 参数与 Sample 简化](../features/F-20261013-02-parameter-and-sample-simplification/summary.md) 已完成。Core 的 ValueDescriptor/FieldDescriptor、ValueCodec/ValueValidation 为中立值事实；开放 EditorId/SchemaForm/registry/lease 由 Avalonia 宿主/主题组合，Editor 直接绑定作者文档，设置保留独立异步 apply/save。generator 生成有序字段与成员访问，仅为构建期 Analyzer；源基类字段/default/converter 可合并并检查冲突。全量 43 条 Entry 中 38 条参数 Entry 已 DTO 化，5 条无参数空 schema；ID 位于 BuiltinEntryIds，Composite 用 typed options/子 DTO writer，普通字符串 reader/34 个作者类壳已删除。partial/full 校验保持分开，专属资源/表达式编辑器仍消费唯一 schema；省略/null/default 仍独立。SampleCustomization 集中项目选择，保留 Session/Runner/Presentation 等所有权。作者路径为 Immediate<T>、Async<T>、For<T>、typed Composite；AsyncPrimitiveInstance 默认传播失败，Builtin 展示明确处理错误，PrimitiveTaskScope 完成移除/保留错误。TextAdvancePrimitiveInstance 保留展示结束与玩家推进分离，原 Dialogue Lock 语义保留；Sample 的顺序清理继续尝试所有独立释放并聚合错误，Session 重复关闭复用结果。最终 11 项目 898 用例、解决方案编译、契约独立部署、CLI 编译 4 个 Group 和 16→20 Composite 完整产物对照通过；原两项 UI Flush 挂起已由真实 UI pump 测试解决。4b 计入公共机制后生产净减 51 行，5d 净减 155 行；新增表单/generator 的成本单列，不声称全仓净减少。原生媒体人工全程未做，完整范围与自审见 summary/review。全量设置 DTO、自动框架与 Core 领域/快照重构继续暂缓。

- [F-20261013-01-editor-cli-operations](../features/F-20261013-01-editor-cli-operations/summary.md) 已实现独立内置/项目契约、项目 scope catalog、面向 agent 的 CLI 原始查询/校验/单 Group 编译、共享生命周期与无损保存、Windows 回收站和单平台截图样例；Phase 1–7 已验证并提交；审核与当前边界见 summary/review。CLI 发现使用 help --json，不保留旧别名或默认内置 fallback。契约是可信 C# 代码，动态加载不构成沙箱。

- [F-20261011-03-theme-owned-views](../features/F-20261011-03-theme-owned-views/summary.md) 已移除 GameView 普通页面和默认玩家样式。主题参数必需，最终映射校验完整性；Page 仅保留 GameShell/GamePage/ScreenshotDialog 行为。Themes 提供布局、通用样式与工具栏；截图独立构造/释放，普通页面 Scoped。Editor 显式 Classic 并采用 ThemesAssembly 别名。

- [F-20261011-02-player-page-layout](../features/F-20261011-02-player-page-layout/summary.md) 已补齐三套内置主题的全部普通页面 XAML View，主题目录自行维护布局、共享 ViewModel/命令；页面无外框，主题内导航位置一致。对白名字比正文大4，Classic/Paper 26/22、Minimal 24/20；默认 GameView 页面回退已由 F-20261011-03 删除。

- [F-20261011-01-game-ui-themes](../features/F-20261011-01-game-ui-themes/summary.md) 已完成启动期主题组合：可选 Themes 提供三布局/三配色，页面顺序已由 F-20261011-03 收敛为共享 GamePage→主题→宿主→完整性校验；GamePage 是稳定 TemplatedControl，四个 PART_* 承载其自有内容，派生页与 Runtime 保持同一 Scoped 实例。样式应用于玩家根，官方主题局部 Fluent 与宿主 Semi 隔离。当前无运行期切换；配置/契约见 Themes README 和 docs/spec/control.md。

- [F-20261009-02-video-entries](../features/F-20261009-02-video-entries/feature.md) 已实现剧情视频图层、阻塞分段 seek、停止/替换清理和非阻塞视频定义恢复。平台视频必须经 AssetManager 获取，解码帧通过租约进入场景管线；播放游标不入存档。验证见 feature summary。

- [F-20261010-02-runtime-i18n](../features/F-20261010-02-runtime-i18n/summary.md) Phase 1–5 已实现。Sample 用 Core 2.1.2 + ReflectionBinding；每 JSON 独立 provider，culture-first 查询内嵌/包内/External/额外源；首次选择在 Engine 前原子保存 locale/count。设置切换不重译当前剧情。安装器保留 External 并拒绝覆盖不同包，导出不包含第三方文件。当前契约见 `docs/spec/i18n.md`，人工 GUI 全程与原生媒体 opt-in 验证限制见 summary。

- `F-20260926-01-gallery-meta-pages` 的实现、正式文档与示例 Gallery 解锁 smoke test 已完成；其外部阻塞项 `EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 已由 `F-20260929-04-quality-baseline` 修复，Gallery feature 可按自身流程收尾。
- [F-20260916-02-audio-system](../features/F-20260916-02-audio-system/feature.md) 已完成首期：轨道逻辑、`AudioRuntime`、`IAudioPlaybackHost`、Avalonia LibVLC host、语音文本 composite 和 Sample 验收已落地。Runtime 只能传 asset ID；平台 host 必须经 `IAssetManager` 实体化临时媒体文件。后续 [F-20261009-01-track-seffects](../features/F-20261009-01-track-seffects/feature.md) 增加 host 注册的 `SEffectCatalog`、`seffect.gain` 与基于 LibVLC equalizer 的 `seffect.lowPass` / `seffect.highPass`；后续已实现轨道/SEffect 存档与玩家音量，见 F-20261015-03；item scope、音频参数动画和角色默认轨道仍未实现。
- `F-20260929-01-compiled-content-pipeline` 正在实施；粒子 Primitive、Runtime 状态与 Presenter 桥接及存档后的 effect/particle 展示重放已经验证，完整 Sample/Headless smoke 与 Phase 4 收尾仍待完成。
- [F-20260929-15-replay-save-format](../features/F-20260929-15-replay-save-format/feature.md) 已暂缓：正式存档继续使用完整稳定快照；后续节点解锁/快速进入能力应另立 feature。
- `F-20260929-03-maintainability-roadmap` 及其后续 `F-20260929-14-format-baseline` 已完成。资源协议归 Core.Assets；内容、保存、变量、Gallery 组合和进度端口归 Runtime；Editor 设置/退出协议归 Editor.Abstraction；展示端口使用 `GalNet.Presentation.Abstractions.*`，推荐内置类型使用 `GalNet.Primitives.Builtins`。`GalNet.Storage.Abstractions` 与无消费者历史接口已删除，`ISaveService` 已统一为异步可取消 API。测试已按 10 个生产/运行边界拆分，跨外层场景集中在 `GalNet.IntegrationTests`，Architecture 门禁同时约束生产与测试项目引用。Editor Workspace、Avalonia presenter、Sample 会话和 Editor 扩展边界均已按路线图收敛。生产项目的物理路径按 Shared、Presentation、Infrastructure、Editor、Samples 和 Launcher 分组。CI 使用 solution 级 whitespace 与 `IDE0005` import 检查阻止格式基线回退。
- 后续 feature、验证命令和新经验在实际工作中补充，并保留来源链接或 feature ID。

## 维护规则

1. 只写已经观察到或从正式文档确认的事实。
2. 事实变化时更新本文件的对应条目，并注明来源。
3. 一次性事故先写 lesson；只有可复用、重复出现或高风险的规则才晋升成独立 skill。
4. 如果项目架构变化，检查并标记受影响的 lesson 和 skill，而不是继续沿用旧规则。

## 经验索引

- [测试 fixture 的服务策略](lessons/L-20261018-02-fixture-service-policy.md)：confirmed；无故障注入时保留生产 DI 注册，防止测试的手工默认构造覆盖新策略而给出误导性失败。

- [Entry 旧绑定复活边界](lessons/L-20261018-01-entry-binding-revival.md)：candidate；稳定 ID 可随撤销恢复，控件提交租约必须重新建立，不能只检查对象存在和类型相同。

- [项目监听器身份](lessons/L-20261017-03-project-watcher-owner.md)：后台事件使用创建时固定的项目根，投递 UI 后复查项目身份与 disposed；关闭后不可再从全局 Current 取根。

- [容错读取与作者重写](lessons/L-20261017-02-tolerant-reader-authoring.md)：读取 codec 返回默认 DTO 不代表可以无损写回；作者操作须严格校验并保留原始文件。

- [局部编辑的全局位置](lessons/L-20261017-01-local-edit-global-indices.md)：删除局部集合中的中间项后，After 快照必须同步重算全局索引；比较完整作者快照验证撤销。

- [批量预加载的部分成功租约](lessons/L-20261016-06-partial-preload-leases.md)：每次成功获取立即登记；WhenAll 失败仍须释放其他已成功任务的租约。

- [分支节点高度变化必须发布连线布局刷新](lessons/L-20261016-04-graph-edge-layout-refresh.md)：连接器重建时输入集合事件可能早于输出集合重建，节点布局完成后必须单独通知入边刷新路径。

- [Editor 补全控件的 nullable 失焦边界](lessons/L-20261016-05-editor-autocomplete-null-boundary.md)：可编辑 ComboBox 失焦可将空输入同步为 null；业务提交入口必须先处理 nullable 输入，属性回调中的 `??` 不会规范化属性自身。

- [Editor 语言绑定实际刷新](lessons/L-20261016-03-editor-localization-binding.md)：静态文案显式监听 CurrentCulture，动态 key 同时监听路径；真实窗口切换验收，关闭后释放绑定。

- [Headless 拖动按键状态](lessons/L-20261016-01-headless-drag-modifiers.md)：MouseMove 必须显式携带 LeftMouseButton，不能从 MouseDown 假定状态持续。
- [结构撤销材料捕获时机](lessons/L-20261016-02-structure-baseline-capture.md)：结构准备在取得发布门后读取 Working/Saved，避免正在发布的保存推进基线后恢复旧内容。

- [Editor 历史与发布锁顺序](lessons/L-20261015-04-editor-history-publication-lock-order.md)：先捕获历史，再尝试发布；发布门忙时释放捕获租约，禁止保存与结构编辑互相等待。

- [停止后端前撤销播放身份](lessons/L-20261015-03-audio-stop-identity-order.md)：Stop 可同步重入终态事件；批量停止先清空旧映射，避免旧队列被推进。


- [UI 测试异步观察异常](lessons/L-20261015-02-ui-tests-await-failures.md)：UI 回调中直接 await + try/catch，避免同步 Assert.ThrowsAsync 阻塞依赖 UI pump 的任务。

- [原语结果先于完成通知发布](lessons/L-20261015-01-primitive-completion-result-order.md)：Engine 在完成通知续跑时读取展示任务；先发布错误再通知，任务和通知是不同同步点。

- [连续滑块应走实时预览而非异步提交](lessons/L-20261014-09-live-slider-preview.md)：音量实时更新但不逐刻度提交；空闲后合并持久化，控件保持启用。

- [显式字符串 codec schema](lessons/L-20261014-08-string-codec-schema.md)：STJ 的自定义 string converter 导出不透明 schema；marker 已承诺 wire 类型时显式保留字符串描述。

- [局部 Entry 校验与完整指令校验](lessons/L-20261014-07-partial-entry-validation.md)：完整 validator 依赖 Normalize 的默认/必备字段，逐字段创作使用显式 partial 回调，编译检查仍保留。

- [DTO 保留专属 Entry 编辑器](lessons/L-20261014-05-dto-keeps-dedicated-entry-editors.md)：FieldDataType 不代表所有字段都有共享工厂，补全、资源及表达式控件继续消费唯一 schema。

- [Entry fixture 显式选择 schema](lessons/L-20261014-04-entry-fixture-needs-explicit-schema.md)：legacy/DTO 分支不要借用会继续迁移的 builtin 作为固定测试模型。

- [表单测试必须验证真实视觉树](lessons/L-20261014-03-form-tests-must-mount-editors.md)：工厂输出不代表模板已挂载，动态选项模板必须处理 null 中间态。

- [编辑文本必须携带完整值契约](lessons/L-20261014-02-editor-text-needs-value-contract.md)：string 与字符串编码的 enum 文本表示不同，转换两端都必须使用完整 JSON 配置。

- [受限环境中的 .NET/Avalonia 构建](lessons/L-20260922-01-dotnet-build-sandbox.md)：禁用 Roslyn shared compilation，并关闭 Avalonia build telemetry。
- [读档必须隔离旧运行实例](lessons/L-20261010-02-restore-run-scope.md)：取消与场景对象隔离同时使用；句柄复用的清理需校验实例 identity；快照不能共享 mutable 对象。
- [删除项目后检查依赖锁图](lessons/L-20261010-03-lock-graph-after-project-removal.md)：检查 lock 内容；缓存可能保留旧项目，需要对受影响 roots 强制重算再 locked restore。
- [Engine 停止覆盖所有续跑](lessons/L-20261010-04-engine-stop-continuations.md)：首次推进返回不等于运行结束；停止须取消 scope、等待内部 continuation，统一发布终态。
- [平台无关的进度契约归属](lessons/L-20260922-02-core-progress-contract.md)：已被 F-20260929-07 取代；当前进度端口由实际消费者 Runtime 拥有。
- [受限环境中的平台 SDK 枚举](lessons/L-20260925-01-platform-sdk-sandbox.md)：全解决方案构建可能只因 Android/Browser/iOS 的 SDK 探测读取用户目录失败；应区分平台环境限制和业务项目编译结果。
- [受限环境中的 Avalonia licensing 枚举](lessons/L-20260926-01-avalonia-license-sandbox.md)：Avalonia BuildServices 即使关闭 telemetry 仍可能读取用户级 licensing tickets；应与业务编译失败区分。
- [首次对话的 Avalonia 模板就绪](lessons/L-20260927-01-dialogue-template-startup.md)：阻塞对话不能因模板部件尚未就绪而静默完成，否则引擎会错误地跑完整个流程。
- [样例资源元数据必须随内容版本化](lessons/L-20260927-02-version-sample-meta.md)：被 `.galgroup` 使用的 `.meta` 映射不可被通用忽略规则吞掉。
- [平台媒体路径必须从资源句柄实体化](lessons/L-20260927-03-asset-handle-materialization.md)：页面不能绕过 `AssetManager` 扫描资源目录；文件路径只能由 acquire 的内容临时生成。
- [粒子端到端桥接验证](lessons/L-20260929-01-particle-presenter-unwired.md)：schema 和平台 presenter 存在不代表剧情 primitive 已可执行；必须验证 Runtime 状态和 presenter 桥接。该问题已在 `F-20260929-01` 解决。
- [持久场景状态必须显式重放展示](lessons/L-20260929-03-persistent-scene-presentation-replay.md)：Runtime 快照恢复不会自动重建渲染端对象；每种持久场景对象都必须覆盖状态到 Presenter 的重放路径。
- [动画存档保存稳定语义而非播放游标](lessons/L-20260929-04-animation-save-semantics.md)：一次性非阻塞动画保存末值，循环动画保存第 0 帧状态与完整定义并从头重播，阻塞动画沿用此前稳定快照。
- [Skia atlas 变换使用 source 局部坐标](lessons/L-20260929-05-skia-atlas-local-transform.md)：`DrawAtlas` 的旋转缩放中心应使用 source rect 的宽高，不能重复带入 atlas 偏移。
- [Sample 调试清空的语义](lessons/L-20260929-01-sample-debug-reset-scope.md)：顶层“清空游戏数据”重置玩家状态；日志清空仅属于日志面板。
- [Sample 调试进程内销毁的导航顺序](lessons/L-20260929-02-sample-debug-navigate-before-teardown.md)：已被用户复现推翻；仅调整导航顺序不足以避免黑屏。
- [Sample 调试重置后的重启](lessons/L-20260929-09-sample-debug-restart-after-reset.md)：当前调试清空和资源重载在必要操作后重启进程，避免进程内 Runner 销毁路径。
- [Sample Debug 源码的忽略规则](lessons/L-20260929-10-sample-debug-source-ignore.md)：全局 `[Dd]ebug/` 会吞掉该目录的新源码，必须精确反忽略。
- [实时日志的尾部追随](lessons/L-20260929-11-log-tail-extent-is-not-user-scroll.md)：追加内容只改变 Extent；只以 Offset 变化判断用户是否离开底部。
- [格式门禁必须建立在干净基线上](lessons/L-20260929-06-format-gate-clean-baseline.md)：Windows 仓库在统一行尾前直接启用 `dotnet format` 会把既有行尾、using 和空白债务混入功能改动；先做独立机械格式化，再启用阻断式门禁。
- [测试项目不要用独立 MSBuild 进程并行构建共享依赖](lessons/L-20260929-07-parallel-test-shared-obj-lock.md)：多个 `dotnet test` 进程会争用共享生产项目的 `obj` 输出；全仓测试应顺序执行或由单一 MSBuild graph 调度。
- [项目移动后必须清理旧构建中间产物](lessons/L-20260929-08-project-move-stale-restore-path.md)：移动 .NET 项目后先删除随项目移动的 `bin/obj`，再从新路径 locked restore，避免旧绝对路径缓存重建旧目录和 lock 文件。

- [动态视频图层的加载与帧生命周期](lessons/L-20261010-01-video-layer-lifetime.md)：待加载源先登记，hide/replace 取消加载；帧租约与独立播放 identity 防止晚发布和旧完成误清理。
- [DynamicLocalization Avalonia 主版本兼容性](lessons/L-20261010-05-dynamiclocalization-avalonia-version.md)：2.1.0 与 2.1.2 的 Avalonia extension 都不适用于当前 Avalonia 12 Sample；后者虽能编译仍在启动时调用已移除的 Binding 构造函数。Sample 使用 Core + 自有 `ReflectionBinding` 适配层。
- [JSON provider 的整 culture 替换](lessons/L-20261010-07-json-provider-replaces-culture.md)：2.1.2 的 LoadJsonString 不增量合并，也不做父 culture 回退；分文件需独立 provider 与宿主有序查询。
- [Render 内的粒子耗尽清理](lessons/L-20261010-08-render-pass-particle-drain.md)：confirmed；UI Dispatch 在 UI 线程同步执行，不能作为渲染回调清理的延迟队列；使用保证异步的 `Post`，并以 emitter identity 防止旧回调删除替换实例。

- [Headless session cleanup](lessons/L-20261011-01-headless-session-cleanup.md)：async Dispatch 测试使用 await using，避免同步 Dispose 等待同一个 session worker；动态 Brush 预期值要在资源宿主中解析或比较语义 Color。

- [Editor Avalonia aliases](lessons/L-20261011-02-editor-avalonia-alias.md)：Editor 引用 GalNet.Avalonia.* 时采用程序集别名，避免既有 Avalonia 限定名称被相对命名空间解析遮蔽。

- [运行中的 Editor 输出文件锁](lessons/L-20261013-01-running-editor-output-lock.md)：当前运行的 .NET Host 可能锁住 bin DLL；保留用户程序，使用 feature 专用 --output 目录验证。
- [契约 DLL 流式加载](lessons/L-20261013-02-contract-stream-loading.md)：Unload 不保证立即解除 Windows 映射锁；项目主 DLL 和自有依赖用流式加载，保持共享框架类型身份。

- [构建期间保持源码快照稳定](lessons/L-20261013-03-build-source-snapshot.md)：验证脚本结束前不修改其依赖图源码；交错后完整重跑。

- [Headless 异步 Dispatch 的重载选择](lessons/L-20261013-04-headless-async-void-dispatch.md)：12.0.5 无 Func<Task> 重载，显式 Dispatch<bool> 等待 post-await 断言及实际产物，禁止 async void 隐藏失败。

- [用实际 producer 生成 metadata fixture](lessons/L-20261013-05-metadata-producer-fixtures.md)：AssetMetaCodec 默认输出 id，读取规则大小写无关；查询返回原 ID 拼写并保留实际 pointer，真实 sample smoke 防止测试与实现共享错误假设。
- [Sample 复用页面前先重置 NVL](lessons/L-20261013-06-sample-nvl-reset-order.md)：页面重置依赖仍订阅的 dialogue presenter 清理 NVL；Runner 应先重置页面再释放 presentation，并复位 NVL 可见状态。
- [包装/嵌入 schema 的局部引用](lessons/L-20261014-01-schema-local-references.md)：保留 exporter 对象结构，外层值约束不能污染递归子类型；移动 schema 时按最终 JSON Pointer 重定位局部引用并实际解析验证。

- [Headless XAML 构造前加载主题](lessons/L-20261014-04-load-theme-before-xaml-controls.md)：资源事后可查不代表已初始化控件的空模板查找会刷新；实际 Editor 面板测试已复现并验证。
- [Headless 会话发布竞态](lessons/L-20261014-05-headless-session-publication-race.md)：12.0.5 StartNew 的 task 闭包发布存在竞态；Presentation 通过官方程序集缓存会话避免逐测试释放 worker，Dispatch 保持 PerTest 隔离。

- [Entry 默认值使用完整 codec](lessons/L-20261014-06-entry-defaults-use-value-codec.md)：JSON 字符串编码不代表 CLR 字符串；无法无损投影的默认值保留在字段描述，不填入旧 Defaults。

- [L-20261017-05 scoped binding 幂等退订](lessons/L-20261017-05-scoped-binding-unsubscribe.md)：字段与DTO两层释放只移除一次实际 source 订阅，不释放借用 binding。

- [L-20261017-06 列表可滚动尾部](lessons/L-20261017-06-listbox-scrollable-tail.md)：使用主题 Padding 入口并测量满视口时的实际尾部空间；短列表空白不能证明实现有效。

- [L-20261017-07 可观察集合批量补偿](lessons/L-20261017-07-observable-batch-compensation.md)：通知抛错时当前集合操作可能已生效，应按实际状态恢复；补偿失败进入 RepairRequired。已由故障注入确认。

- [L-20261017-08 作者元数据与严格 Raw shape](lessons/L-20261017-08-author-metadata-strict-shape.md)：新增声明字段须同步写入前的严格白名单/类型校验，保留保存重开证据，不能绕过未知字段规则。

- [文件路径复用与删除身份](lessons/L-20261019-04-file-document-path-reuse.md)：FileDocuments 按会话 ID 持有历史；删除后同路径新建可同时保留旧记录，路径查询须优先有效身份并保留删除遮罩。

- 2026-10-20 游戏本地化 P2 已验证：字段标记/只读索引之上，LocalizationDocumentStore 与数据操作接入单栈和双版本文件事务；相关 JSON 草稿校验/重基与冲突保留、catalog/metadata 专属管理、角色编号持久预留完成。新增 21 项通过，完整相关回归 495 通过、3 个 P6e3 已记录 UI 失败；构建/格式/任务源码指纹通过。P3 角色 UI、P4 表单映射、P5 多语言表格、P6 交换后续。见 features/F-20261019-01-editor-game-localization/phase-2.md 与 docs/spec/editor-game-localization.md。

- [嵌套语义引用使用 wire 名](lessons/L-20261020-02-nested-semantic-wire-name.md)：FieldDescriptor.Id 不等于嵌套作者 JSON 属性名；按值契约 JsonTypeInfo 定位，重建字段须保留 Semantic。P3 索引大小写与真实列表角色改 key 测试已确认。

- [程序化 TextBox 刷新与延迟事件](lessons/L-20261020-03-deferred-text-change-is-not-user-edit.md)：TextChanged 延迟到 bool guard 之后，可能触发失焦写入；使用同步 TextProperty 监听并保留旧异常 JSON。角色阶段完整 Editor 回归已复现并验证修正。

- 2026-10-20 游戏本地化 P3 已 verified：角色 Dock、开发语言姓名/key、speaker 姓名/key 搜索与失焦创建、跨全部 Group/嵌套引用改 key、一次历史与旧值显式导入完成。新增 14 项全通过；相关回归 759 通过、3 项 P6e3 已记录 UI 失败，完整构建 0 错误。正文/分支默认语言映射及复制身份 P4、多语言表格/语言 P5、交换/完整校验 P6 待续；见 features/F-20261019-01-editor-game-localization/phase-3.md。

- 用户协作约定（2026-10-20，2026-10-29 补充）：每个可验证实施阶段完成后提交相关代码、测试和文档，并报告 commit；不要混入其他任务改动。同时精简计划，把已完成步骤归并成阶段总结（结果、验证、commit/证据入口），计划只保留剩余任务、依赖和退出条件。保留详细执行历史及未验收项，不把子范围通过写成整体完成。当前跨平台 feature 的索引为 features/F-20261029-05-cross-platform-applications/progress-summary.md 与 phase-plan.md。

- 用户修订：本地化 feature 不考虑向后兼容；旧 Speakers、角色迁移 UI/服务与回调已删除（d2e4b82），后续只支持当前作者及剪贴板格式。
- [文本历史与不可撤销编号](lessons/L-20261020-05-text-history-reserved-number.md)：同文件的 characterHighWater 仅允许单调重基，文本历史不能回退编号；其他 metadata 严格校验。
- [多行提交的 Tunnel 路由](lessons/L-20261020-06-multiline-commit-tunnel.md)：Ctrl+Enter 在 TextBox 类处理前截获，保留普通换行与 IME 边界，验证真实挂载输入。

- 2026-10-20 游戏本地化 P4 已 verified：默认语言正文/分支语义编辑器、稳定 key、Entry/Graph/列表复制各语言快照、源文复核状态及跨文件单次历史完成。新增 16 项通过，解决方案 0 错误；完整相关回归 812 通过、3 失败（2 项既有布局断言，1 项 Headless DisposeAsync 空引用，独立复验该方法 7 项通过）。四语资源各 850 key 一致、格式/源码指纹/文档链接通过；见 features/F-20261019-01-editor-game-localization/phase-4.md。P5 多语言表格与语言管理、P6 交换/完整校验待续。

- [虚拟化模板的空内容](lessons/L-20261021-01-virtualized-null-content.md)：容器解绑回收时 FuncDataTemplate 会收到 null，工厂须返回空占位，真实滚动与关闭验证。
- [异步文本提交的焦点](lessons/L-20261021-02-async-textbox-focus.md)：Busy 用 IsReadOnly 保护输入，避免 IsEnabled 使 Ctrl+Enter 后丢焦点、后续失焦提交消失。
- [项目级 Dock 草稿生命周期](lessons/L-20261021-03-project-owned-dock-drafts.md)：实际菜单 CloseDocument 会主动 Dispose Context；项目级草稿模型须保留到 scope 结束，并用真实开关路径验证。
- [历史队列执行时重查](lessons/L-20261021-04-history-admission-recheck.md)：入队后仍可因前序操作改变原文或语言；单元格及开发语言预览的 guard 必须进入实际 Redo 边界，两种交错已复现并验证。

- 2026-10-21 游戏本地化 P5 已 verified：项目级 i18n Dock、角色/全部 Group/分支虚拟化多语言表格、语言新增/调序、开发语言缺译预览及切换、单元格状态/草稿保留/Save All 和历史队列执行时重查完成。I18nLocale 统一 CultureInfo 名称，支持脚本子标签标准大小写。新增 17 项通过，正常解决方案构建 0 错误；Core/Runtime/Editor.Shared/Architecture/Presentation/Editor 合计 1039 通过、2 项 P6e3 既有布局失败，另有 6 个视频环境跳过。四语各 882 key、格式/17 源码指纹/文档链接通过。见 features/F-20261019-01-editor-game-localization/phase-5.md 与 docs/manual/game-localization.md；P6 导入导出及发布前完整校验待续。

- 2026-10-21 Editor 写入口与状态已收口：GUI dirty 唯一来源为 ProjectContentSession 的内容/保存基线比较，Document 与 GalProject 不再持有 dirty 标记；变量声明通过 VariableAuthoringService，IVariableDefinitionService 仅返回保留 UID 的独立快照并通知刷新。Preview 只编辑运行时当前值，旧声明表单、Workspace 快照补历史和 PushAlreadyApplied 已删除，不保留兼容层。见 features/F-20261021-01-editor-state-simplification/summary.md、docs/spec/editor-variables.md 与 docs/spec/editor-history-and-save.md。

- [组合查询的完整性原因](lessons/L-20261021-05-query-completeness-causes.md)：内部保留语义未知与对象能力缺失的区别；停止效果可消除后者，不能抹掉前者。candidate，见双路径回归。

- [架构项目发现排除构建输出](lessons/L-20261021-06-architecture-project-discovery.md)：递归 csproj 枚举须排除任意层级 bin/obj；保留临时仓库副本复验 45 项通过，不能靠删除其他任务产物修复测试。
- 2026-10-21 游戏本地化 P6 已 verified，feature P1–P6 完成：单语言模板/平铺 JSON 导入导出、项目交换 GUID、稳定 key 与源文指纹、选择预览、明确空串、整批单次历史及执行时证据重查；完整 project.validate/build 增加包内默认语言和角色语义校验，未知范围 warning/Complete=false。身份预留可能在输出失败后保留，旧文本历史须保留新身份（见文本历史 lesson）。新增 20 项通过，正常解决方案构建 0 错误；完整相关回归 1180 通过、2 项 P6e3 既有布局失败，另有 6 个视频环境跳过。原生文件选择/IME 和大项目性能阈值仍待人工验收。当前契约见 docs/spec/editor-game-localization.md，操作见 docs/manual/game-localization.md，交付与自审见 features/F-20261019-01-editor-game-localization/summary.md、review.md；不保留旧格式兼容。

- [作者参数线格式](lessons/L-20261021-07-author-parameter-wire-format.md)：测试作者 JSON 必须按字段 schema/converter 编码；Animate 的字符串布尔与嵌套 Timeline 的 JSON bool 不可混用，保留坏格式拒绝与有效夹具 Complete 断言。

- [透明 hover 与预乘 alpha（已废止）](lessons/L-20261024-01-transparent-hover-interpolation.md)：全局自定义画刷过渡会影响 Group entry 等非按钮容器；Editor 已恢复 Ursa/Semi 原生 hover 反馈。
- 2026-10-24 Editor UIUX：Core `[Common]` → `EditorHints.IsCommon`，共享表单默认 All，Group 显式 Common；独立 Inspector 绑定与项目 EntryEditSession 草稿，辅助工具不清空详情。保存/检查状态分离，Problems 用当前快照与稳定 ID 导航；三套手动布局及最大化恢复不进入作者历史。契约、使用与证据见 features/F-20261023-01-editor-uiux/summary.md、docs/manual/editor-authoring-workspace.md。真实 DPI/原生 IME 仍需人工验收；旧页面绑定失效规则仍保留，不用复用控件/旧绑定解决草稿保留。
- [Dock 焦点与 Inspector](lessons/L-20261024-02-dock-focus-and-inspector.md)：已活动文档跨区域返回只改变 FocusedDockable；详情跟随须同时处理焦点和激活，并防后台选择抢占及辅助工具覆盖解锁目标。
- [外部调试子进程监控生命周期](lessons/L-20261024-03-external-debug-child-monitor-lifetime.md)：断开调试不能等待未终止的外部进程退出；Editor 存活时仍需排空重定向输出并隔离旧会话事件。candidate，待运行复现。
- [异步构建的同步前缀](lessons/L-20261024-04-sync-build-work-ui-thread.md)：Task API 首个 await 前的验证、扫描与编译仍会阻塞调用线程；先在 UI 线程捕获安全快照，再后台运行纯构建工作。candidate，待大项目运行复现。
- [动画帧颜色与主题](lessons/L-20261024-05-animated-brush-measurement.md)：中间帧比较须纳入 Brush.Opacity，并显式使用实际 ThemeVariant；只看 Color 或默认资源会误报跳色。

- [Benchmark 条件引用与还原图](lessons/L-20261025-02-benchmark-restore-graph.md)：Benchmark 使用独立 bin/benchmark、obj/benchmark 和锁文件；切换条件依赖后不能跳过 restore，以实际运行验证 deps。

- [Avalonia benchmark 退出码](lessons/L-20261025-03-avalonia-benchmark-exitcode.md)：窗口生命周期可覆盖提前设置的 Environment.ExitCode；宿主返回后的 finally 重申失败，脚本同时验证退出码与 Ready。
- CPU 启动事件：Benchmark=true 启用独立 GalNet.Diagnostics 与方法内 #if BENCHMARK 埋点，普通宿主无诊断依赖；scripts/benchmark-startup.ps1 自动采集 Editor/Sample，契约和用法见 docs/spec/cpu-benchmark.md、docs/manual/cpu-benchmark.md。

- [角色姓名文本与身份](lessons/L-20261025-04-character-name-key-identity.md)：DisplayNameKey 允许共享，角色草稿按 ID 与姓名文本组合区分；仅始终唯一的姓名文本可辅助跟随改名，提交仍检查原基线。
- 2026-10-25 Editor 面板：变量以项目级 VariablePanelState 保存作用域选择与 UID 草稿，角色用 CharacterPanelState 隔离共享姓名身份；视图和 transient VM 正常释放，Save All 仍只保存两者已应用声明。本地化筛选保留单元格身份，冻结上下文保持单套虚拟化行；见 features/F-20261023-01-editor-uiux/phase-ui-20261025.md。

- [启动固定快照交错对照](lessons/L-20261025-05-paired-startup-baselines.md)：机器缓存/负载可使同一 exe 跨轮大幅漂移；固定 SHA256、AB/BA 配对和同一 ready 口径，不用单次 smoke 宣称收益。
- Editor 启动在 MainWindow 构造完成时准备欢迎页；Loaded 不再重置导航。20 对交错实验确认小幅收益，主题与 Acrylic 探测均回退；见 features/F-20261025-01-cpu-startup-benchmark/experiments.md。

- [ReadyToRun 与契约身份](lessons/L-20261025-06-r2r-contract-identity.md)：发布转换必须排除严格契约闭包并验证原始字节。Editor/Sample 的 WindowsReadyToRun 配置隔离 bin/obj/lock；仅发布程序获得预编译收益，见 docs/manual/ready-to-run.md。
- 启动调用栈：scripts/profile-startup.ps1 显式打开 Benchmark 的 GalNet-CPU EventPipe 镜像，scripts/analyze-startup-trace.cs 对齐 JIT/采样；采样只做诊断，收益仍用无采样固定快照交错比较。

- [TraceEvent 事件保留](lessons/L-20261025-07-traceevent-retained-events.md)：迭代器复用事件对象；立即提取字段，或 Clone 后再保留。原生启动诊断使用 scripts/profile-startup-native.ps1 和 analyze-startup-native.cs，拒绝已有 WPR 会话和丢事件 trace，见 docs/manual/cpu-benchmark.md。

- Editor 正常 Main 在 Avalonia 初始化前启动两个 Task.Run：设置读取与本地化数据准备；完成后交给 UI/DI，文化/主题/控件仍在 UI 线程，设计器保留同步回退。20 对复核改善约 4.1%；WGL 探测未达收益门槛，默认 ANGLE 保持，见 features/F-20261025-01-cpu-startup-benchmark/parallel-native.md。

- 2026-10-25 本地化资源表实测修订：正文/摘要左上对齐；输入本地保留，失焦/Ctrl+Enter/Save All 提交，不逐字刷新项目。表头与正文明确靠左，隐藏语言使表格窄于视口仍对齐。语言移除保留翻译文件且可撤销，列宽与可见性保留在项目会话；见 features/F-20261023-01-editor-uiux/follow-up-localization-20261025.md。

- [变窄表格的表头左边界](lessons/L-20261025-08-localization-header-alignment.md)：显式宽度的表头与正文都指定 Left；真实布局覆盖内容从宽于视口变成窄于视口。

- [同步捕获待提交输入](lessons/L-20261025-09-textchanging-local-input.md)：Avalonia TextChanged 在渲染后异步触发；TextChanging 同步更新本地输入，作者提交仍只在失焦/明确保存边界进行。

- [静态引用不完整警告](lessons/L-20261025-11-static-reference-warning.md)：References 正常带 references.partial warning；消费方按 error/实际引用拒绝，并保留 ReferencesComplete=false，不能用诊断总数判失败。
- 外部项目创作：CLI project.apply 使用期望哈希和原生文件清单，同事务发布 Graph/Group/资源/本地化；产品 Skill 在 skills/galnet-project-authoring，随 CLI 分发。Editor 通用热重载采纳干净项目，在同一 scope 推进所有基线并清除旧历史；本地草稿冲突保留，Contracts DLL 仍需重开。见 features/F-20261025-02-external-project-authoring/summary.md 与 docs/manual/external-project-authoring.md。

- 2026-10-26 引擎核心重构已完整验收：P0–P6、R1–R8 verified，1789 项普通与 10 项原生测试通过；完整构建/locked restore/格式通过。当前架构事实见前述及 docs/spec，分阶段提交与原始失败证据见 features/F-20261026-01-engine-core-simplification/summary.md。性能专项按用户要求延期。

- [原生音频 UTF-8 ABI](lessons/L-20261027-06-native-audio-utf8-abi.md)：Windows miniaudio 窄字符 fopen 使用 ANSI；统一 UTF-8 ABI 后内部转宽字符，托管/原生同步升级。跨平台构建与 AOT 限制见 docs/manual/cross-platform-backends.md。

- [完整 MSVC 工具集选择](lessons/L-20261027-07-complete-msvc-toolsets.md)：按目标架构检查编译器、头文件和 CRT，再显式选择并存工具集；Windows ARM64 原生库已用 14.44 交叉编译并核对 PE/ABI，x64 保持 14.51。
- [NativeAOT 与编译工具隔离](lessons/L-20261027-08-aot-build-tool-isolation.md)：标准 PublishAot 不能污染 netstandard 源生成器，restore 图和开发锁需隔离；复用 Headless Sample 自检，见 docs/manual/native-aot.md。
- [源生成 extension data](lessons/L-20261027-09-sourcegen-extension-data.md)：GalleryAnnotation 的 init 字典触发构造绑定限制，改用 set 并验证未知字段往返。

- 2026-10-29 默认播放器对白历史/重播/已读颜色：历史正文只在实际保存时进入 GameSnapshot.Attachments，host.dialogue.history 状态只保留计数；同 Group、同 rawId 的相邻 audio.play 与文本按本次执行关联，重播使用独立后端 voice，任何有效继续均停止。阅读前导不能成为半句稳定快照，见 [阅读前导快照边界](lessons/L-20261029-09-reading-preamble-checkpoints.md)、docs/spec/control.md、runtime.md 和 features/F-20261029-03-dialogue-history。
