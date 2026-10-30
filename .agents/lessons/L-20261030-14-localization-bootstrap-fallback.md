# 合并 UI 文案不能提前执行默认语言回退

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications / P6c
- 范围：AvaloniaPlayerLocalization、GameTextResolver、通用 UI 与项目嵌入文案

现象：拆分通用 UI 和 Sample 文案后，先用一个带默认语言回退的复合 provider 合并 UI，再把它作为多个文化的 bootstrap 输入内容 resolver。当前语言为 zh-CN、项目只有英文某键、外部翻译覆盖该英文键时，错误得到项目英文而不是外部英文。

根因：内层复合 provider 在 zh-CN 查找中提前回退到 en-US，外层 resolver 把该结果视作 zh-CN 命中，尚未检查 en-US 的高优先级外部来源就结束。

正确做法：启动期可以使用合并后的 UI provider；AttachGame 时保留并展开原始的精确文化来源，让外层 resolver 在检查全部当前文化来源之后统一做父文化/默认文化回退。不要把已经带回退的聚合查找伪装成每个文化的精确来源。

验证：Presentation 的 `ProjectResourceFallbackDoesNotHideExternalDefaultCultureOverrides` 在修复前明确失败（Expected External English，实际 Project English）；保留失败 log `out/cross-platform-p6c/fallback-before-fix.log`。修复后随呈现测试验证；已有默认 UI、项目覆盖、外部翻译及重复 AttachGame 的集成测试同时回归。

禁止做法：改变翻译覆盖优先级以迁就聚合 provider，或只用 UI 默认 en-US、有完整双语词条的样例证明回退正确。后续调试模块等新增文案来源必须覆盖缺键和默认语言覆盖场景。
