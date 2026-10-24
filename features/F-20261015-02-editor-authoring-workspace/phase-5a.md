# P5a：Group 创作交互

日期：2026-10-17。状态：verified。范围为用户确认的 Group 补充设计第 1–5 点，继续使用 P6a 共享 DTO 表单；不将整个 P5 的剩余多选/剪贴板、变量 UI 和作者绑定迁移宣称完成。

## 实现

- 类型弹窗新增搜索/默认焦点、四语显示名与英文 ID、常用内置拼音/自定义别名、首次上下首项/继续导航/Enter/取消及 IME 预编辑让路；缺译回退为英文 ID。补齐现行内置类型和分类的翻译，参数表单的字段标签使用已有 Entry.Parameter key。
- 顶部添加/追加/数量/插入类型，移除底部按钮区域；数量作用两按钮，类型直接确认一条。创建前确定类型，不依赖 catalog 首项；默认类型不可用时保存待执行按钮的数量与追加/插入意图，再请求选择类型。
- 捕获稳定 Entry 锚点并在确认时复查；锚点视图被删除导致 Popup 自动关闭时同样取消待执行意图并反馈。列表尾部 64 DIP 留白和背景取消选择，不干扰工具栏/输入控件/滚动条；Space 快速插入尊重文字/数字/条件编辑及其他 Dock 页焦点。
- 展开状态按页面内稳定 ID 保存，行及拖动预览共用；移动不重新 Configure 参数控件，折叠不进入内容历史。行内切换类型使用明确意图，一次历史同时保存原类型/原参数，Undo 不留下新类型配旧参数的中间状态。

## 验证过程

首次测试编译因 partial fixture 的辅助方法 Key 遮蔽 Key 枚举而失败，已改名。聚焦首次运行 14/16：搜索用例直接读取尚未投递的 TextChanged 状态，改为运行 UI 队列后断言；锚点移除会使 Popup 在确认前自动关闭，产品改为在 Closed 同样处理失效并清理待执行意图。修正后 16/16，通过原生鼠标折叠/拖放及预览检查。

之后增加自定义契约不使用 catalog 首项，以及行内类型/参数单步 Undo，聚焦 17/17。补充字段标签时遇到 C# 14 属性内 field 关键字与主构造参数同名的编译错误，使用 @field 显式引用后通过。最终补充超过视口的列表尾部留白断言后，首轮完整 Editor 140/141：普通 ItemsPresenter margin 样式未产生实际留白。核对主题的 Padding 模板绑定后，改用 ListBox.Padding，并保留虚拟化面板；group-tail-fixed.trx 的满视口留白断言通过，随后重跑完整门禁。详见 L-20261017-06-listbox-scrollable-tail。

测试包含：打开即搜索焦点、当前四语/英文 ID/轻量别名、IME 不误确认、重复同类型确认事件、零结果与取消、真实顶部按钮点击、批量单历史/Redo 身份稳定、Space 锚点插入/无选择末尾/缺失类型保留批量追加、数字输入不抢空格、锚点删除、保存、折叠原生拖放预览/Undo 与作者快照不变。

## 门禁与边界

最终隔离输出解决方案构建通过（0 警告、0 错误）；Editor 141、Shared 191、Presentation 237、Architecture 45，共 614 项通过、0 失败。Presentation 的 6 个原生媒体环境用例为 NotExecuted，不计入通过数。whitespace 与 IDE0005 verify 均通过。证据目录：C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/group-ui-validation，含脚本、日志、TRX、源码 hash 与 group-toolbar.png；早期聚焦 log/trx 在相邻 p4b-validation。

拼音仅为已声明别名，不含任意中文转写；真实系统 IME 的多平台人工验收不由 headless 预编辑测试代替。P5 多选/剪贴板、变量面板及剩余历史迁移、P6 嵌套结构/状态查询与动画工具仍未完成。当前条目主体仍使用既有投影/字段提交路径，未擅自改写所有业务模型。

工作树中有另一任务的 GameTestCase/shader/ComplexPrimitiveOptions、entry-types/runtime 文档及 PlayerInputTests 改动，保留并排除提交。隔离构建目录只隔离输出，不伪称源码工作树与其他任务隔离。

最终执行 validate-final.ps1：顺序运行 dotnet build GalNet.slnx --no-restore --artifacts-path test/GalNet.Editor.Tests/bin/p4b-artifacts -m:1 -p:UseSharedCompilation=false、四个测试 DLL，以及 dotnet format whitespace/style --verify-no-changes（IDE0005，仅本阶段 C#）。source-sha256.json 记录最终受检 C#/AXAML/四语输入，提交前再次核对一致。group-toolbar.png 已检查，行折叠、顶部布局与列表实际尾部空间另由真实控件断言验证。
