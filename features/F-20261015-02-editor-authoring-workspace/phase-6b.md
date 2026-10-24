# P6b：GalGroup 普通复杂值表单

日期：2026-10-18。状态：verified。用户授权普通生成 UI 开始实施，复杂专属控件与 AnimationPlan 一起留下一阶段。

范围：复用 P6a 三层 registry 和 P5d 作者 JSON/项目历史，提供生成 DTO 的嵌套对象、常用一维列表/数组、元素修改/添加/删除/上下移动；缺省、显式 null、显式默认值和未提交草稿恢复独立操作；不支持类型/控件提示有明确的多行 JSON 回退。JSON 使用 Ctrl+Enter 或失焦提交，Escape 恢复草稿。未知对象成员通过 JSON 局部合并保留，不通过 DTO round-trip 重写作者数据。

对象在展开时创建子表单，折叠释放控件/订阅；最多八层，列表最多创建 200 个元素控件，超限转 JSON。复杂转换器、字典和没有生成 schema 的对象使用 JSON。嵌套 DTO 本身可以整 DTO 覆盖，内部字段继续成员/默认类型注册。静态字段描述不能直接作为嵌套 JSON 的 wire 名：根据当前 JsonTypeInfo 找到实际序列化属性，保留 JsonPropertyName/命名策略和字段转换器。

列表结构变化产生新版本并取消旧索引提交，防止延迟写入落到重排后的其他元素。普通元素值修改保留当前控件。只打开/展开不构造 DTO、不写作者默认值；真正提交仍执行值及宿主 partial 校验。宿主负责历史与保存，共享控件不引用 Editor 服务。

状态查询/Locator、未知 Entry 的整参数 JSON 修改器和动画/曲线/timeline 专属控件未包含在本切片；P6 不整体标记完成。P7 的全局草稿关闭协调也不在此处宣称完成。

验证覆盖：新增共享真实控件测试覆盖 wire 名/未知字段、三层覆盖、列表操作与延迟取消、presence/null/default、JSON 键盘和递归上限；新增实际 Group 页测试验证项目历史、四语和文件重开。首轮回归发现 TextBox 默认处理会先消费 Ctrl+Enter，改为 tunnel 处理；原“不支持工厂删除字段”的旧断言改验有诊断的 JSON 回退，任意工厂异常清理测试保留。


最终隔离解决方案构建成功（25 条警告、0 错误）。Editor 156、Shared 214、Presentation 243、Core 111、Architecture 45、Integration 110，共 879 项通过、0 失败。Presentation 的 6 个原生媒体环境用例为 NotExecuted，未计入通过数；原生媒体和手工完整创作流程未在本切片宣称完成。whitespace/IDE0005 verify 与源码 SHA-256 门禁通过。

实际 Semi 页面截图及 Group 测试覆盖主题展开入口、嵌套 wire 名、未知成员、列表重排旧绑定失效、单项目历史 Undo/Redo、显式 null 保存后 Undo 恢复缺省、二次保存与项目重开、四语刷新。共享 Fluent 页面另覆盖列表增删/编辑、递归与长列表回退、取消延迟提交及释放订阅。新增主题样式键经验见 L-20261018-03；原先直接设 IsExpanded 的测试不足以证明模板入口可用，已补断言。

证据目录：C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/group-ui-validation，包含 validate-p6b.ps1、p6b-*.log/trx、p6b-source-sha256.json 与 p6b-structured-group.png。P6 整体仍 in-progress。

嵌套字段提交仍经过根字段的值契约与宿主 partial 校验；其他已有子值使整个对象无法类型化时，需要切换 JSON 模式一并修复，当前不绕过根值校验。
