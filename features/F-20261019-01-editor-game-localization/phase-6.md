# P6：交换、发布检查与收尾

状态：verified；2026-10-21。

## 实施细化

单语言 JSON 模板 version 1 包含 projectId（来自 exchangeProjectId）、开发/目标语言、稳定 key、开发原文及 SHA-256、上下文、nullable translation，以及分析范围 complete。平铺 JSON 为独立输入形式，null 跳过，空串须明确允许，未知 key 不能应用；导入不修改开发原文，不按行序/原文匹配。表格增加导出范围、运行文本格式、导入预览/选择与整批一次历史。

项目 GalProject.Id 实际来自目录名，不能作为持久交换身份。Graph/localization-authoring.json 的 exchangeProjectId 首次模板导出时持久预留；如编号高水位一样，旧文本历史允许缺省→已预留身份并在撤销时保留。已有身份不能被历史替换。首次身份预留可能在输出失败后保留，文本值与其他 dirty 文件不因导出保存；这是持久身份初始化对“导出失败不改会话”的明确例外，不回滚或重用已分配身份。

导出必须在项目外，拒绝目录链接，输出先写临时文件再原子替换，不能覆盖受管理来源；取消文件选择不开始导出。导入预览捕获当前作者证据，相关 JSON 草稿须先协调；实际历史执行时再校验，过期、关项目及冲突保留原值与草稿。

完整 project.validate 与 ProjectContentBuilder 接入 localization-semantics，读取包内作者来源，不把 External 当默认语言完整性依据；作者保存仍允许语义草稿，不在保存入口强制发布条件。graph/group 单独范围继续明确报告未检查完整本地化范围。

## 验证

新增 20 项全部通过：Editor 9 项、Shared 交换解析/批量叶值 9 项、完整检查范围 1 项、Integration 缺译阻断 1 项。直接相关定向合计 106 项通过（Editor 37、Shared 66、Integration 3）。

端到端覆盖模板导出→外部修改→导入→撤销/重做→保存重开→构建→ProjectGameContentProvider 加载→GameTextResolver 中英文解析；同时验证外部行重排、角色改 ID 后姓名文本身份、旧文本撤销跨身份预留、源文变化需复核、平铺证据未知、空/null/未知选择、过期/关闭/排队预览、输出路径/失败、JSON 草稿冲突和实际控件/文件选择适配。

首次测试将 Button.ClickEvent 直接 RaiseEvent，未触发 ICommand；改用真实鼠标点击后 9 项 Editor 交换用例通过。初期正常构建遇并行效果测试未完成（Capability/隐式数组类型），曾以 Temp 的 CustomAfterMicrosoftCommonTargets 排除其两个测试文件做隔离验证；对方修正后已恢复正常完整构建，最终没有排除文件。自己的 OutputRoot/Id 测试属性名修正为实际 OutputPath/ObjectId，未更改业务行为绕过验证。

最终正常解决方案构建 0 错误、118 警告。完整相关回归：Core 116、Runtime 85、Editor.Shared 329、Integration 111、Architecture 45、Presentation 243、Editor 251 通过；合计 1180 通过、2 失败。Presentation 日志另有 6 个视频环境跳过（发现 249、执行 243）。Editor 两个失败仍为 [P6e3](../F-20261015-02-editor-authoring-workspace/phase-6e3.md) 已记录的尾部留白 0 < 40 与宽度 482 >= 480；没有隐藏失败或宣称工作树全绿。

Architecture 初次 15 项失败来自 test/bin/te4c3b 临时仓库副本重复 csproj 被计为源码。保留副本，测试发现规则排除递归 bin/obj，复验 45 项通过；独立提交 c7c97b7，不混入交换业务。其余项目测试源码和业务实现未因此变化。

本阶段 24 个 C#/XAML/四语资源文件最终指纹全部一致；19 个 C# 文件格式只读检查通过，四语资源各 898 key 且无重复、集合一致，56 条相对文档链接及 diff whitespace 校验通过。证据在本地 Temp/localization-p6-final-solution.log、Temp/localization-p6-final-results/、Temp/localization-p6-results/、Temp/localization-p6-sources.json；Temp 不提交。

## 边界

- 使用 UTF-8 JSON，不新增 CSV/XLSX 或机器翻译依赖。
- 不加入旧版本模板、旧项目和旧剪贴板迁移。
- 原生文件对话框/IME 人工验证和大项目性能阈值尚未验收；自动测试使用可替换文件选择服务和真实挂载控件。
- 自定义未知/opaque 范围不执行游戏代码推断，提供 warning/Complete=false；已知缺译和角色等确定错误仍阻止发布。
