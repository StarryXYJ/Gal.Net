# P5c：统一变量面板

日期：2026-10-17。状态：verified。用户已授权一个面板内区分玩家/存档变量，编辑声明/初始值及说明，运行时值留待预览。

实施范围：新增全局 Dock 变量面板、两个作用域页签与显式应用草稿；新增/修改/删除走项目统一历史，Graph 声明为唯一数据源，settings 副本由保存派生。按稳定 UID 编辑，类型变化按目标类型初值创建草稿。引用覆盖 Graph 分支条件、所有已加载作者 Group（含未打开页面）的 Entry 条件及 schema 标注的 VariableName/Expression；字符串字面量、函数名与普通参数不猜测替换，候选给出位置。明确引用阻止删除。引用与定义重命名一次历史，更新打开的投影、补全与诊断。旧 Preview 实时编辑未纳入本阶段，P5 剩余作者绑定迁移单列。

实现取舍：沿用表达式的方括号/裸标识符词法边界，跳过引号内容、函数及限定符；不宣称这是完整 NCalc AST 重构。不可靠词法内容只作候选。旧 CLI 的通用字符串替换不复用，避免改动对白。新增声明说明字段并保留克隆/保存往返。

## 验证过程

首次测试编译遗漏 Graph 模型命名空间，补齐后聚焦 3/4：说明已加入中立声明，但严格 Raw Graph shape 仍拒绝 description。补齐唯一变量 shape 的可选字符串字段，没有放宽其他未知字段检查，并增加 Shared 的字符串/数字/null 三例。修复后 4/4，随后增加按现有 NCalc 语法预检以及已挂载条件诊断刷新断言，聚焦再次 4/4（p5c-focused-final.trx）。

检查 variables-panel.png，布局包含作用域页签、声明列表、草稿、类型提示、应用/还原/删除及引用区域。验证全局 Dock 单实例与关闭、变量及引用单步 Undo/Redo、不误改文字/函数/不可靠表达式、未打开 Group、跨作用域重名、默认值溢出/NaN/小数点规则、过期草稿、通知失败恢复及 RepairRequired，覆盖保存/重开和说明/UID 保留。

最终 validate-p5c.ps1 已通过：只格式化自有 C#，解决方案隔离输出构建，Editor/Shared/Core/Architecture/Integration 全量回归，最终 whitespace/IDE0005 verify 与源码指纹核对。源码仍为共享工作树，其他任务的游戏示例/shader/运行时文档/PlayerInputTests/ComplexPrimitiveOptions 改动保留并排除提交。

最终解决方案构建成功，0 错误、73 条编译/分析器警告（含既有模块及测试分析器建议，不宣称零警告）。五个测试项目最终全部通过。源码清单/哈希、日志、TRX 与截图均在 C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/group-ui-validation。

## 最终门禁与范围

Editor 151、Shared 194、Core 111、Architecture 45、Integration 110，共 611 项通过、0 失败、0 未执行。自有 C# 的 whitespace/IDE0005 --verify-no-changes 通过，受检 C#/AXAML/四语文件的前后 SHA256 一致；p5c-source-sha256.json、p5c-*-final.trx 与 validate-p5c.ps1 可复核。Shared 新增 description 的合法字符串/数字/null 三例全部通过。

自查了新面板的 Dock 单例与释放、默认预览未启用、草稿不进入项目内容、声明唯一真源、UID/类型/说明保留、引用范围与字面量保护、单历史与旧监听去重、失败恢复/冲突拒绝、四语切换及源码提交边界。Formal docs 已同步 editor-variables、editor-graph、file-formats 和 i18n；旧 CLI 的重命名算法没有在此阶段统一，明确引用扫描不宣称完整 AST 语义分析。未将 P5 剩余作者绑定迁移或 P6 状态查询宣称完成。

## 默认值控件跟进（2026-10-17）

按用户要求，Bool 默认值使用 CheckBox，Int/Float 使用 NumericUpDown（步长 1/0.1），String 保留 TextBox。仍编辑同一份草稿，应用才写项目历史；数字采用固定小数点格式。NumericUpDown 绑定原始文本，避免打开/应用时通过 decimal 值转换截断 float 范围或精度，最终合法性由原有声明服务检查。四语 Bool 提示同步改为勾选说明。

扩展现有挂载交互测试，验证复选框点击、数值控件编辑、类型可见性、文本输入、应用前不改作者文档、Undo/Redo 回填，以及 1E+35、1E-35 回填和应用往返。首次构建发现应使用 NumberFormat 而非 CultureInfo 属性，修正后通过。最终隔离 Editor 测试项目构建 0 错误、20 条编译/分析器警告；Editor 全量 151 项通过、0 失败、0 跳过。本次两个 C# 文件 whitespace verify 与自有 diff 空白检查通过，截图已检查。证据为上述验证目录中的 variables-typed-build.log、variables-typed-editor.trx、variables-panel.png；本次未重跑上一节其他四个测试项目。
