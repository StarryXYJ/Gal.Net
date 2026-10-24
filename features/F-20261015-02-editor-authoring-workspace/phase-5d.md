# P5d：Entry 作者绑定迁移

日期：2026-10-18。状态：verified。用户已确认继续实施。

本切片将 Entry 类型、条件和参数编辑改为项目级显式提交。历史只保留 Group/Entry 稳定身份和作者值，不保留 Workspace、页面或表单；移除 Workspace 的 Entry PropertyChanged 补记历史与参数字符串写回链。新增条目的旧历史路径一并接入已有项目级批量服务，删除、重排继续复用该服务。

类型与参数作为完整快照提交和撤销；参数以作者 JSON 为准，生成表单直接提交 JSON patch，专属文本/资源/表达式控件通过契约转换后提交。打开/重建表单不清理参数，不把未知字段或未完成内容当作垃圾删除。局部编辑继续使用 partial 校验，完整校验留在编译。旧表单在类型变化、移除条目、关闭页面、项目关闭后拒绝晚提交。字段编辑失败尝试恢复，恢复失败进入 RepairRequired。

验证目标：真实条件与参数控件、跨 Group 交替编辑、单步历史、类型完整恢复、页面关闭后的 Undo/Redo、保存重开、缺省/null/未知参数保留、过期提交、失败补偿与项目历史游标。范围不含 Preview 旧变量路径、游戏预览重构、状态查询/Locator 或复杂编辑器扩展。

边界核对：现有 EditorDocumentRepository.Load/PrepareSave 仍调用完整契约校验，SerializeEntry 也拒绝未知参数；Shared 安全测试明确要求保存拒绝这些内容。故 P5d 只验证已知有效条目的文件往返，以及未知/缺省/null 值在内存、字段编辑与撤销快照中的保留，不把内存无损投影宣称为未知或未完成作者文件可保存。P5 需要追加独立草稿持久化切片，区分结构保存与完整语义检查，并修订现有安全测试的责任边界；此处不静默降低全局验证标准。

首轮构建成功；现有 Group/Entry/DTO/变量聚焦 39 项通过。新增跨两个真实页面的条件、默认 SchemaForm 数值与专属文本提交，关闭页面后完整 Undo/Redo、保存重开；JSON 缺省/null/未知字段内存保留、旧绑定在换类型/删除及其撤销后不复活；通知一次失败补偿、持续失败 RepairRequired 与外部变更冲突拒绝。新增聚焦 4 项全部通过。

## 最终验证

隔离输出解决方案构建成功，0 错误、48 条编译/分析器警告；未宣称零警告。Editor 154、Shared 194、Core 111、Architecture 45、Integration 110，共 614 项通过、0 失败、0 跳过。删除仅针对旧监听器的 1 项测试，新增 4 项字段提交/补偿测试，Editor 从 151 变为 154 项；原有类型切换单历史、剪贴板/重排、变量引用、DTO override 与 partial/full 校验回归一并通过。

自有 12 个 C# 文件 whitespace/IDE0005 verify 通过，受检 C#/四语文件前后 SHA256 一致，git diff --check 通过。证据目录为 C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/group-ui-validation，包含 p5d-solution-build.log、p5d-*-final.trx、p5d-binding-focused.trx 与 p5d-source-sha256.json。格式化曾因使用 git diff 枚举范围而被自动审批拒绝，改为显式列举本次文件后通过，没有请求额外权限或改变其他任务文件。

自查：字段历史按稳定 ID 和预期值执行；项目层不保存页面/控件闭包；生成表单直接读写 JSON，专属控件刷新不反向生成第二条历史；新建批量使用同一项目入口；相同值不入栈；删除/类型变化/页面关闭使旧绑定失效；通知失败恢复与 RepairRequired 均有故障注入验证。同步 editor-groups、editor-history-and-save、i18n 和 feature 计划。P5d 完成，P5e/P6/P7 及 Preview 均未宣称完成。
