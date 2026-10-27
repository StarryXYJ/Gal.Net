# Shader 扩展参数与封闭 DTO 的边界

状态：confirmed
来源：features/F-20261027-03-recursive-properties/follow-up-open-shader-parameters.md

## 现象与根因

切换 shader 后保留 angle 等旧参数时，扫描曾将其报告为 field.unknown；用户确认这类参数合法，不应 warning。扫描把 shader 参数目录误当成封闭 DTO，而渲染器 ShaderProgramTextureEffect 仅枚举 descriptor.Parameters 绑定 uniform/texture，额外数据不被消费。

## 正确做法

已知 shader 的参数对象显式声明为开放对象，附加成员是没有子引用语义的原子 JSON。保留任意合法附加值，不生成漏分析 warning，不猜测其中的文本/角色 key。已声明字段仍校验类型/范围，重复成员仍诊断，未知 shader 仍报告无法确定结构。该规则也适用于 shader 预设和 Timeline 效果事件。

## 适用范围与禁止做法

规则来自领域消费者对扩展数据的明确忽略语义；不可全局关闭未知 DTO/未知 schema 的诊断，也不可自动套用到仍有封闭校验的音效或 legacy catalog。不能把“不写作者数据”等同于“必须给作者 warning”，也不能把一条合法扩展的 warning 解释为用户需要手工清理。

## 发现与验证

用户提供 Bloom 参数对象 angle/strength/softKnee/radius/threshold 并明确纠正。源码核对 TextureEffects.cs 的 descriptor 参数枚举；Core 开放对象回归验证附加对象/数组/空键、已知字段校验、重复成员、局部 patch 与别名边界。相关正式规范：docs/spec/entry-types.md、docs/spec/editor-game-localization.md。
