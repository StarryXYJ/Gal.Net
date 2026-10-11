# 已存储的 i18n key 与执行位置

状态：confirmed（用户确认，2026-10-10）

来源：F-20261010-02-runtime-i18n。

现象：实现讨论把 raw ID 当成需要透传 Runtime 的本地化身份，随后又提出在编译时将作者文本改写成 key；两者均偏离用户确认的契约。

根因：混淆了 Editor 生成 key 的输入与 Runtime 消费的完整 key。每个 group 的 Entry.Id 在编译前后都表示位置，组合展开会改变编号。content 在 raw 阶段已是 key。

正确做法：未来 Editor 自动生成 key 时使用 raw 阶段的 ID；key 保存后原样编译、展开、执行。不要按运行时序号重新生成，不增加仅为解析 key 使用的 SourceEntryId 属性，不擅自把 raw content 解释为源文本。

适用：GalNet 内置文本条目与未来翻译编辑工具。自定义参数的本地化语义仍由 implement 决定。
