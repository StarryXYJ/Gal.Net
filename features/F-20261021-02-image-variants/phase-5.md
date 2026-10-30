# P5：示例、发行集成与收尾

日期：2026-10-23
状态：verified（技术集成范围）；整体自查 pass-with-follow-up，未扩大为全部人工矩阵通过。

## 交付范围

- `GameTestCase` 默认 intro 直接运行两个图集，保持原有四个 Group、两条分支、音视频和 Gallery 示例。
- 五张用户更名后的 `xy_*.png` 放入独立 `Assets/Variants/xy/`，第一维 pose、第二维 expression。额外 `solo.png` 逐字节复制 wink，用于验证不同段数与 Reduced；没有新绘制素材。
- 两张原有 classroom JPEG 放入独立 `Assets/Variants/classroom/`，改名为 classroom_idle_empty / classroom_speech_teacher，维度 scene / mode / focus。保留两个背景与 shock 立绘的既有 ID、Gallery item 100；为此前无 sidecar 的图片创建正式 ID。
- 两个描述文件分别为 `xy.variants`（ID 尾部 0020）、`classroom.variants`（尾部 0021）。所有原有七张图片的 Git blob hash 与 HEAD 原素材一致；solo 与 wink 相同。
- 增加中英文对话检查点：Exact → Reduced → 恢复保留的 shy；背景 Default → 继续切到 teacher；hide/re-show 回默认；既有循环动画期间切到 wink。检查点与构建命令见 [Sample README](../../GameTestCase/README.md#image-variants-walkthrough)。
- 本阶段不改业务算法、状态协议或 renderer。更新现有 Sample 测试的 catalog 准备、存档夹具的 variants 筛选，以及默认呈现测试空资源 stub 的枚举接口。

## 自动与原生验证

新增 `ImageVariantsSampleTests`：三个正式导出用例分别使用 Normal / Quick / CatchUp。通过 GamePackageExporter → GameInstallation → PAK provider → AssetManager 加载，安装 Assets 下只有 PAK；本地/发行定义和所有变体图片逐字节一致。使用实际编译 intro 条目，覆盖 Reduced 和 Default 快照恢复后继续选择、第二实例隔离、移动、循环动画、hide/re-show 和最后一个表情选项。

既有 `ModularSaveRestoreTests` 继续从真实 intro 筛选指令，经过 FileSaveService 写新存档文件、加载、模块恢复，核对立绘/背景的实际差分与暂停音轨。内容不一致在换根前拒绝的故障矩阵沿用 P3 测试，本次 Builtins 回归包含这些测试。

新增显式 `ImageVariantsNativeSampleTests`：复制真实素材形成最小原生夹具，使用项目的 Screenshot schema，经正式导出安装后**删除整个临时作者项目**，再启动独立 Avalonia Sample 进程。通过外部调试 seek/step 执行五个检查点，截图前允许 bitmap 加载，截图后等待原子文件发布。检查 PNG 大小和五帧不同，人工逐张查看原生 SceneHost 渲染结果：shock、solo/wink、恢复 shy、蓝色 classroom、重显示 idle 均符合预期。最后 CloseMainWindow，等待 exit 0 并清理临时内容与 Pictures 输出。

截图是原生 SceneHost 的渲染产物，不是操作系统桌面截图；没有据此宣称人工点击所有菜单、全屏/高 DPI、多平台与长时间压力矩阵均已完成。Editor 创建→配置→预览→撤销/保存→重开的真实挂载证据来自 P4b，并纳入本次 Editor 回归。

日志、TRX 和截图留在忽略的 `artifacts/image-variants-p5/`；测试源码和下列数值为可复现的持久记录。

| 验证 | 结果 |
| --- | --- |
| Headless CLI validate / build | exit 0；4 个 compiled Group；保留结构化字段的 localization.field.opaque / incomplete 警告 |
| 完整解决方案隔离构建 | 最终通过，0 error、1 个既有 Avalonia AVLN3001 警告 |
| Core | 148 passed |
| Assets | 58 passed |
| Builtins | 140 passed |
| Runtime | 112 passed |
| Presentation | 243 passed |
| Architecture | 47 passed |
| Editor.Shared | 393 passed |
| Integration（非显式） | 最终 117 passed |
| 原生显式 PAK 渲染 | 1 passed，五张不同 PNG，正常退出 |
| Editor | 完整运行 257 passed / 3 failed；两项既有布局失败，另一次 Headless DisposeAsync NRE 的用例单独复跑 1 passed |
| 本阶段五个测试文件 whitespace | verify-no-changes 通过 |
| 文档链接与 Git whitespace | 178 个本地链接目标存在；本阶段 diff --check 通过 |

首次完整构建被并行调试面板测试缺少 Avalonia 扩展引用阻断；待其自己的修改补齐后完整重建通过，没有修改/提交这些并行文件。第一次 Integration 全量 115 passed / 2 failed：一个空资源 stub 的 GetFilesAsync 抛 NotSupportedException，另一个存档测试只筛选 layer.* 导致遗漏新 variants.show；修正上述测试夹具后全量 117 通过。新用例早期编译引用、编译 ID 的 `#1` 后缀、原生夹具开发语言与异步截图等待等测试接线错误已修正，不将这些失败冒充产品回归。

Editor 两项布局失败为 P4a/P4b 已记录的 `GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor`（scroll tail 0，要求 ≥40）与 `GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave`（宽 482，要求 <480）。另一个用例 `EntryFieldsUseProjectOrderAcrossMountedPagesAndSurvivePageCloseAndReopen` 在 Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync 抛 NRE；相同用例单独复跑通过，根因未确认，不隐去首轮失败、不声称 Editor 全量全绿。五个图集挂载/查询测试在本次完整运行均通过。本轮没有改动 Editor 业务/UI。

八个非 Editor 回归项目共 1258 passed，另有原生显式 1 passed；Editor 结果单列。最后亦重建了默认 `GameTestCase/Output`（4 Group），方便直接运行新示例。

## 规模与内存观测

在 Windows / .NET 10.0.12，Debug 隔离产物，合成 16×16×16 共 4096 变体、每轴 16 个选项，10000 次第二维选择：最终回归构建 3.8767 ms / 分配 2699584 bytes，选择 69.6913 ms / 分配 26080000 bytes。测试输出 JSON，可用同一测试复现；受 JIT 与并行负载影响，不设置虚构的性能门槛。

原生五检查点测试关闭前 WorkingSet 1169068032 bytes、PrivateBytes 1164673024 bytes、PeakWorkingSet 1823805440 bytes。宿主预载 11 张 sprite 并预热 7 个 effect program；此数值包含完整宿主、图像、渲染器、截图和运行库，不能归因为 catalog，也不能当成每张图片内存成本或泄漏证据。关闭日志记录 scope 正常释放、最终 exit 0；没有测量进程内所有缓存的 GC 留存，也没有宣称大规模原始图片的预加载成本可忽略。

纯索引测试不含 4096 张真实图片解码，也不代表数千选项的 GUI 交互延迟。按当前宿主预加载策略，真实素材容量仍需作者控制；大规模图片/菜单的专门性能矩阵作为后续测量范围保留。

## 复现入口

```powershell
dotnet build GalNet.slnx --artifacts-path artifacts/image-variants-p5/build --verbosity quiet -m:1 -p:WarningLevel=0 -p:NuGetAudit=false --ignore-failed-sources
dotnet artifacts/image-variants-p5/build/bin/GalNet.Editor.Headless/debug/GalNet.Editor.Headless.dll validate GameTestCase
dotnet artifacts/image-variants-p5/build/bin/GalNet.Editor.Headless/debug/GalNet.Editor.Headless.dll build GameTestCase --output G:/program/GalDotNet/artifacts/image-variants-p5/content
dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-build --no-restore --artifacts-path artifacts/image-variants-p5/build
dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-build --no-restore --artifacts-path artifacts/image-variants-p5/build --filter FullyQualifiedName~Packaged_variants_render_in_native_sample_and_close_cleanly
```

原生测试为 Explicit：普通测试运行不自动开启窗口；准确筛选方法名才执行，要求桌面及原生依赖。其截图与内存指标通过 TRX 输出/附件归档。构建、testhost 和格式工具使用可访问命名管道的执行环境。

## 设计偏差与文档

采用用户最新素材名替代原计划的 smile/coat 示例；算法中原始五名称仍由 P1/P3 测试覆盖。常量前缀 xy 本身是第一维，需要额外较短的 solo 来演示削减；两张 classroom 的缺组合则演示显式 Default。无语义或算法改变。

补齐 [图集手册](../../docs/manual/editor/image-variants.md)，并清除 [assets](../../docs/spec/content/assets.md) / [entry-types](../../docs/spec/runtime/entries.md) 中“专属 UI 尚未实现”的过时描述。P5 收尾和验收映射见 [summary](summary.md)；实施者自查与限制见 [review](review.md)。异步 Step 的已复现经验记入 `.agents/lessons/L-20261023-02-debug-step-async-presentation.md`。
