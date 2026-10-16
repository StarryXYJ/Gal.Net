# Phase 5d：Composite DTO 与作者类壳清理

状态：verified，2026-10-14。全量 Entry 外层参数 DTO 迁移已完成；下一阶段为 Phase 4b 的异步实例、任务 scope 与清理。整个 feature 尚未收尾。

## 实现与收益

8 条 Composite 的 81 个参数槽位迁移到 7 个 DTO；black/white fade 共用 ColorFieldOptions，ColorFadeOptions 继承同一源基类的 12 个字段并增加 color。生成器合并源基类 Field，保留顺序、默认值和 converter，拒绝隐藏/重写成员及 Order/ID 冲突；不再维护第二套公共颜色字段描述。

Composite 通过 ForDto<T> 接收独立强类型参数，静态业务回调继续显式构建计划及条件/batch；字符串取值、数字与枚举解析退出 Composite 路径。正数、颜色和 slide 正 scale 等业务规则保留。普通 Normalize 现在也执行 Composite 声明的类型/选项/未知字段检查；合法内容格式不变，原 Values 路径可默默忽略的非法字段不再被编译接受。

ValueCodec.SerializeFields 按字段 codec 序列化子 DTO：默认保留 null，省略 undefined JSON，可显式 omitNullValues。语音的 AudioPlayOptions + DialogueTextOptions/NvlAppendOptions，以及计划 Primitive 的 AnimationPlanOptions 都使用此入口；batch 通过 PrimitiveEntry.BatchId 传递。开放动画事件参数保持现有 JSON 结构与显式业务构造，不增加递归计划 DSL。

42 个稳定 ID 移到 BuiltinEntryIds；删除 34 个 Primitive 作者类壳所在的 9 个文件，以及无消费者的 BuiltinInstructionFactory 文件。同步 visibility helper 直接接收 ID，删除仅为取 ID 实例化作者类的泛型约束；旧参数表和通用 Schema<TEntry> helper 完全退出 Builtins。8 个 Composite 作者类及颜色展开父类也由类型化回调/共享业务函数替代。Core 的通用作者表示与手写/动态契约扩展仍可使用。

本批生产 C# 净减少 155 行：已有文件 +256/-568，新 ID/DTO 文件 157 行；包含 writer 和生成器增量成本，排除既有对白 Lock 与并行表单/设置改动。源码 ID 引用替换涉及多个消费者，但未改变 wire ID、程序集依赖或自动注册策略。正式 README/spec/development 已同步，历史 feature 记录保持原状。

## 验证

- Generator 14、Core 97、Builtins 102、Integration 100、Editor 43、Architecture 37，共 393 个不重复用例通过。
- 新增验证继承字段/default/converter/order、隐藏与重复 Order 诊断；DTO writer 的编码、null/undefined；voice 顺序/condition/reading boundary/source map；颜色共享字段与 transition 无效输入、scale 和嵌入 JSON 错误。
- 删除 WaitEntry 后，Integration 的未编译作者条目拒绝 fixture 首次无法编译；替换为实际 Core AuthoringPrimitiveEntry，原拒绝断言继续成立。没有恢复旧类壳或放宽 Engine。
- 8 条 Composite 的 CLI schema result.items 与迁移前完全相同。
- 16 个 source（每类默认/自定义各一个）编译成 20 条 Primitive；JSON 对象按键比较、数组顺序保留，完整 arguments/condition/batch/reading boundary、计划事件/轨道及 source map 与迁移前一致。产物位于 Temp/F-20261013-02/phase5d-{before,after}.galgroup，源样例保存在 phase5d-source.rawgalgroup。
- Contracts 独立部署、CLI 构建、整个 GalNet.slnx 编译通过；包括 Sample Avalonia/Headless、Editor/CLI、Launcher 和全部测试项目。实际 GameTestCase 编译 4 个 group，diagnostics 为空。
- 改动文件执行 IDE0005/whitespace 与 git diff --check；不格式化/提交已有 DialoguePrimitiveInstance 的 Lock 改动或并行表单/设置修改。临时 source fixture 已从游戏项目清除。

TRX 为 Temp/F-20261013-02/phase5d-{generator,core,builtins,integration,editor,architecture}.trx。构建顺序运行、关闭 shared compilation/telemetry。全解决方案使用独立共用输出目录绕开运行中 Editor DLL 锁，SDK 给出 NETSDK1194；该输出仅用于编译验证，独立契约/CLI 另有分项目产物。既有 analyzer/Avalonia warning 与 NU1900 审计端点提示保留，无编译错误。

## 当前边界

43 条 entry 中 38 条有参数的 entry 均采用 DTO（37 条内置 + sample screenshot），5 条无参数保持空 schema。当前 parameter/schema 维护只来自 DTO 与明确开放 payload；快照、业务异步实例、重放和平台资源所有权保持原边界。Phase 4b 与最终净收益复核/收尾尚未完成。
