# P5e：作者草稿持久化

日期：2026-10-18。状态：verified。用户已授权继续。

EditorDocumentRepository 显式区分 Complete 与 Authoring：默认 Complete 服务 CLI 编辑/检查的既有严格路径；GUI scope 注册 Authoring，加载及保存允许不完整条目、未知类型/参数、错误类型的参数值和未完成条件表达式。仍验证当前格式、严格顶层/条目 shape、JSON 重复属性、稳定身份、Graph 拓扑、变量声明、源路径/存在性/唯一归属及外部变更，不把 GUI 作者模式等同于全局 structureOnly（后者会跳过源引用检查）。CLI validate/group compile、预览的编译路径继续严格。

未知 schema 在 Group 中提示内容保留，既有条目及 Graph 剪贴板按原始 JSON 保留，粘贴分配新身份，支持项目 Undo/Redo。未知 schema 通用 JSON 修改器仍留 P6；本切片不伪造表单或自动修正内容。已知字段表单继续 partial 校验，保存不执行 Normalize、不补默认值、不重写显式 null。专属字段改为单字段 JSON 提交，避免把其他无效的已知字符串参数强制转换成字符串。

验证目标：未知/不完整/无效值/条件在真实文件中保存重开，已挂载 Group 表单可编辑已知字段且保留其他 JSON；复制、换类型及撤销往返；作者模式保留严格结构和路径防护；默认 Complete 安全测试与真实 CLI 验证/编译仍拒绝语义无效内容。最终证据见下。

首轮 GUI 聚焦 1/2：草稿保存仍报 Unknown parameter 'future'，定位为公共 Services() fixture 覆盖生产注册、手工构造默认 Complete 仓库。修正为普通场景保留生产注册，故障注入场景才显式包装 Authoring 仓库；未放宽 CLI 默认策略。修复后 GUI 聚焦 2/2，包括已挂载表单四语刷新、无效已知字符串值保留、未知 Entry 复制和 Graph 复制/撤销、换类型撤销、两轮保存重开。经验见 L-20261018-02-fixture-service-policy。

Shared 初始定向 19 项通过，随后补充 structureOnly 即使显式注入 catalog 也不执行语义/复合编译的回归。测试 CLI helper 在 --artifacts-path 布局优先选择同轮 Headless 程序，避免固定默认 bin 路径导致验证旧程序；默认输出布局保留原有入口。CLI 的代码路径与默认 Complete 策略未更换。

## P5 整体退出条件核对

P5a 交付多 Group 条目交互、搜索/键盘插入、折叠与选择；P5b 交付多选/剪贴板和批量历史；P5c 交付玩家/存档变量声明面板、引用及补全；P5d 交付作者字段提交、跨页面历史与旧绑定失效。P5e 补上已知/未知/不完整条目的文件往返，覆盖类型切换后先保存再撤销、参数完整恢复并重新 dirty。

基本标量/枚举与默认 DTO 表单沿用已验证的共享控件，字段局部校验与完整编译分开。复杂值编辑、未知 schema 通用 JSON 修改器、状态查询/Locator 与更完整的动态 schema 生命周期继续按 P6；Problems/检查交互和前期整体验收按 P7。原生输入法及完整桌面人工验收未在本切片执行，不把 Headless 挂载验证表述为人工验收。

## 最终门禁

隔离解决方案构建通过，0 错误、71 条编译/分析器警告，未宣称零警告。Editor 155、Shared 214、Core 111、Architecture 45、Integration 110，共 635 项通过、0 失败、0 跳过。Shared 的 20 项新增覆盖作者/完整语义边界、Raw 结构拒绝、路径及 Graph 条件、显式 structureOnly 与真实 CLI；原有默认 Complete 严格安全回归继续全部通过。Editor 新增完整挂载生命周期用例，原剪贴板拒绝测试改为拒绝格式错误并额外验证未知 schema 保留，没有移除格式或超限断言。

自有 11 个 C# 文件 whitespace/IDE0005 verify 均通过，受检 C#/AXAML/四语文件 SHA256 前后一致；git diff --check 通过。证据目录为 C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/group-ui-validation，包含 p5e-solution-build.log、p5e-*-final.trx、p5e-source-sha256.json 和 authoring-drafts.png。截图确认未知类型和未知参数的保留提示可见；既有 instanceId 字段缺译及窄标签截断留 P6 字段 UI 整理，不宣称全部 schema 字段翻译完成。四语静态资源各 611 key。

自查保存模式注册、完整编译边界、未归一化的 JSON/缺省/null、大数保留、类型保存后撤销的 dirty、未知 Entry/Graph 复制与身份重建、严格结构/路径/外部冲突检查，以及生产 DI/测试 fault 注入分离。P5a–P5e 的退出条件均有实现和回归证据，P5 标记 verified；P6/P7 与游戏预览未标记完成。
