# 效果参数结构补全

后续语义修订：[合法 shader 扩展参数不产生 warning](follow-up-open-shader-parameters.md)。本记录中早期将 shader 额外成员按 field.unknown 处理的结论已被用户纠正；已知 shader 的开放参数规则以该后续记录与正式规范为准。

2026-10-27 用户反馈：最新提交后，应用特效、应用音频效果的参数 JSON 仍报告 `Structured value has no traversable field schema.`。

原因：首轮迁移只关联 Transform/Flipbook；动态参数表单已经消费效果目录和 shader metadata，但本地化扫描没有收到同一份元数据。此前 summary 将动态迁移留待后续，不能认为正常效果参数的覆盖已完成。

本次按用户反馈补全应用效果、音频效果、shader 预设参数以及音频更新参数的扫描。统一复用 shader 字段投影和效果目录字段，通过每次作者快照的 Entry 类型、program/id 和前缀音频实例身份选择结构。未知类型、资源不可用、未知参数及不确定的更新目标仍保留覆盖诊断。显式自定义字段结构优先，不覆盖契约提供的 schema。

Phase：实现共享字段投影及扫描适配 → 接通项目资源代次、Editor/磁盘验证 → 补回归、同步规范。资源加载继续归现有项目服务；分析接收该代不可变 metadata，不依赖 Avalonia 或执行游戏代码。Timeline 事件全量迁移仍另列范围。

验证要求：有效视觉/音频参数不再 opaque；向量、类型切换、未知成员、未知类型、缺失资源、资源刷新、作用域不同但同名实例、前后文变化、Undo/Redo；扫描不修改作者数据，保留原引用语义和定位。

## 实现

- EntryParameterStructures 在每个作者快照内把所选效果的参数字段关联到 ValueDocument，保留显式契约结构的优先级；不将普通字符串推断为文本引用。
- ShaderEffectFields 从原表单提取公共字段投影；EffectParameterDefinition.Field 和既有 SEffectParameterDefinition.Field 供目录校验与扫描复用。
- ProjectSceneTargets 实现 IEntryParameterMetadataSource，复用现有资源准备；目录、shader 声明与 Revision 在同一锁内捕获。ProjectLocalizationQuery 的缓存同时跟踪作者内容和该资源代次。异步发布不能把旧声明标记为新代结果。
- shader 加载读取托管工作版；音频 update 通过共享 SceneTargetAnalysis 的前缀查询，以 scope/targetId/instanceId 匹配，未来、已停止或不确定类型保持 opaque。
- 磁盘本地化验证只解析 effect-program 的声明块，不创建渲染器。未知资源、重复 GUID 或不可读清单保留覆盖诊断。

## 验证

- Editor 首轮 6 项通过，包含新增的实际 Problems/资源刷新/未保存 shader 用例；TRX：`TestResults/effects-editor.trx`。
- Editor.Shared 全量 465 项通过（新增 7 项），包括内置音效、shader 全参数种类、未知成员/资源、自定义目录、音频更新的完整附件身份、磁盘解析和重复资产 ID；TRX：`TestResults/effects-shared.trx`。
- 最终 Editor Release 构建成功，扩大聚焦 11 项全部通过，包含 shader 实际编辑/预览、音频类型切换与更新、元数据刷新、问题定位和保存重开；TRX：`TestResults/effects-editor-final.trx`。首轮 6 项不重复累计。
- 并行序列化迁移整合后，Editor.Shared 再次构建成功，效果/本地化索引聚焦 32 项全部通过；TRX：`TestResults/effects-shared-final.trx`。不与此前全量累计。
- `git diff --check` 的本轮范围通过。验证使用 `AVALONIA_TELEMETRY_OPTOUT=1`、`-m:1 -nr:false -p:UseSharedCompilation=false`，不让共享编译器/遥测目录权限混入产品诊断。没有执行整解决方案构建或宣称原 feature 所有 P5 项完成。

工作区在本轮期间存在并行 JSON/AOT 迁移；未回滚其改动。验证过程中修正了新生成的 `.WithMember(...)` 链前目标类型 `new(...)` 无法推断的单行生成代码，以及 JsonCodec 的缺失 using；这些属于当前工作区编译整合，不将并行迁移的其余改动归为本次效果参数修复。

状态：本补充修复已实现并验证。正式本地化规范与创作工作区手册已同步；Timeline 全量结构迁移仍在本补充范围之外。
