# P1：字段语义与可诊断项目索引

状态：verified，2026-10-19。feature 整体仍 in-progress，P2–P6 未实施。

## 已实现

- Core 提供 LocalizedText/CharacterReference 特性及 FieldDescriptor.Semantic，类型/互斥规则由 generator 和手工 descriptor 构造器分别检查；标签/控件 hints 不再被当作值语义。
- generator 支持继承成员的标记，错误类型、没有 Field、重复语义报告带源码位置的 GNF001。简单和带语音的对白/NVL DTO 均标记，动态参数表保留原 descriptor；CLI schema 描述增加 Semantic。
- Shared 提供纯 LocalizationReferenceIndex：全部作者 Group、角色姓名和 Choice 分支引用；嵌套 DTO/列表 walker；保留稳定身份和 JSON Pointer，不生成 key、不改值、不执行剧情。
- 所有翻译定义按 Ordinal 文件顺序保留来源，FindDirect 用索引返回最后生效的直接译文，区分缺失与空串，不使用回退文本冒充已译；External 不进入作者数据。
- JSON 重复属性、扁平/嵌套 key 碰撞、坏文件/catalog、未知字段/schema、未知角色、缺默认语言译文均有诊断。分析深度 16、值预算 100,000，Complete 明确表示引用分析覆盖度，不表示项目可运行。
- ProjectLocalizationQuery 注册为项目 scope，读取 Working 及未受管理的磁盘来源，受管理内存修改/删除优先；缓存分析代次，卸载和释放后失效。同路径删除后重建按有效文档身份选择，不因历史保留记录崩溃。

## 验证

最终解决方案构建：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false --verbosity quiet
```

结果：成功，0 errors，77 warnings。未对整个工作树建立警告增量基线，不宣称无新增告警。日志 `Temp/localization-p1-solution-final.log`。

对最终构建使用 `dotnet test <project> --no-build --no-restore`：

| 测试项目 | 通过 | 失败/跳过 |
| --- | ---: | ---: |
| GalNet.SchemaGenerator.Tests | 18 | 0/0 |
| GalNet.Editor.Shared.Tests | 237 | 0/0 |
| GalNet.Core.Tests | 111 | 0/0 |
| GalNet.Architecture.Tests | 45 | 0/0 |
| 合计 | 411 | 0/0 |

本次增加 generator 4 项、索引/会话 14 项；覆盖原文相同但 key 独立、未打开 Group、分支身份、来源顺序/空串/External 排除、坏文件不遮蔽正常文件、重复/collision、未知 schema/缺失 Group、null/缺省/未知参数、嵌套指针转义、递归预算、未保存文件/删除重建、卸载/Dispose 缓存失效。TRX 位于 `Temp/localization-p1-results/`，以四个项目名称的结果文件为最终回归证据，早期 focused/旧快照结果不混计。

对本任务 11 个 C# 文件的 `dotnet format whitespace ... --no-restore --verify-no-changes --include ...` 通过，`git diff --check` 通过。没有批量格式化其他工作树文件。

## 调查与修正

1. 受限环境内测试宿主连接超时；退出受限环境运行相同已构建测试后通过。受限环境解决方案构建也遇到浏览器 MarshalingPInvokeScanner 任务宿主连接错误，外部复核构建通过；没有修改产品代码绕过宿主限制。
2. 测试初版使用 DI BuildServiceProvider，但 Shared 测试只依赖 DI 抽象；改为夹具 EmptyScope，没有引入生产依赖。
3. 复核 TrackFile 发现删除记录和新有效文件可以拥有同一路径；查询按有效身份分组处理，并补充回归。见 agent lesson L-20261019-04-file-document-path-reuse。

## 当前限制与下一阶段

- 本阶段只有元数据、只读索引和 scope 查询，没有角色 Dock、自动创建角色、自动建文本 key、默认语言表单映射、翻译表格或导入导出。
- 普通 JSON 页与结构化写入的草稿冲突还未接通，查询不提交草稿；P2 实现文档写入、文件创建、历史和保存协调。
- 按需查询仍捕获整份作者文档和读取来源后判断变化，未做文件级增量性能验证，不应逐字符调用。
- 不完整分析只做诊断；尚无角色删除功能，不宣称已实施删除保护 UI。
- 未进行桌面 IME 人工验收；本阶段不新增输入控件。
- 原有未提交/并行工作区修改保留，本记录只描述本 feature 改动；没有执行 Git 提交。

当前事实同步到 [规范](../../docs/spec/editor-game-localization.md)。
