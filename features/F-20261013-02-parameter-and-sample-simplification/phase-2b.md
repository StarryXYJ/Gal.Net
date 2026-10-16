# Phase 2b：共享 SchemaForm 与工厂注册表

日期：2026-10-14。状态：verified。

## 实现

GalNet.Avalonia.Controls 的 Forms 命名空间承载 SchemaForm、EditorRegistry、FieldEditorContext/Lease 和 IFieldBinding；没有新增项目或 Core 平台依赖。表单只遍历有序字段、解析工厂、挂载与清理；默认类型选择留在 registry，控件创建/绑定留在工厂。

- 宿主显式创建 registry，默认集合可 Register 扩展、Replace 替换；主题可贡献样式/工厂，由宿主组合。注册更新后显式 RefreshEditors，不加入插件扫描/全局服务容器。
- 默认 Text/Numeric/JSON 使用文本编辑，保留 `-`、`1.` 等草稿及超过 double 精度的整数；Enter/失焦只提交实际编辑过的文本。Slider/Toggle/Choice 使用同一 Context 与 Core 值处理，尊重项目 JSON 配置与 converter。
- Slider 要求有限范围/正步长，拒绝 decimal、不安全整数精度、超 CLR 类型范围及非整数整数滑块配置；整数小数不通过转换被四舍五入为合法值。Step 只作交互提示，业务校验仍按 schema 值约束。
- FieldValue 明确区分缺省与显式 null；默认只作缺省展示，不因控件刷新或无编辑失焦写回。默认结构不添加统一删除/null 操作按钮，宿主可通过 Context.CommitAsync 显式提交这些状态。
- binding 借用宿主对象；Changed 支持后台通知，值/选项/可用性更新不重建控件。草稿、基础/业务诊断、异步失败和版本保护集中在 Context；工厂不重复保存/撤销/异步应用逻辑。
- 换输入、卸载和创建失败先取消全部 Context、退订，再释放全部 lease，最后移除控件，避免移除焦点触发旧字段提交。清理失败仍继续，并保留 Diagnostics；过期请求结果不更新新 UI。宿主仍需捕获正确作用域并遵守取消令牌，控件不回滚已完成的业务写入。
- 复用原 SettingEditorLease 的职责，将其迁入共享层并命名 FieldEditorLease；CustomSetting/CustomSettingEditor 和原主题测试已接入，不保留旧兼容类型。

结构样式 SchemaForm.axaml 只提供标签、子控件和诊断布局，无颜色、间距、皮肤。宿主显式加载或替换 Theme/Template；自定义工厂可保留自己的 DataContext，行项目独立持有 Context/Control。API、注册与所有权示例见 docs/spec/control.md、docs/development.md。

Core 只将已有 ValueCodec.ParseEditorText 开放给草稿消费者，不另写一套解析规则；校验仍在提交前使用公共 ValueValidation。Editor 和 Sample 的正式 binding/工厂组合尚未迁移，属于 Phase 2c。

## 验证

串行执行 dotnet test，统一 --no-restore -m:1 -p:UseSharedCompilation=false，禁用 Avalonia telemetry。

| 项目 | 范围 | 结果 |
|---|---|---|
| GalNet.Presentation.Tests | 全部，含 21 项新表单测试和原自定义设置生命周期 | 155/155 |
| GalNet.Core.Tests | 全部 | 90/90 |
| GalNet.Architecture.Tests | 全部 | 31/31 |
| GalNet.IntegrationTests | ScreenshotEntryTests，构建 Sample | 14/14 |

合计 290 项通过。表单测试使用真实 Headless Window、FluentTheme 与结构样式，验证视觉树中实际子控件、真实输入/失焦/选择事件、默认/显式/自定义映射、有序独立实例、后台通知、动态选项、草稿/null/default、JSON converter、释放故障和过期异步结果。日志为 Temp/F-20261013-02-tests/phase-2b-*.trx，不提交。

11 个拥有的 C# 文件完成 whitespace 整理和 import/IDE0005 检查，git diff --check 通过。现有命名/分析器/Avalonia 警告不扩大清理。Headless 构建零警告/错误；契约独立构建并完整重新部署 GameTestCase/Contracts；实际 build GameTestCase 返回 success=true、4 个 Group、diagnostics=[]。未执行人工 GUI 或全解决方案回归。

## 修正与成本

初始编译修正了 Slider 工厂与控件类同名、测试使用不存在的 GetVisualRoot API；失焦测试改为实际 FocusChangedEventArgs。第一次行为验证发现只有工厂对象、没有视觉子控件，结构样式增加显式主题选择；真正挂载后又发现选择模板中间 null 状态，修为 null-safe。早期失败不计入上述通过数，[lesson](../../.agents/lessons/L-20261014-03-form-tests-must-mount-editors.md) 已记录。

新增 6 个生产 C# 文件 544 行、1 个结构 XAML 32 行；既有生产文件净减 4 行，总生产净增 572 行（含注释/空白，不含测试/文档）。这是新增表单能力，不宣称本阶段总行数下降。成本集中在一个状态/草稿 Context、一个表单生命周期和一组默认绑定工厂，没有新增完整表单 DSL、DI 层或第二套 lease。复用收益还需 Phase 2c 的实际宿主迁移验证。

工作区的其他音频改动、NVL lesson 与未来 Entry 作者路径的计划调整均保留，不纳入本阶段提交。下一步按现有计划实现 Phase 2c，验证 Editor 撤销/保存与设置应用/持久化的独立适配。
