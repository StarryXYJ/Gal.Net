# 新建项目选择 App：实现总结

状态：本次实现与定向验证完成；不代表当前工作树全部回归通过。

## 行为与实现

- 新建项目在契约目录上方提供可选游戏 App EXE 输入与浏览。仅契约输入为空时自动填入发现目录；已有输入和取消选择均保留。支持普通构建 EXE 同级及当前发行包的 lib，不做历史兼容或递归猜测。
- 缺失/多份契约时提示手动选择；指定 App 后不回退到默认契约。创建前后台读取并校验静态托管依赖闭包，部署保持原字节的精简临时快照，排除框架、原生和无关 App 文件。
- 自包含宿主按 deps 中的 runtimepack runtime 资产识别平台程序集；避免将与 corelib 同目录的 Core/Builtins 错当作平台排除。框架依赖部署使用独立框架目录的 TPA 列表。
- 项目创建并打开后，在工作区导航前复用既有调试配置保存所选 EXE。参数为空，工作目录遵循现有 EXE 所在目录默认值。
- 未指定 App 且未指定契约时保留原默认部署流程。Editor 的 DefaultContracts 目录只包含入口 DLL，Core/Builtins 由 Editor 共享，不作为外部完整闭包处理。

主要文件：`ProjectContractSource.cs`、`NewProjectPanelViewModel.cs`、`NewProjectPanelView.axaml`、四语资源与 `ProjectWorkspaceLifecycleTests.Application.cs`。公共创建接口、CLI 目录复制语义与既有调试协议均未修改。

## 验证证据

- 完整构建 `dotnet build test/GalNet.Editor.Tests/GalNet.Editor.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`：0 error，209 个仓库 analyzer/XAML warning；见 `out/new-project-app-full-build.log`。早期另一个进行中的 feature 的 DialogueHistoryOverlay XAML 编译失败，之后该文件由其原工作流修正，本次没有修改它。
- 最新完整构建后，`NewProjectApplication`、`NewProjectView` 与 `FourPublishedLanguagesHaveIdenticalKeysAndCompatibleFormatArguments`：11/11 通过；见 `out/new-project-app-ui-final.log` 与 `out/new-project-app-results/new-project-app-ui-final.trx`。
- 新增 8 个用例验证：两种布局通过实际挂载的新建面板创建并重开；复制传递依赖，排除框架/无关/原生/符号/EXE 文件，保持契约字节；已有契约和取消选择保留；缺失、损坏、依赖不全、歧义来源阻止创建；手动指定解决自动发现失败。
- 自包含 `out/new-project-app-smoke/published/ContractSmoke.exe` 已实际读取 SchemaA 与真实 `src/Samples/GalNet.Sample.Avalonia/bin/Debug/net10.0`。Sample 精简结果为 `GalNet.Project.Contracts.dll`、`GalNet.Core.dll`、`GalNet.Contracts.Builtins.dll`；SchemaA 还保留其 SchemaDependency 和测试夹具所引用的 TestPlatform 依赖。验证所有输出与来源逐字节相等、临时目录释放。
- 实际 Sample Debug 构建成功（0 error，2 个既有 XAML warning）；见 `out/new-project-app-sample-build.log`。初次使用 `--no-restore` 遇到此前 Player 配置的还原图，缺少 WithDeveloperTools；按 Debug/EnableGameDebugging=true 做 locked restore 后恢复正常，没有修改 Sample 源码或锁文件。随后对新构建 EXE 的契约发现/精简 smoke 再次通过，见 `out/new-project-app-smoke-sample.log`。
- Headless 截图 `out/new-project-app-results/screenshots/p1a-new-project-default.png` 已人工查看：App 位于契约目录上方，输入、提示及创建按钮布局可见。
- 针对本次三个 C# 文件的 `dotnet format whitespace . --folder --include ...` 成功，定向 diff whitespace 检查通过。MSBuild workspace 模式格式化在当前受限环境加载失败，改用只格式化所选文件的 folder 模式。

扩大回归保留的失败（均不调用新增契约来源实现，且失败位置未由本次修改）：

1. `ProjectContractPipelineTests.CliValidateBuildAndExport_UseTheSameDeployedCustomContract`：旧测试按散装 Assets/Paks 路径读取导出归档，当前另一个进行中的打包 feature 已改动发布布局，取 entry 为 null。见 `out/new-project-app-shared-test.log`。
2. `ProjectContractPipelineTests.CustomConfigurationPatch_PreservesUnknownDataThroughExportAndInstall`：旧测试直接读安装根 settings.json，当前安装格式变化导致文件不在原路径。该批测试合计 18 pass / 2 fail。
3. `EditorLocalizationTests.FoundationViewsHaveNoUnlistedLiteralUserInterfaceText`：未修改的 ProjectSettingsPanelView.axaml 中现有 `Content="…"` 不在测试白名单；新建面板没有新增未本地化 literal。最初该批还有本次触发的默认契约闭包错误，已修复并由最终默认创建用例通过确认。

## 正式文档与经验

- [项目格式](../../docs/spec/content/file-formats.md)：新建项目来源发现、复制边界与调试配置。
- [外部游戏调试](../../docs/manual/editor/preview-and-debugging.md)：直接选择 Debug Sample EXE、Developer 包和契约更新要求。
- [自包含平台程序集 lesson](../../.agents/lessons/L-20261029-07-self-contained-platform-assemblies.md)：已记录并索引；未扩大修改既有 SchemaLoadContext 的平台回退实现。

## 限制

只收集静态托管引用，动态/原生加载不属于作者契约。选择 App 不检查或启用其调试能力；Debug Sample 默认启用，玩家发行包需换用 Developer 构建。未用原生文件对话框或真实游戏进程执行完整调试连接验收；本次复用既有选择器与启动配置链路。创建成功后的调试配置写盘若发生 I/O 错误，会显示错误并保留已经创建的项目，不提供跨项目创建与调试配置的回滚事务。项目与 App 后续契约变更仍需同步 DLL 并重新打开项目。
