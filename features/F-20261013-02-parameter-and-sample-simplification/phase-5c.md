# Phase 5c：复杂 Primitive 外层参数 DTO

状态：verified，2026-10-14。无阻断项；下一批为 Phase 5d 的 Composite 与旧壳清理。异步实例简化尚未实施。

## 实现

迁移 layer.show/showColor/move、animation.animate/play、audio.play/enqueue/seffect.apply、video.play、effect.apply、particle.play/burst，共 12 条指令、66 个参数槽位。新增 11 个 DTO（audio.play/enqueue 共用 AudioPlayOptions），执行模块与普通 request 构造使用强类型成员；删除相应旧参数表。按参数名读取的 Arguments 方法已全部移除，仅保留原 JSON 配置与嵌套 JSON 解析工具。

标量使用原数值类型；bool/enum 使用显式字符串 codec，保留 wire 大小写、选项顺序、默认值、null fallback。动画属性选项通过 AnimationPropertyValueConverter 从 Layer/ParticleEmitter 属性表派生，复用现有 IStringValueEncoding 扩展点，不添加生成器 hook。动画 from 保持无默认的 float schema，通过 context.Arguments 的 presence 区分省略/显式零；batchId 继续由编译信封提取，不误当业务 DTO 状态。

transform/flipbook/plan/skipPoints/parameters 等嵌套 JSON 仍为 JsonElement，解析时机及对象/嵌入 JSON 字符串规则沿用旧逻辑，不扩大为递归 DTO 改造。开放 effect/seffect payload 保留项目自定义字段。快照、plan event/replay 的旧格式、视频/动画实例与平台资源生命周期保持原实现；本批没有强行合并这些不同入口。

## 两项兼容性修正

1. 粒子整组 validator 在未补默认值的作者字典上形成创作死锁：先填 ID 缺 parameters，先填 payload 缺 ID。定向两用例在修复前均失败。DynamicParameterTable/ForDto 增加显式 validatePartialArguments；默认沿用原 validator，内置视频/粒子/计划显式将完整语义留给编译/执行，局部编辑仍验证字段类型/范围。新增 Editor 两种填写顺序测试，未完成内容完整 Normalize 仍拒绝，填完后通过；原项目自定义逐字段 validator 继续有效。
2. CLI 对照发现自定义 JsonConverter<string> 导出 opaque true schema。显式 IStringValueEncoding 已承诺字符串 wire 格式，ValueCodec.Describe 对这种 string codec 导出标准字符串类型，保留约束和原读取 codec；其他自定义 converter 的描述不改变。新增 Core 回归后 12 条 CLI 描述完全一致。

新增 Editor 测试首轮修复后还因 fixture 缺 StableId 失败，补齐已有文档要求后通过；产品没有绕过 stable ID 校验。经验见 L-20261014-07/08；设计补充已同步 design.md。

## 验证

- Core 完整 96、Builtins 完整 99、Editor 完整 43、Integration 完整 100，共 338 个不重复用例通过。
- 新增回归覆盖动画 from 省略/显式零、实际 presenter request 与编译 batch；对象及嵌入 JSON 字符串 transform；开放 payload；大小写选项；动态属性来源；数值范围、视频 skipPoints，以及粒子两种创作顺序和完整诊断。
- TRX 位于 Temp/F-20261013-02/phase5c-{core,builtins,editor,integration}.trx；失败复现保留 phase5c-editor-partial-before.trx。
- Contracts 独立部署、CLI 构建、Sample Avalonia 依赖构建通过；12 条指令 CLI result.items 与 before 基线完全相同。
- GameTestCase 实际编译 4 个 group，diagnostics 为空，记录 phase5c-build.json。
- 改动文件执行 IDE0005/whitespace 格式整理与 git diff --check。构建有既有 analyzer/Avalonia warning 与 NU1900 审计端点提示，无编译错误。

验证顺序执行并保持源码稳定。过程中另一工作提交了旧 AudioChannelManager 删除，未包含在本批范围；受影响的 Builtins/Integration 已用当前源码重新验证。

## 收益与当前边界

所有参数 Primitive 的外层参数与执行读取现在均来自 DTO，原字符串 reader 及重复参数表退出普通执行路径。本批生产代码净增 49 行（已有文件 +93/-187，新增 DTO 文件 143 行），收益在唯一参数事实、类型检查及旧 reader 删除，未声称总代码量下降。按多行 DTO 声明统计总代码量不代表维护成本，旧 primitive 类壳仍由 5d 集中删除；未把未来删除或生成产物计作本批净收益。当前 43 条 entry 中 30 条参数 entry 使用 DTO，5 条无参数；剩余 8 条 Composite 待迁移。
