---
status: confirmed
feature: F-20261102-03-presentation-library
---

# 项目 shader 不应继承虚构的 progress 动画值

现象：通用 shader 以静态 progress=1 应用，存档恢复时 EffectInstance 默认的 AnimationValues.progress=0 覆盖静态参数，导致遮罩恢复成闭合。

根因：旧 EffectInstance 给宿主效果预置 progress=0，项目 shader 恢复又将整个 AnimationValues 送入呈现绑定。尚未发生的动画因此被保存为显式覆盖值。

修正：设置非空 ProgramResource 时移除这个旧默认值；只持久化真实写入的动画参数。无 program 的宿主效果保留原默认值。

验证：PresentationShowcaseTests.Static_shader_progress_survives_snapshot_without_a_fabricated_animation_override 检查真实状态往返后保留静态 progress=1 且 AnimationValues 无 progress；既有 Builtins 回归通过。

适用：新增通用 shader 属性、存档恢复和静态初始值。禁止用默认零值填充所有未被动画修改的 shader 参数。
