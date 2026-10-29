# Timeline 结构覆盖与按目标诊断

后续修订：[开放 shader 参数](follow-up-open-shader-parameters.md)。本文“保留旧 shader 字段并 warning”的早期判断已撤回；合法附加值不产生 warning。轨道引用自身缺失/属性不支持的诊断仍有效。

2026-10-27 用户再次反馈：效果参数仍有提示（可能为切换 shader 后保留的旧成员，用户允许继续保留）；Timeline 根字段仍有 `Structured value has no traversable field schema.`，且 `/parameters/timeline/tracks/0/handleId` 报 `scene.reference.uncertain`。

用户样例为 60 FPS/60 帧 Timeline，目标 `c2121d4d69544d4d8a584d392dfedbe6` 的 radius 轨道从 32 到 34.3，Replace/Linear；第 8 帧含 layer.show 事件。没有提供此前目标的创建 Entry，因此不能仅凭此片段证明其运行时存在性或 shader 声明有效。

此次用户明确要求处理 Timeline，扩展前一补充未包含的范围：

- TimelineDefinition、Track、Keyframe、Event 的运行 DTO 提供共享 Field schema，保持原有 JSON/wire 与默认实例行为。
- Editor.Shared 将 editorId/editorTangents 作者 metadata 合入只读结构；事件参数根据当前项目 Entry catalog 和事件 type 解析，效果事件继续复用 shader 参数结构。不运行游戏逻辑、不修改作者 JSON。
- 已关联 Timeline 中的 shader 资产也参与参数扫描的 metadata 捕获；未知事件/成员/资源保持覆盖诊断。
- 诊断层对确定、能力完整且已知当前属性值的目标，不再因另一个对象的能力缺失而给出 uncertain。全局 Complete 与删除/重排影响分析仍保留原保守语义；目标自身缺能力、属性不支持/值不可确定、条件或未知引用仍保留诊断。
- shader 切换留下的旧字段不清除，仍按未知成员报告；不把已知结构覆盖问题与作者数据扩展混为一谈。

验证计划：用户原样例的无副作用遍历、大小写/枚举/编辑器切线、事件内动态 schema 与本地化语义、未知成员及缺失 metadata；无关效果能力缺失不能污染已知 radius 轨道，但目标自身缺失/不支持属性必须仍报警；实际 Editor Problems/导航回归。

## 验证结果

- Editor.Shared Release 构建通过；聚焦 138 项通过后，全量 **470 项全部通过**。新增 4 项 Timeline 结构用例与 1 项按目标诊断用例，包括用户 radius/第 8 帧 layer.show 样例、事件内 shader、自定义项目事件字段的本地化语义、未知成员定位。
- Editor Release 构建通过；聚焦 **8 项全部通过**，含新增实际 Problems/资源刷新用例，以及原有 shader 编辑、保存重开和警告导航。旧导航测试改用确实缺少 shader metadata 的事件 payload，正常 Timeline 不再被当作 opaque 测试夹具。
- Primitives.Builtins Release 构建与全量 **321 项全部通过**，覆盖运行契约、Timeline 执行与序列化相关回归。
- 本轮 `git diff --check` 通过。证据为 `TestResults/timeline-structure-{shared-full,editor,builtins}.trx` 及同名前缀构建日志；聚焦与全量不重复累计。不声称执行了整解决方案构建。

正式本地化规范、场景查询规范与作者手册已同步。工作区中其他 Timeline/UI 和 JSON/AOT 改动保留，没有提交或回滚。

状态：本轮结构补全及已复现的按目标误报修复已验证。随后用户提供完整 Group 并确认 Timeline 不再 warning；剩余问题为已知 shader 的 angle 附加参数被错误警告，已转入开放参数补充修复。目标自身缺失、shader 不支持轨道属性等有效诊断仍保留。
