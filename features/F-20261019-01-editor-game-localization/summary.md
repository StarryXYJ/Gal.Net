# 默认开发语言创作、角色表与游戏 i18n 面板

状态：done；2026-10-21。P1–P6 与取消兼容的 P3a 均已 verified。操作入口和当前契约以 [使用说明](../../docs/manual/editor/localization.md)、[Editor 游戏本地化](../../docs/spec/editor/localization.md)、[i18n](../../docs/spec/content/localization.md) 为准；[设计](design.md) 保留取舍，[审核](review.md) 记录结论和限制。

项目 TargetLocale 控制普通创作原文，Editor UI 语言独立。DTO 的 LocalizedText/CharacterReference 语义由 generator 传到嵌套字段，统一索引所有作者 Group、角色与稳定分支选项。当前作者 JSON 保留稳定文本 key 和 Entry GUID，移动不重建，复制得到独立身份并携带各语言译文；自定义未标记字符串不被推断。

角色 Dock 编辑开发语言姓名与唯一 ID，speaker 可搜索姓名/ID、选择或失焦创建；同名允许但有歧义时需选择。自动 character_n 高水位持久且不回收；改 ID 同步明确引用，被引用或分析不完整时不允许据此删除。旧 Speakers、迁移候选和旧回调按用户要求删除。

游戏本地化 Dock 包含角色、全部 Group 和分支分类，虚拟化文本行，开发语言首列，其余语言可增序。直接译文、缺失、空串、待复核/未知分别呈现；共享 key 合并提示。语言切换先预览缺译。单元格草稿跨分类与 Dock 关闭保留，相关 JSON 草稿协调、实际历史执行时再检查，Save All 共用作者文档和单栈。

当前 UTF-8 JSON 模板携带项目 GUID、稳定 key、原文指纹和上下文，支持当前分类/全部分类的单语言模板及运行文本导出；平铺 JSON 也可导入。导入先预览，未知/null 跳过，空串须明确启用，只写非开发语言的支持语言；原文变化保留译文并标复核，整批操作一次历史。交换身份首次持久预留，输出失败可能保留身份，但不保存其他 dirty 内容。

完整项目校验和构建接入 localization-semantics，确定的包内默认语言缺译、未知角色和损坏来源阻止替换输出，External 不补足默认语言。无法分析的自定义范围明确 warning/Complete=false；Runtime 仍由宿主 ITextResolver 解析，不运行自定义模块做隐式猜测。

模块：Core 字段语义与语言规范化、SchemaGenerator、Builtins 标记；Editor.Shared 索引/受管理文档/交换 JSON/发布检查；Editor 控件、角色与本地化 Dock、历史适配和文件选择；相关单元、真实挂载和构建加载回归。

最终证据：正常解决方案构建 0 错误；P6 新增 20 项通过。完整相关回归 1180 通过、2 项 P6e3 既有布局失败，另有 6 个视频环境跳过；原生文件对话框/IME 人工验证及大项目性能阈值没有宣称完成。模板→外部改写→导入→撤销重做→保存重开→构建→中英文运行解析已验证。过程和各阶段数量见 phase-1 至 phase-6。架构测试构建输出扫描修正独立提交 c7c97b7，其他并行任务未纳入本 feature。

提交历史：P1–P3 为 5549ffb；兼容移除 d2e4b82；P4 d0105ba；P5 c835243；P6 随本总结提交。后续为既有布局修复、平台人工验证及性能验收，不包含旧版本迁移或机器翻译。
