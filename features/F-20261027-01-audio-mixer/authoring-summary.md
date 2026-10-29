# 音频效果创作交互实现记录

2026-10-27。用户先要求提交已有工作，再授权实现 [创作设计](authoring-design.md)。后端与设计基线已提交为 `f3c19655`；本记录描述其后的创作交互增量。并行聊天的 Inspector/持续视觉预设改动由其各自提交，本轮没有重置或覆盖这些内容。

## 实现

- 应用效果表单使用类型 ComboBox，类型改变时将类型键与目录默认参数作为一次历史提交；参数以共享数值字段编辑，显示范围错误。范围、默认值和 PCM 默认值复用契约元数据/常量。
- GUI 创建时自动生成隐藏实例身份。新增 AudioEffect 身份域，在现有输出/引用声明、身份遍历、批量复制与引用索引中复用；复制按原 scope/target 限定重映射，不改算法 ID 或 Track/Bus 声明 ID。
- 复用现有 Graph 遍历、条件合并、固定点、Group 前缀缓存和项目查询生命周期，在同一分析状态中增加独立音频集合及完整性。结果保留可能的创建来源与类型集合，不把音频效果加入视觉可动画对象。
- 停止与更新选择当前剧情位置之前、直接挂载在选定目标上的实例，按类型和创建来源区分同类效果。停止提供显式“全部效果”，选择原子写入 all/instanceId。音源停止不删除效果；Track 不列出所属 Bus 的效果。
- 新增 `audio.seffect.update`，将参数 patch 合入已有实例后整体验证；失败保留旧值，不创建缺失实例、不改类型或链顺序。选中来源类型不唯一时不生成猜测的参数表单；改变目标后清除原 patch，字段草稿按目标/实例/类型失效。
- 音频查询结果使用独立 AudioRevision，并沿用写入方 ContextRevision 和提交前有效性检查；相关修改使旧选择失效，Undo 不复活旧选择。参数和引用的未知/失效值保留原文，查询刷新不自动改选。

## 设计校正与兼容

初稿错误地将可空 DTO 字段解释为 Runtime 允许创建空实例。实际 `ValidateEffect` 已拒绝空/空白身份；设计和手册已校正。省略 stop 的实例仍是精确空键查找、通常无操作，不代表全部。新 `all` 默认 false，true 与非空实例互斥。既有非空显式身份、线字段和音频存档表结构保留，无需为这些创作变更提升存档版本。

没有为复用而另写音频 Graph 分析器，也没有大范围重命名 Scene 外观 API；共享状态传播中增加领域 payload。已有场景/播放结果保持独立。没有新增 Timeline 音频事件、音频参数动画、Bus 路由/Send 或实时预听。

## 验证

最终采用独立输出 `artifacts/audio-isolated`，不覆盖正在运行的 Editor。构建参数为 `--no-restore -m:1 -p:RunAnalyzers=false -p:UseSharedCompilation=false`，设置 AVALONIA_TELEMETRY_OPTOUT=1。测试前使用同次构建的默认契约 DLL。

| 检查 | 结果 |
| --- | --- |
| 解决方案构建 | 0 错误 |
| Editor.Shared 全套（含新增音频分析/身份/引用 7 项） | 456 通过 |
| Media.Audio（含新增 update/stop-all 2 项） | 22 通过 |
| Primitives.Builtins | 321 通过 |
| Architecture | 52 通过 |
| Presentation | 262 通过 |
| Integration | 132 通过 |
| 音频与共享选择器最终挂载回归（含新增 2 项） | 11 通过 |
| Editor 全套 | 329 通过、2 失败；范围文案断言已修正并经上述最终复验通过，既有 Timeline Flyout 空引用保留 |

对应日志为 `artifacts/audio-authoring-*.log`；最终源码指纹位于 `audio-authoring-final-sourcehashes.json`，另保留较早的 `audio-authoring-sourcehashes.json`。四语本地化各 1125 键且无同名重复。NUnit 显式设备/原生示例用例不计入普通通过数；本轮不重复声称执行了真实设备验收。

中间失败保留：测试夹具先误用了 SchemaA、又在 Group 表单准备前读取表单，修正后两个挂载用例通过。后续范围错误显示断言误要求错误文案包含数字 1，而共享 ValueValidation 只提供稳定错误码 `value.range` 和通用文案，已改为检查错误码与可见消息。旧 TimelineEventQueryPickerKeepsPositionAndPropertyCreationUsesFrameZeroBaseline 的 Flyout 空引用仍需单独报告。

一次最终验证被提前启动的测试进程锁住输出 DLL，构建失败；日志保存为 `audio-authoring-overlap-build.log` 与 `audio-authoring-overlap-editor.log`，未用作最终验收。等待这些进程退出后，同源码构建以 0 错误通过，再启动最终测试。规则见 [lesson](../../.agents/lessons/L-20261027-04-build-before-no-build-tests.md)。

正式用法见 [音频手册](../../docs/manual/audio-mixing.md)，契约见 [Entry 类型](../../docs/spec/entry-types.md)、[Runtime](../../docs/spec/runtime.md)，查询边界见 [作者目标查询](../../docs/spec/editor-scene-targets.md)。全 feature 的后端验证仍见 [原总结](summary.md)，不覆盖其历史证据和保留项。
