# Phase 4：低样板 Entry 作者路径

状态：verified，2026-10-14。当前无阻断项；下一阶段为 Phase 4b。整个 feature 仍在进行中。

## 选择与实现

以 Phase 2d/2e 的 generator、ForDto<T> 和 For<T> 为基础，不重新建立 typed reader 或参数容器。剩余可复用样板是同步工厂反复创建 ImmediatePrimitiveInstance 并传递 BatchId，因此在现有 PrimitiveImplementation 增加 Immediate 和 Immediate<T> 两个入口，共 20 行。

同步入口创建非阻塞、不可跳过的实例，同步完成并自动传递批次；Normal/Quick 执行 dispatch，CatchUp 默认重复 dispatch，可以显式覆盖。回调错误仍传播。它不推断 UI dispatch、取消、异步任务或持久状态，复杂实例仍通过 For<T> 返回。无参数路径不要求空 DTO，也不自动注册/扫描模块。

实际迁移：

- NVL show/hide/clear 和对白 show/hide 使用普通 Immediate，业务与 CatchUp 行为不变。
- audio.pause/resume/next/clear 共用 AudioTrackOptions，以一个 TrackId 属性声明默认 bgm、可省略及允许 null；执行使用 Immediate<T>。模块的 scope 绑定/取消检查仍在业务适配中，显式 null 仍按原逻辑映射为空轨道名。
- flow.wait 使用 WaitOptions 的 float Duration、默认 1 与 minimum 0，For<T> 返回原 WaitPrimitiveInstance；等待、skip、Quick/CatchUp 与取消语义不进入同步 helper。
- 截图继续使用 ScreenshotOptions + 自定义安全校验 + For<T> + 异步实例，Composite 继续显式 Compile 展开。两者的端到端回归通过，不为统一形式改写复杂语义。

移除 WaitEntry 和四种轨道 Entry 的旧 ParameterTypes / DefaultValues；当前参数事实从 DTO.Schema 或 catalog.DynamicParameters 取得，不保留重复兼容投影。Contracts.Builtins 增加现有 generator 的 Analyzer 引用，运行时仍仅依赖 Core 与框架；没有新增生产项目、接口或运行时 Roslyn 依赖。

## 作者成本与方案比较

| 代表路径 | 原路径 | 当前路径及收益 |
|---|---|---|
| 无参数同步 NVL/对白 | 工厂 lambda 内构造实例并重复传 BatchId | 一个 Immediate 调用；无需空 DTO 或实例类 |
| 默认轨道同步操作 | 一套 ParameterTypes/Defaults，四处字符串 trackId 读取，手写实例/批次 | 一个共享 DTO 成员与常量默认，四个类型安全业务回调；删除 4 处字符串读取与旧表 |
| 带范围和默认值的 wait | duration 名称在契约/读取重复，默认 1 在契约和 fallback 重复 | 属性/特性提供字段与唯一默认；仍显式选择独立异步实例 |
| 截图业务校验 | Phase 2e 已消除基本参数读取，仍有安全规则/异步所有权 | 保留；减少参数样板不能替代业务验证/生命周期 |
| Composite | 手写展开与 primitive 契约检查 | 保留；没有第二个足以证明收益的通用展开适配，继续使用既有 Compile |

手写 DynamicParameterTable + 字符串 reader 仍可用于动态契约，但在已采用 DTO 的普通路径上继续维护读取表没有收益。运行期反射/JSON metadata 适配将重复已有生成成员访问；新建通用 definition DSL 或生成 module 会增加排错和注册约定，本阶段均不采用。轻量同步 helper 与已落地 DTO 组合已经降低明确维护点；Phase 5 再按具体作者问题评估诊断/模板，不从零评估 generator。

生产文件净增 41 行（其中 C# 净增 40，csproj 1）：已有生产文件增加 49、删除 25，新增 DTO 文件 17 行。两份生成代码各 22 行，共 44 行，保存于 Temp/F-20261013-02/phase4-generated。测试净增 207 行：Presentation 95、新 Builtins 测试 111、Architecture 1。生成器本身未增加代码；构建期依赖增加一个显式 Analyzer ProjectReference，locked restore 通过，没有锁文件变化。收益是维护事实/工厂步骤减少，未声称总行数下降。

## 验证

| 范围 | 不重复用例数 |
|---|---:|
| Builtins 完整回归，含生成契约、实际音频后端请求、wait、Composite | 81 |
| Presentation 完整回归及追加文档/CatchUp 用例 | 172 |
| Architecture，含 Builtins generator 仅构建期引用 | 37 |
| 真实截图及 Sample 集中组合 | 17 |
| 合计 | 307 |

TRX 为 Temp/F-20261013-02/phase4-{builtins,presentation,sync-examples,architecture,screenshot-composition,typed-diagnostics}.trx。Presentation 首轮全量 170 通过，新增两用例后定向 18 通过（16 重复，不重复计数）；Builtin 首轮 81 通过，精确非法字段诊断加强后定向 8 通过（不重复计数）。同步测试覆盖三个模式、显式 CatchUp、默认/converter、自定义校验拒绝、DTO 隔离、未知字段、错误 DTO 契约、批次和错误传播；文档无参数示例实际编译并写入 Runtime 变量。音频验证编译参数进入实际 host 的 Pause/Resume/Stop 请求，取消仍阻止 dispatch。截图验证真实 PNG 和异步失败/取消/释放，不使用简单工厂取代异步实例。

迁移前保留 Phase 3 CLI 输出，迁移后构建 Phase 4 CLI 并独立部署契约；对 audio.pause/resume/next/clear 与 flow.wait 的 data.result.items 做完整 JSON 对照，五条描述全部相同，含默认、null、constraints、editor 和 valueSchema。证据为 phase4-before-*.json、phase4-after-*.json、phase4-schema-comparison.json。截图 schema 查询成功；实际 GameTestCase 编译 4 个 Group，diagnostics=[]，见 phase4-build.json。Sample.Avalonia 随集成测试构建，Headless 静态项目引用构建通过；未做完整剧情或原生媒体人工播放，也未运行全解决方案测试。

初次测试夹具误写不存在的 CompiledGroupEntry，检查实际序列化模型后修正为 PrimitiveEntryDocument。受限环境 restore 退出失败且无日志，同命令沙箱外 locked restore 成功；部署过程中 NuGet 漏洞数据源不可达产生 NU1900 警告，构建/部署仍通过，未修改产品代码或隐去警告。改动文件 whitespace / IDE0005 检查通过。

docs/development.md 现在给出无参数同步、DTO 同步和复杂/异步扩展的选择与可运行示例；正式契约同步至 docs/spec/entry-types.md。后续 Phase 4b 单独处理异步任务跟踪和清理失败，不夹带作者 schema 或运行时领域重构。
