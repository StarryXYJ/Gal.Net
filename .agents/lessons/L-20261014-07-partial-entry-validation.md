# 局部 Entry 创作不能要求完整业务字段

状态：confirmed；来源：F-20261013-02 Phase 5c。

现象：particle.play DTO 表单无论先填 instanceId 还是 parameters 都失败。先填 payload 抛 InvalidDataException（缺实例 ID），先填 ID 抛 KeyNotFoundException（尚无 parameters）。定向两项失败记录 phase5c-editor-partial-before.trx。

根因：完整指令 validator 依赖 Normalize 已补的默认值和其他必备业务字段；逐字段编辑提交的是未完成的作者字典，直接调用会形成创作死锁。

正确做法：使用 DynamicParameterTable/ForDto 的显式 validatePartialArguments 区分局部校验；未配置时仍沿用原 validator。内置完整业务检查保留在编译/执行 Normalize，局部编辑继续检查字段类型和约束。不要吞掉完整编译异常，也不要为解决首字段提交问题降低粒子贴图/视频/计划的完整合法性。

发现和验证：增加粒子两种填写顺序的真实 Editor binding 回归；每次首字段可提交，未完成内容完整 Normalize 仍拒绝，填完后通过。原自定义逐字段 validator 测试继续运行。

适用范围：生成 DTO 表单、跨字段业务规则、尚未补默认值的作者数据。关联：docs/development.md、feature design.md。
