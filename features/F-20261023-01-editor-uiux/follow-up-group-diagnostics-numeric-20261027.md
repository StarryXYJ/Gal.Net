# Group 诊断与数值输入后续

状态：verified。用户确认分析后于 2026-10-27 授权开始，三个 Phase 均已实现并验证。

范围：正常待提交输入不作为错误；数字中间态保留；作者内容和依赖变化自动检查内存工作版，旧结果不再作为当前问题；本地化可操作的问题提示、去重和稳定身份导航；NumericUpDown、Slider + 精确输入、无损数字文本三条可由元数据/宿主选择的路径；浮点简写 `.3`、`-.22`。

设计：共享 codec 只对类型化数字编辑输入规范化简写，JSON 源码仍严格。共享表单保留 numeric 文本兼容，新增显式 numericupdown/numerictext 路径；Editor 的 numeric 默认使用保留原始文字的上下调整框，slider 使用配对数值输入，不把步长用作业务校验。无法安全调整的类型/范围回退文本并说明原因。表单错误由宿主格式化，Core 保持平台中立。

Problems 合并短时间内的更新，捕获当前项目快照后运行契约检查，不自动提交字段。检查结果绑定版本，变动后撤下旧活动错误；字段和场景诊断继续独立更新。提示显示 Group/条目/字段名称和修正建议，原始细节可展开；不存在目标不提供有效跳转，点击时再次核对稳定身份。

Phase 1：诊断状态、自动检查与定位；Phase 2：数字输入与控件路径；Phase 3：实际挂载/交互、保存与撤销回归、相关构建及正式文档同步。只有测试证据齐备才标 verified。

保护：开工已有 ProblemsPanelView.axaml 的 WrapPanel 排版及 CheckBox 居中改动保留。不修改运行时业务校验与文件格式。

## 实现结果

- Problems 的正常输入为 info，不计条目错误/警告数；标记按实际严重程度分色。数字中间态（包含 `1.`）在确认前中性展示，确认仍拒绝不完整值。
- 作者历史、资源和工作文件变化触发 220ms 合并检查。UI 捕获中立工作快照，Graph/Group 契约及编译在后台检查，代次拒绝过期结果；输入期间不提交、不逐字全项目检查，旧活动校验结果撤下。手动入口可立即检查。
- 本地化提示包含字段类型、上下限和修正方法，只有单边约束时不显示无穷大。来源显示 Group 名、序号、类型和字段路径。原始消息保留在技术详情，重复参数汇总去重；可跳转的正文及来源靠左且整个按钮区域可点击。
- 作者导航按稳定身份重新核对；打开对应 Group/Graph/文件/设置，清除阻挡定位的搜索、选中 Entry、展开参数、显示 Inspector。条件、普通字段及能唯一识别的 Transform/向量等子字段聚焦到实际控件，已删除目标不跟随旧序号。视图晚挂载仍接收请求，延迟焦点复核来源与字段路径。
- `NumericFieldEditors` 提供原始文字驱动的 NumericUpDown，复用 Semi/宿主主题及上下按钮/方向键/焦点内滚轮，提交使用 FieldEditorContext；模板初始化不能制造输入。显式 numericupdown/numerictext 工厂及 Editor slider 配对输入，宿主注册仍可替换。long/ulong/decimal、自定义字符串数值编码及不适用范围保留无损文本。滑块手输不受步长量化。
- Variants 透明度及音频增益已有 0–1 约束改为 slider 元数据；未改变合法范围。Transform、Flipbook、向量和音效数字复用注册表，子字段提示使用相同本地化名称；音效 update 继续保持可选 patch，同时保留 EditorHints。
- Core ValueCodec 仅在类型化浮点编辑文本中规范化前导小数点。共享表单和项目草稿 Save All 同用此入口，原始 JSON 仍严格。

## 验证

- 解决方案 `dotnet build GalNet.slnx --no-restore -m:1 /nr:false -p:UseSharedCompilation=false`，设置 AVALONIA_TELEMETRY_OPTOUT=1，成功、0 错误。最终标签补充后再次构建通过；既有分析器警告保留。
- 完整回归：Editor 336、Core 129、Editor.Shared 456、Presentation 262、Architecture 52；内置 schema/效果参数专项 11，共 1,246 个不同用例通过。TRX 为各测试项目 `TestResults/group-release-*.trx`。
- 最终本地化子字段补充后，当前产物的 16 项 Editor 聚焦再次通过，见 `group-final-authoring-verified.trx`；覆盖问题自动刷新/快速连续修改/Undo/Redo、四语提示、重复诊断合并、错误点击/实际条件与缩放 Y 聚焦、正常输入不安排全检查、原始上下按钮与方向键、清空/取消、失效字段、关闭页面后 Save All 与重开、滑块精确值、ulong/decimal 精度、Transform/Flipbook/音效及 DTO patch。
- Presentation TRX 另有四项原生视频环境用例和一项显式预览生成 NotExecuted，不计入通过数。
- 四语各 1,153 keys，一致；changed C# whitespace、git diff --check 通过。普通 solution 格式工具受沙箱命名管道限制，whitespace 使用无 MSBuild 的 folder 模式；没有声称语义格式检查通过。
- 真实主题 Headless 控件已挂载、执行输入与按钮交互，并检查截图：`artifacts/editor-group-diagnostics-20261027-session/group-problems.png`、`group-numeric.png`，另有 Group 宽/窄视图。源码指纹位于同目录 `source-fingerprints.json`。

## 验证中修正与边界

首次 NumericUpDown 适配的原生模板同步制造了未编辑草稿，回归暴露列表/设置/外部刷新问题；最终直接拥有模板输入与 spinner 事件，使用同步 PropertyChanged 捕获输入，挂载无草稿和输入后立即 Enter 均有断言。经验见 `.agents/lessons/L-20261027-05-numeric-template-drafts.md`。Timeline 对象选择器的既有测试曾在异步准备结束前读取 flyout，改为等待实际准备信号；没有通过修改产品准备语义迎合测试。新增 Core 断言使用 JsonException 的派生类型匹配，音效断言校验本地化解释及原始 issue 代码。

检查覆盖沿用现有 Graph/Group/本地化与场景静态规则，不把运行期和所有资源解码问题声称为静态可判定。无法唯一识别的复杂同名嵌套子字段仍定位到根字段。原生桌面 IME、DPI 与辅助技术体验保留人工验收边界。

正式规范同步：[Group 编辑](../../docs/spec/editor-groups.md)、[共享表单](../../docs/spec/control.md)、[历史与保存](../../docs/spec/editor-history-and-save.md)；接入示例见 [创作工作区手册](../../docs/manual/editor-authoring-workspace.md#选择数值编辑器)。
