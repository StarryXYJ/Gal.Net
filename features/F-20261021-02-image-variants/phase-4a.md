# P4a：场景分析与基础字段接入

日期：2026-10-21
状态：verified（本切片；P4 整体未完成）

## 范围与结果

P4 拆为 P4a 与 P4b，分别提交并验证。P4a 建立后续联动字段所需的场景事实，并接通基础资源和对象选择；P4b 继续完成目录创建、配置、差分预览、初始选择与维度/值联动、影响诊断及其保存流程。没有修改匹配算法、运行时指令或发布格式。

- `SceneTarget.Resource` 仍表示实际 sprite；新增不可变 `SceneImageVariants` 保存图集候选来源、来源确定性和可知的完整选择结果。查询直接复用运行时纯 resolver，保留请求、保护维度和回退链。
- show 创建 Layer 及原有连续属性；select 改变可知图片事实，保留 Locator、存在性及属性值。普通 show/color/video/replace 清除图集绑定，hide 移除，re-show 重置请求。坏选择或未知场景语义不保留权威图集请求。
- 分支请求不同但图集相同时保留共同来源、放弃唯一请求；来源不同保留可能 ID 集合。条件性普通换图使图集模式仅为可能。后续 select 无法安全重建请求时不推测实际图片，标记不完整。
- `ProjectSceneTargets` 在后台读取编译 payload，解码不可变定义，完成后换代；每代最多 256 个图集、每个 payload 最多 1 MiB，超出部分保持不完整。坏定义、缺资源、错误类型缓存到下次刷新。取消一个查询者的等待不取消项目共享准备；刷新及释放使迟到读取与旧选择失效。
- show 的资源字段筛选 `image-variants`，变换使用既有控件；select 的对象字段使用场景 Picker，排除已知普通图层，保留可能的图集对象并对不唯一来源显示提示。图集 show 的内部 handle 自动分配且隐藏，复制通过声明槽位统一重映射 select 与 Timeline 引用。补齐四语指令、维度、初始选择及来源提示。
- 基础字段确认复用项目历史；验证撤销/重做、保存重开与未知 JSON 大整数保留。查询和准备不写作者数据。Timeline 能定位图集 Layer 并采样已有属性，没有新增差分切换事件。

## 验证证据

日志与 TRX 位于忽略目录 `artifacts/image-variants-p4a/`。

| 检查 | 结果 |
| --- | --- |
| 新增 Shared 场景用例 | 16 项通过，包含在 Shared 回归中 |
| 新增 Editor 用例 | 2 项通过：异步/取消/坏资源/刷新/释放代次；真实挂载资源与对象选择、单历史、保存重开 |
| Editor.Shared 全量 | 383 通过、1 失败，共 384；失败为并行调试任务的 `DebugRoutePlannerTests.UnorderedSelectedChainIsPreferredWithoutChoosingTheShorterUnselectedRoute` |
| Editor.Shared 排除该并行测试类 | 381 项通过 |
| Editor 全量 | 254 通过、2 失败，共 256；失败与既有记录一致，见下文 |
| Architecture | 47 项通过 |
| 完整隔离解决方案构建 | 失败于并行修改的 `SampleCleanupTests.cs:75`：`SampleGameSessionService` 新构造函数缺少 `services` 参数；4 个既有 Avalonia 警告 |
| Editor 及其测试定向构建 | 成功，使用隔离产物，未关闭正在运行的 Editor |
| 本阶段源码/测试 whitespace | 修正测试字典初始化换行后验证通过 |

Editor 两项失败：`GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor` 的滚动尾部期望至少 40、实际 0；`GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave` 的行宽期望小于 480、实际 482。相关布局不在本次改动中，未修改断言或实现来消除失败。

主要命令（参数省略日志重定向）：

```powershell
dotnet build GalNet.slnx --artifacts-path artifacts/image-variants-p4a/build --verbosity quiet -m:1 -p:WarningLevel=0 -p:NuGetAudit=false --ignore-failed-sources
dotnet test test/GalNet.Editor.Tests/GalNet.Editor.Tests.csproj --artifacts-path artifacts/image-variants-p4a/build --no-restore --verbosity quiet -p:WarningLevel=0
dotnet test test/GalNet.Editor.Shared.Tests/GalNet.Editor.Shared.Tests.csproj --artifacts-path artifacts/image-variants-p4a/build --no-build --no-restore --filter 'FullyQualifiedName!~DebugRoutePlannerTests' --verbosity quiet
dotnet test test/GalNet.Architecture.Tests/GalNet.Architecture.Tests.csproj --artifacts-path artifacts/image-variants-p4a/build --no-build --no-restore --verbosity quiet
```

新增挂载用例最初的失败是测试夹具断言问题：图集使用 DTO 表单而非旧 ParameterFields；对象搜索需明确选择候选而非 None；保存 JSON 的空白变化应按语义比较。修正后新增两项独立重跑通过，完整 Editor 运行也通过这两项。

## 剩余范围与限制

P4b 尚未开始；当前不能通过专属图集面板创建描述、配置维度/default 或预览差分。初始 selection 和 select 的维度/值仍使用通用字段。后续需要覆盖图集定义草稿、Save All、配置变更的影响诊断、动态选项与打开期间失效，及整条创建→预览→剧情选择流程。实际运行的原生图像人工验收、示例和导出集成留 P5，不能用本切片测试宣称整项图集功能完成。

工作区存在并行调试任务的代码、lock 文件和四语调试文案。提交只包含本切片内容，保留其他任务的文件与同文件改动。
