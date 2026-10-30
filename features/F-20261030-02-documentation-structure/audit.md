# 文档核查与迁移记录

日期：2026-10-30。核查基于当时当前工作区、适用源码与已有测试/feature 证据；本次没有重新运行全部产品测试或平台实机矩阵。下表的测试列是适用的回归入口，不表示每项在本次重新执行。

## 现有正式文档覆盖

36 篇旧页全部有归属。通过主题拆分和新增 Quick Start/Sample 使用/分类入口，最终导航覆盖 72 页。完整多目标迁移表见 design.md；这里列主归属和核查入口。

| 旧页面（docs 相对路径） | 新主归属 | 实现入口 | 相关测试/验证入口 | 处理 |
| --- | --- | --- | --- | --- |
| `README.md` | `README.md` | `docs/navigation.json` | `scripts/docs/tests` | 重建按读者任务导航，完整覆盖正式页面 |
| `philosophy.md` | `philosophy.md` | `src/Shared` | `test/GalNet.Architecture.Tests` | 保留理念，修正当前 manual 职责和架构链接 |
| `glossary.md` | `glossary.md` | `src/Shared/GalNet.Core/Entry/EntryModule.cs` | `test/GalNet.Primitives.Builtins.Tests/Entry/EntryModuleCatalogTests.cs` | 纠正 Primitive 参数契约与执行工厂分离 |
| `manual/audio-playback.md` | `manual/editor/audio.md` | `src/Shared/GalNet.Primitives.Builtins` | `test/GalNet.Primitives.Builtins.Tests/Audio/AudioRepeatTests.cs` | 合并音频使用，保留次数/轮播/停止边界 |
| `manual/audio-mixing.md` | `manual/editor/audio.md` | `src/Infrastructure/GalNet.Media.Audio` | `test/GalNet.Media.Audio.Tests/AudioEffectRuntimeTests.cs` | 操作、后端配置和混音契约分开 |
| `manual/content-packaging.md` | `manual/editor/content-packaging.md` | `src/Editor/GalNet.Editor.Shared/Services/GamePackageExporter.cs` | `test/GalNet.Assets.Tests/ContentPackageContractTests.cs` | 保留唯一 Graph、完整导出及补丁语义 |
| `manual/cpu-benchmark.md` | `manual/development/cpu-benchmark.md` | `scripts` | `test/GalNet.Architecture.Tests` | 归开发指南，保留固定构建/交错测量及实跑边界 |
| `manual/cross-platform-backends.md` | `manual/extensions/media-and-platform-backends.md` | `src/Infrastructure/GalNet.Media.Miniaudio/GalNet.Media.Miniaudio.csproj` | `test/GalNet.Media.Audio.Tests/AudioOutputLifecycleTests.cs` | 拆出平台支持入口，保留设备/媒体/原生部署前提 |
| `manual/custom-actions.md` | `manual/extensions/actions-and-contracts.md` | `src/Shared/GalNet.Primitives.Builtins` | `test/GalNet.Primitives.Builtins.Tests` | 归扩展契约，核对独立执行注册 |
| `manual/customize-avalonia-sample.md` | `manual/extensions/customize-sample.md` | `src/Samples/GalNet.Sample.Player/SamplePlayerServices.cs` | `test/GalNet.IntegrationTests/SampleExtensibilityTests.cs` | 修正共享 Player 所属，拆分主题/设置/字段/历史 |
| `manual/default-visuals.md` | `manual/editor/default-visuals.md` | `src/Shared/GalNet.Contracts.Builtins` | `test/GalNet.Primitives.Builtins.Tests` | 归 Editor 演出操作，保留显式 shader 和 timeline 边界 |
| `manual/editor-authoring-workspace.md` | `manual/editor/projects-and-workspace.md` | `src/Editor/GalNet.Editor/Services/EntrySelectionContext.cs` | `test/GalNet.Editor.Tests` | 拆出 DTO 扩展，保留工作区与草稿语义 |
| `manual/external-game-debugging.md` | `manual/editor/preview-and-debugging.md` | `src/Shared/GalNet.Debugging` | `test/GalNet.IntegrationTests` | 分离 Editor 操作、宿主接入和内部状态契约 |
| `manual/external-project-authoring.md` | `manual/editor/external-tools.md` | `src/Editor/GalNet.Editor.Headless/README.md` | `test/GalNet.Editor.Shared.Tests` | CLI 路由/帮助与外部创作使用分开 |
| `manual/game-localization.md` | `manual/editor/localization.md` | `src/Editor/GalNet.Editor.Shared/Services/LocalizationExchange.cs` | `test/GalNet.Editor.Shared.Tests/LocalizationExchangeTests.cs` | 保留作者交换和语言操作，修复引用 |
| `manual/image-variants.md` | `manual/editor/image-variants.md` | `src/Shared/GalNet.Contracts.Builtins/Variants` | `test/GalNet.Editor.Tests/ImageVariantsAuthoringTests.cs` | 归资源使用，保持目录/metadata/运行选择区分 |
| `manual/native-aot.md` | `manual/publishing/native-aot.md` | `scripts/release/Test-DesktopAot.ps1` | `test/GalNet.Presentation.Tests` | 归发布，保留 Headless 与图形/实机验证差异 |
| `manual/new-presentation-layer.md` | `manual/extensions/new-presentation-layer.md` | `src/Shared/GalNet.Presentation.Abstractions` | `test/GalNet.IntegrationTests/ProjectConfigurationCompositionTests.cs` | 修正共享配置入口，保留小配置投影及宿主所有权 |
| `manual/ready-to-run.md` | `manual/publishing/ready-to-run.md` | `src/Samples/GalNet.Sample.Avalonia/Properties/PublishProfiles` | `scripts/release` | 归发布，保留严格契约字节和原生验证边界 |
| `manual/release-packaging.md` | `manual/publishing/engine-release.md` | `scripts/release/Release.ps1` | `scripts/ci/Invoke-CI.ps1` | 拆分本地 CI、游戏合包与引擎发版；修正模板过时描述 |
| `spec/architecture.md` | `spec/architecture/modules.md` | `GalNet.slnx` | `test/GalNet.Architecture.Tests` | 拆出模块/生命周期/Editor 数据流，补 Player/CrossPlatform/SDL |
| `spec/assets.md` | `spec/content/assets.md` | `src/Shared/GalNet.Assets/AssetManager.cs` | `test/GalNet.Assets.Tests/BoundedDecompressionTests.cs` | 修正待领取引用、文档包及解压/取消边界 |
| `spec/control.md` | `spec/presentation/game-view.md` | `src/Presentation/GalNet.Avalonia.GameView` | `test/GalNet.Presentation.Tests` | 保留共享 UI，删去已过时的阶段能力描述 |
| `spec/cpu-benchmark.md` | `spec/architecture/diagnostics.md` | `src/Shared/GalNet.Diagnostics` | `scripts` | 归架构诊断契约，测量操作留开发手册 |
| `spec/dock-design.md` | `spec/editor/dock.md` | `src/Editor/GalNet.Editor/Dock/BuiltInDockContributions.cs` | `test/GalNet.Editor.Tests/DockLayoutSerializerTests.cs` | 重写当前面板表、公开泛型契约和生命周期，移出接入教程 |
| `spec/editor-files.md` | `spec/editor/project-and-files.md` | `src/Editor/GalNet.Editor.Shared` | `test/GalNet.Editor.Tests` | 保留文件事务/身份与草稿边界，修复引用 |
| `spec/editor-game-localization.md` | `spec/editor/localization.md` | `src/Editor/GalNet.Editor.Shared/Services/ProjectLocalizationQuery.cs` | `test/GalNet.Editor.Shared.Tests/LocalizationTextStateTests.cs` | 去除阶段名，保留查询/预留/交换事务规则 |
| `spec/editor-graph.md` | `spec/editor/graph.md` | `src/Editor/GalNet.Editor.Shared/Commands/GraphEditRules.cs` | `test/GalNet.Editor.Tests/GraphEditingServiceTests.cs` | 清除继续按旧阶段推进的说明 |
| `spec/editor-groups.md` | `spec/editor/groups.md` | `src/Editor/GalNet.Editor/Services/TimelineEditSession.cs` | `test/GalNet.Editor.Tests/TimelineDockTests.cs` | 保留现有曲线、事件与草稿交互，删除相互冲突的旧未实现描述 |
| `spec/editor-history-and-save.md` | `spec/editor/history-and-save.md` | `src/Editor/GalNet.Editor.Shared` | `test/GalNet.Editor.Tests/UndoRedoHistoryTests.cs` | 删除过时 Group UI 待迁移说法，保留单栈与保存基线 |
| `spec/editor-scene-targets.md` | `spec/editor/scene-targets.md` | `src/Editor/GalNet.Editor.Shared/Services` | `test/GalNet.Editor.Tests/GraphSceneReferenceTests.cs` | 修正 shader 元数据命名空间，保留不完整分析语义 |
| `spec/editor-variables.md` | `spec/editor/variables.md` | `src/Editor/GalNet.Editor` | `test/GalNet.Editor.Tests` | 修正导航能力描述，保留声明/运行值与草稿区分 |
| `spec/entry-types.md` | `spec/runtime/entries.md` | `src/Shared/GalNet.Core/Entry/EntryModule.cs` | `test/GalNet.Primitives.Builtins.Tests/Entry/EntryModuleCatalogTests.cs` | 归运行时契约，核对独立 schema/执行注册 |
| `spec/file-formats.md` | `spec/content/file-formats.md` | `src/Infrastructure/GalNet.Storage.FileSystem/InstalledGameDescriptor.cs` | `test/GalNet.Assets.Tests/ContentPackageContractTests.cs` | 修正实际 galnet.project section 示例版本，保留完整 v4 安装和旧目录边界 |
| `spec/i18n.md` | `spec/content/localization.md` | `src/Shared/GalNet.Runtime/I18n` | `test/GalNet.IntegrationTests/GameLocalizationLoadingTests.cs` | 归内容规范，区分 UI、内置文本和外置覆盖 |
| `spec/runtime.md` | `spec/runtime/execution.md` | `src/Shared/GalNet.Runtime/Engine` | `test/GalNet.GameHost.Tests/GameSessionTests.cs` | 拆执行与快照，修正配置装载与音频状态版本说明 |

## 实际执行的验证

- `dotnet --version`：10.0.401。
- 当前已有 Debug CLI 的 `help --json` 成功，返回 68 个实际操作描述。
- 当前已有 Debug CLI 的 `project create`（使用 GameTestCase/Contracts）与 `validate --structure-only` 成功，均无 diagnostics；输出位于忽略的 Temp/documentation-structure。未据此宣称 GUI 首次使用或完整媒体闭环已重新验收。
- `python scripts/docs/sync_wiki.py check`：72 页、导航、正文内本地链接与标题 fragment 通过。
- 仓库其他 Markdown 指向 docs 的引用检查通过；40 条原本已删除文档的历史链接改为可由本地 Git 对象验证的历史 commit URL，另修复 1 个 Gallery 锚点。不改写历史方案为当前规范。
- `python -m unittest discover -s scripts/docs/tests -v`：11 项通过，覆盖转换、引用式链接、附件、生成后 fragment、导航覆盖、人工修改/删除保护、幂等与清理边界。
- Wiki 已本地生成全部 72 页及侧栏/页脚/管理清单；最终来源提交与幂等复验记在 summary。

## 偏差与保留边界

- 增加 spec/editor/debugging.md：将原调试文档的状态/身份/缓存契约独立归属，避免与 Editor 操作混合。
- 将已移除的历史 docs/design、docs/adr 链接指回各自最后有内容的提交，不重新创建一套过时正式规范。
- 本次核查覆盖结构、相关 API/实现与已有回归入口，不等于逐个示例重新构建、GUI 操作或平台实机全量验收。macOS/iOS/Browser/真实手柄及完整媒体的未验收边界继续保留。
- 工作区的 Sample/跨平台源码由其他任务继续修改，本次不提交这些源码。对应源码在主仓库发布前需由原任务完成提交与验收。
- 用户明确选择主仓库与 Wiki 一起待发布；本次不 push。Actions 流程只完成本地定义与验证，DOCS_WIKI_TOKEN 未配置/未检查，不能声称远程自动发布已经启用。
