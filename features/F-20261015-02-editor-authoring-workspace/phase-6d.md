# P6d：Timeline Dock 与项目编辑会话

日期：2026-10-18。状态：verified。用户授权继续，前置 P6c verified。

本切片实现 BottomDocument 的 Timeline 单实例面板，默认与资源/日志同组；项目级活动 Entry 上下文按来源 owner 区分页面，切换普通条目/无选择时禁用，聚焦工具不改变选择。多选只取活动项，关闭非活动 Group 页不会清除另一页的选择。

TimelineEditSession 由项目 scope 拥有，直接读作者文档并经 EntryFieldAuthoringService 提交参数，不借用 Group 页字段 lease。显示已有轨道在当前帧的纯采样值，以 NumericUpDown 编辑；Enter/失焦确认，Escape 恢复未确认值。一轮数值输入只提交一次根字段更新；已有键只改 Value 并保留切线/插值/未知字段，新键在当前帧插入并继承原区间插值与导数。

绑定捕获原 Entry 引用、根值快照、播放头和集合版本；切选择/帧/类型、外部编辑、Undo、删除/项目关闭后旧行不能提交。打开不写默认值，缺失 timeline 用明确创建动作建立内部播放身份和空轨道；无效内容提示到条目参数修复，不覆盖原值。

P6d 仅编辑已有轨道，先用对象序号区分目标且不展示 handle。目标候选、属性加入轨道、真正对象树/画布和曲线工具留 P6e；实时场景预览不纳入计划。验证目标为实际主题面板、默认 Dock/重开、选择矩阵、数值更新/插键/取消、过期行、Undo/Save/重开、未知 JSON 保留及四语。

## 验证记录

首轮聚焦测试发现动态状态文案将 `Session.StatusKey` 当作固定翻译 key；改为明确 `Path=Session.StatusKey` 后，四语真实视图断言通过。补齐 EditorViewFactory 映射并通过实际工厂创建视图，未绕过生产解析路径。最终针对性 8 项全部通过，包含 6 组缺省/null/无效作者 JSON 保留。

最终隔离解决方案构建成功（81 条分析器警告、0 错误，含新增测试的两条 CA1861 数组建议）。Editor 164、Shared 214、Core 111、Architecture 45、Integration 110，共 644 项通过、0 失败、0 跳过。没有重新运行原生媒体用例。真实 Semi 主题下挂载 Group 与 Timeline，核对 NumericUpDown 模板、键盘确认/取消、混合多选、关闭非活动页、Dock 关闭重开、Unknown JSON 与大整数、切线/插值、历史粒度、保存重开和四语切换，产出截图并人工查看。

证据目录：`C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation`。脚本 `validate-p6d.ps1`，构建/测试日志及 TRX 为 `p6d-*`，截图 `p6d-timeline-dock.png`，源码指纹 `p6d-source-sha256.json`。四语各 636 keys，新增 16 个 key。

whitespace 与 IDE0005 verify 通过；验证期间源码 SHA-256 全部一致。P6d 退出条件满足，P6e/P6f/P7 仍待实施，整个 feature 不标记完成。

未确认输入采用明确取消规则，而非完整长手势草稿协议：切 Entry/帧/相关根值、关闭 Dock 时取消；保存或无关更新、切语言时保留。统一关闭草稿协调仍留 P7。当前只隐藏 Timeline Dock 数值行的 handle；Group 的 JSON 修复路径仍可看到底层定义，不宣称已经完成全 Editor 的 Locator 作者 UI。
