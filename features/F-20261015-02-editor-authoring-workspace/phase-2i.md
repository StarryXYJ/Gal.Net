# P2i：编辑器 i18n 基础补齐

日期：2026-10-16。状态：verified。

## 实现

复用现有 `IEditorLocalizationService`、DynamicLocalization Core 与嵌入 JSON，不引入第二套翻译框架。zh-CN/en-US/ja-JP/ko-KR 各 444 个 key；默认中文，同语言 locale 归一到支持值，缺译回退中文，未知 key 显示 `[key]`。

| 范围 | 本阶段结果 |
| --- | --- |
| 启动、新建、项目/编辑器设置、Log 基础筛选 | 文案、选择器提示、快捷键错误与数值控件无障碍名称接入四语 |
| 基础 Graph、Inspector、菜单与历史 | 节点种类和内置操作描述动态翻译，作者名称/字段 ID 保持原值 |
| 关闭、保存恢复、布局与错误 | 已打开的对话框、已有状态/诊断随语言刷新，关闭释放绑定 |
| 偏好与数据边界 | UiLocale 写入 Editor 设置，重载恢复；不修改 TargetLocale、作者快照、dirty 和历史位置 |
| P3–P6 | 分支批量交互、文件对话框、Group/schema 新 UI 随各阶段接入 |

`LocalizeExtension` 改为显式监听 `CurrentCulture`，动态 key 同时监听路径；嵌套属性使用 `Path`。原索引器绑定在实际 Avalonia 控件上不会随 Item[] 通知重新求值，已通过失败用例定位并记录 lesson。

`EditorText` 保留 key/参数，在显示时翻译并支持嵌套文案；历史使用显式内置描述适配，不改历史执行或原始 Description。Shared 的已识别错误使用中立 `EditorOperationException` code/参数，原始诊断保留。未知异常显示本地化提示及原始细节；未知扩展历史、技术 ID、路径、原始日志和作者内容原样显示。

语言刷新保留项目设置草稿和快捷键草稿；防止重建显示名称触发主题/偏好写入。窗口和 ViewModel 释放语言订阅。`EditorSettingsService` 增加可选设置路径，测试真实写入/重载而不污染用户偏好；原有默认路径与保存行为不变。

## 验证证据

验证使用提交 `bb94248` 加本阶段 35 个源文件的隔离快照 `Temp/p2i-validation`。工作区另有 F-20261015-04 玩家输入功能同时修改 Presentation/Sample；一次混合树构建遇到该任务尚未写完的 TouchInputRecognizer，因此未把中间状态计作本阶段回归。隔离快照不包含该任务改动；本阶段源文件 SHA256 与工作区逐一核对，记录在 `artifacts/F-20261015-02/p2i-source-sha256.json`。这不是对并行任务最终合并状态的集成验收。

已执行非空范围的 whitespace 与 IDE0005 修复/verify 门禁，覆盖 5 个项目的 25 个 C# 文件；locked restore 成功。最终 `dotnet build GalNet.slnx --no-restore -m:1 -p:UseSharedCompilation=false` 成功，0 错误（最终增量构建 9 个警告）；此前完整构建同样成功。最后修正一处可空路径标注后重新构建、测试并复验该文件的格式门禁。

| 测试组 | 通过 | 结果文件 |
| --- | --- | --- |
| Editor | 90 | p2i-editor-final.trx |
| Editor.Shared | 188 | p2i-shared-final.trx |
| Architecture | 38 | p2i-architecture-final.trx |
| 内容集成 | 12 | p2i-integration-final.trx |
| 合计 | 328 | 0 失败、0 跳过 |

各组使用 `dotnet test <project> --no-build --no-restore`；集成组过滤 `ProjectContentBuilderTests|GameInstallationTests|GameTestCaseTests`。最终四语截图检查包含日语关闭对话框和韩语工作区，关键按钮可见；Graph 作者名称按既有节点宽度截断，与翻译无关。

测试覆盖：四语 key/非空文案/格式语法/占位符一致性；真实偏好文件重载、locale 与缺 key 回退、名称含花括号；挂载主题的真实窗口通过语言 ComboBox 切换中→英→日→韩→中，检查 Graph、嵌套 Inspector、菜单历史、既有错误、关闭按钮和未提交草稿；对照作者快照、dirty、历史和游戏语言不变；四种语言下通过 NumericUpDown 提交不同增益并保存，检查 JSON 数值；新建错误动态刷新，释放后无通知，反复开关关闭对话框后不再刷新旧窗口。

产物位于 `artifacts/F-20261015-02/`：最终 TRX、构建日志、四语 workspace/close PNG。图像仅用于验证，不纳入仓库。

## 边界

游戏文本翻译、新增语言、游戏预览不在本阶段。未新增语言/翻译编辑页面；技术细节不承诺翻译。Headless 使用真实主题/视觉树和控件事件，没有替代原生桌面 IME 人工验收。尚未实施的 Group/schema 等界面不能据此宣称全面四语完成。

下一阶段为 P3 Graph 完整交互，继续沿用项目单栈与上述文案边界；整个 feature 保持 in-progress。
