# 设计

沿用 BuiltinAnimationCurve / IAnimationCurve 和枚举生成的 Choice；Linear、Step 加十类 In/Out/InOut。旧 Ease 名称替换为 QuadEase 名称。统一由 In 曲线通过时间反转及半程映射生成 Out/InOut，避免重复公式。端点精确返回 0/1，输入时间夹到 [0,1]，Back/Elastic 允许进度过冲；最终属性范围仍由现有呈现层约束。无新依赖及调度机制。
