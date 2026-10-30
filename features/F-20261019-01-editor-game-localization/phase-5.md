# P5：多语言表格与语言管理

状态：verified；2026-10-21。

## 实施范围

新增项目级游戏本地化 Dock。左侧角色/全部 Group/分支分类，右侧开发语言首列和其他支持语言列；分类与行使用 ListBox 虚拟化，表格整体横向滚动，非开发语言列通过左右按钮调序。正常行只显示上下文和文本，不显示文本 key。

GameLocalizationPanelService 投影现有索引和作者文档，同分类共享 key 合为一行。单元格只读取直接包内译文，区分缺译、显式空串、待复核、复核未知。开发语言修改复用 P4 原文链，译文与关联原文指纹为一次历史，缺开发语言文本时不伪造原文；变更开发语言后旧关联不能冒充已复核。

语言新增规范化 culture 并拒绝重复，不复制原文；顺序写入 AvailableLocales，开发语言固定首列。切换开发语言使用“检查变更→显示缺译数量→应用”独立操作，应用前重新检查预览证据。项目设置原开发语言字段只读，避免绕过检查；语言历史只修改语言切片，不覆盖其他设置。

单元格多行 Ctrl+Enter/失焦提交，Escape 还原；IME 组合态不提交。草稿记录直接原文、目标译文、开发语言及分类引用，源变更/目标变更/引用删除/项目释放后拒绝旧草稿覆盖。ViewModel 为项目 scope，切分类或关闭 Dock 保留草稿；Save All 提交已访问分类的草稿，冲突保留并阻止保存，另有放弃草稿入口。

## 验证记录

首轮编译发现新的控件不能访问 TextInputMethodClient，已复用现有 TextPresenter.PreeditText 检测。下一次编译中本阶段代码通过，但工作区并行时间线测试出现 TimelineTreeNode.Descendants 未定义；未修改其他任务文件，曾用 Temp 的 CustomAfterMicrosoftCommonTargets 仅排除该未完成测试独立验证。该任务随后在 1a31d37 完成，已恢复正常构建；最终验证不排除任何文件。

新增阶段回归覆盖语言规范化/重复/顺序/保存重开、开发语言切换预览失效、全部分类及共享行、译文和复核历史、缺原文/空译文/保存撤销重做、JSON 草稿协调、过期单元格、真实输入/关闭重开/列调序/行及分类滚动；最终结果见下文。

首轮隔离定向为 9 通过、3 失败：虚拟化模板在回收时收到 null，提交期间禁用 TextBox 导致后续失焦提交丢失，另一个 JSON 草稿 fixture 错用 Get 而没有 Open。修正后 11 通过、1 失败（动态按钮不在 XAML 名称作用域，改从真实视觉树定位）；随后补充组合态测试时须在设置 Text 后注入 PreeditText，否则 TextBox 会先清除组合态。上述失败和复验均保留本地日志，不用重试隐藏产品断言。

扩充语言测试发现 I18nLocale 原逻辑把整个后缀大写，zh-Hans-CN 会变成 zh-HANS-CN，与 CaptureText 的 CultureInfo.Name 不一致；改为统一标准 culture 名称并拒绝无效 culture，补脚本语言添加→翻译→开发语言切换→正常正文提交，以及 Core 三个大小写参数和无效名称测试。该修正属于语言管理的必要契约，不加入旧格式迁移。

实际 Dock 关闭用例发现 CloseDocument 的通用 IDisposable 释放会提前销毁 scoped ViewModel；关闭本地化视图现在保留项目持有的模型，项目 scope 结束仍释放。真实菜单开关带草稿验证已通过，当时定向 14 项全部通过；随后补充队列交错用例。

异步路径检查新增 2 个历史队列交错用例，修改前均失败：入队前检查不能覆盖前序语言切换或译文编辑。单元格 guard 与开发语言预览校验现在进入实际历史 Redo 边界，修改后 i18n 定向 16 项全通过，其中新增 Editor 用例 13 项；连同 Core 新增 4 项，本阶段新增共 17 项通过。整份 metadata 与行上下文每次快照只解析/建立一次，避免按单元格反复扫描。

## 最终证据

正常完整解决方案构建成功，0 错误、10 警告；最终 Editor 定向 16 项通过。本阶段新增 17 项在完整回归中全部通过。相关全量结果如下：

| 测试项目 | 通过 | 失败 |
| --- | ---: | ---: |
| Core | 115 | 0 |
| Runtime | 85 | 0 |
| Editor.Shared | 312 | 0 |
| Architecture | 45 | 0 |
| Presentation | 243 | 0 |
| Editor | 239 | 2 |
| 合计 | 1039 | 2 |

Presentation 日志另有 6 个视频环境用例跳过（TRX 发现 249、执行 243）。Editor 两项失败为 GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor 的尾部留白 0 < 40、GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave 的宽度 482 >= 480，均已在 [P6e3](../F-20261015-02-editor-authoring-workspace/phase-6e3.md) 记录，不隐藏或宣称全绿。最后补充的队列保护只改 GUI 服务和测试，复跑完整 Editor 验证；其余模块保持前次通过的源码。

17 个 C#/XAML/四语资源文件的最终源码指纹一致，C# whitespace 验证通过；四种 UI 语言各 882 个 key 一致，文档相对链接、Git diff whitespace 检查通过。使用说明同步至 [manual](../../docs/manual/editor/localization.md)，当前契约同步至 [spec](../../docs/spec/editor/localization.md)。本地证据：Temp/localization-p5-final-solution.log、Temp/localization-p5-final-results/、Temp/localization-p5-results/p5-queue-before.trx 与 p5-queue-fixed.trx、Temp/localization-p5-format-verify.log、Temp/localization-p5-sources.json；不提交 Temp。

提交暂存时工作区又出现其他变量/历史相关并行修改，未纳入本阶段。上述构建与回归对应验证时的源码快照，不扩展为后续并行工作树的构建结论；本阶段 17 个源码文件在提交前再次核对无漂移。

## 边界

- P6 模板导入导出与发布前完整校验尚未实施。
- 没有新增旧项目或旧剪贴板兼容。
- 不宣称已完成原生 IME 人工验证或大项目性能验收。
