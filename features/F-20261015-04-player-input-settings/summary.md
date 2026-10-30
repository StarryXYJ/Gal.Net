# 操作设置实现记录

2026-10-16。代码已实施，自动化验证已完成；实体手柄/触屏和人工视觉验收尚未执行，Phase 4–6 保留 in-progress。

## 实现结果

- Sample 设置分类为游戏、操作、画面、音频。操作分类借用 `CustomSetting<PlayerInputConfiguration>`、`FieldEditorLease`、`SettingsCoordinator`，以全宽自定义编辑器挂载三设备面板。三主题继续使用原设置页外壳。
- `PlayerInputSettings` 保存动作与绑定；采用冻结动作目录、复制配置和串行编辑门。值通过 `player.galnet.input.bindings` 的版本化 JSON 字符串持久化，读档不回滚。无效配置回退默认，恢复过程不写回文件。
- 键盘与鼠标共用 `InputBindingCapture`，覆盖修饰键、鼠标键/侧键/滚轮。考虑到键鼠统一、释放后提交和手柄输入需求，采用专用控件；未强行扩展 Ursa 的键盘值契约。
- 捕获有独立草稿，Esc/手柄取消保留旧绑定，控件 detach 解除监听；重复/保留键和无效输入被拒绝，冲突显示对应动作名称。提供清除整行与恢复本设备默认。
- 输入设备按实际按键/触摸记录；子页首次按设备选择，同一访问内手动选择保留。键鼠或手柄事件不会在捕获期间触发游戏动作。
- `PlayerInputRouter` 组合 `KeyboardMouseShortcutListener`、`GameActionDispatcher` 与手柄导航。非视觉键鼠监听随根控件挂到 TopLevel，解决初始窗口焦点和工具栏按钮焦点下快捷键失效；独立动作执行器复用 Quick/Auto/Skip、导航、截图等现有命令，提供暂时按住快速、显示/隐藏 UI 切换和全屏设置回调。其他页面/文本与选择等编辑控件内受屏蔽，工具栏保留 Enter/Space 原生激活和鼠标操作；选择和确认期间走手柄导航。
- `SampleGamepadInput` 在 Windows 使用 XInput 轮询，处理按钮/扳机阈值/左摇杆死区及失焦/断开取消。GameView 只消费 South/East/LB 等中立代码。A/B 确认布局可互换；方向、肩键分类、按钮、ComboBox、滑块、捕获和取消均有路由。
- `TouchInputRecognizer` 仲裁单击/双击、长按、双指点击/长按、四方向滑动，一次序列只分发一个动作。ComboBox 配置与其他设置共用保存队列。
- `IFirstEmptySlotSession` 是可选宿主能力。Sample 会话校验正在运行，flush 设置，保存层在同一写锁内查询最新普通槽位并保存；并发保存不抢同槽，不覆盖损坏槽，满槽返回 null 并打开存档页。Sample 槽位列表由原前 12 个扩展为真实普通槽位集合。
- `open.history` 动作已注册，通过 `PlayerInputSettings.OpenHistoryAsync` 保留接入口；无回调时该行禁用并说明页面尚未提供。未增加历史数据或页面。

## 验证

- Sample 项目构建成功。
- Presentation 完整回归 202 项通过，覆盖新动作配置、组合键、取消、三主题视觉树挂载/释放、重复键、页面隔离、手柄布局/捕获及触屏双击/双指互斥；最后的小范围生命周期/显示修正另按最终命令复验。
- Sample 输入、显示设置、存档、组合及清理生命周期集成测试 11 项通过。
- 最终小范围复验：PlayerInputTests 与 SettingSchemaFormTests 共 14 项通过；确认最后的冲突文案、配置校验和编辑器释放修正未破坏输入/设置队列。
- 追加游戏页焦点修复：先运行窗口/工具栏焦点复现用例，旧实现 2 项失败。修正后输入专项 25 项通过；再补充按住快速 Control+K 组合键用例，最终 Presentation 全量 217 项通过（原输入测试 11 项，现 26 项）。覆盖三主题真实 GamePage 的推进、截图、隐藏/恢复、鼠标中键、修改绑定立即生效，以及编辑器/宿主隔离、工具栏原生交互、捕获优先、卸载重挂/释放和按住状态取消。
- 新增模块边界已同步到 [control.md](../../docs/spec/presentation/game-view.md)，设计偏差见 [design.md 第 9 节](design.md#9-2026-10-16-游戏快捷键路由修正)。焦点路由经验见 [feature 内暂存 lesson](lesson-shortcut-focus-routing.md)。本轮是无界面测试宿主中的真实路由事件验证，未冒充桌面窗口/实体设备人工验收。
- 首空槽测试实际建立临时 profile，保留槽 0 内容和槽 1 损坏文件，并发保存分别写入槽 2/3，填满其余槽后返回 null，原内容不变。
- 输入持久化测试关闭/重开变量存储，恢复键鼠与 A/B 布局且不额外写文件；清空玩家变量后恢复默认。
- 本 feature 未修改 Editor 用户工作树，未自动提交混合改动。

## 追加：设置项视觉提示与默认 Esc 返回

- 上一轮实现单独提交为 `b62bff6`，未纳入用户的 Editor 和 agent 文档改动。
- 上一轮视觉实现（现已撤回）：`SettingsCategories` 普通设置行和 `InputBindingsPanel` 动作行使用 `player-setting-row`。用户已澄清框应位于键值绑定入口本身，见下节修正。
- `BackNavigablePageViewModel` 提供共享 `BackCommand`、`Navigation` 和 `OnBack()`。设置、存读档、全部鉴赏页及详情、关于已迁移，去掉各 VM 重复的返回命令；视频详情通过 `OnBack()` 停止播放。
- GameShell 一次组合 `PageBackInputListener`，不依赖 `PlayerInputSettings` 是否存在。未被内部控件消费的纯 Esc 在 bubble 阶段执行页面返回命令，捕获/弹出选择/局部取消优先，长按不连续退页；标题/加载/剧情不默认返回，宿主壳外输入不会误触发。手柄返回同样调用当前页命令。
- 最终 Presentation 回归 225 项通过。新增 5 项 Esc 用例、3 项真实三主题 GameShell 设置/存档/鉴赏返回用例；原设置工厂测试追加边框和焦点画刷断言。实际渲染截图位于 `artifacts/player-settings/`（构建产物，不提交），已检查三套主题的设置页面和行内编辑焦点。
- Sample 构建成功；Sample 输入/显示设置、保存、组合/清理，以及 Gallery 和截图受影响集成测试共 26 项通过。测试项目顺序构建，未改动 Editor 的业务或测试文件。
- 已同步 [control.md](../../docs/spec/presentation/game-view.md)，新页面继承返回基类即可，无需逐页监听按键；特殊清理重写 `OnBack()` 并调用基类。

## 修正：键值入口描边，不是逐行外框

- 撤掉普通设置和快捷键动作行的全部 `player-setting-row` 外框；不影响共享 Esc 返回。
- `InputBindingCapture` 显式复用 Button 样式键与 `player-input-binding` 类，为 Control 等键值自身提供输入框式描边、主题底色/圆角和悬停文字/描边反馈。模板内实际显示的文字同样变色，不只是根控件属性发生变化。
- 原设置模板无条件创建非空 Tip 内容对象，ReasonKey 为空、内部文字隐藏时仍会弹空容器。删除重复 Tooltip，保留原内联可用性原因和错误；测试验证原因出现时仍可见。
- 先补三主题回归，旧实现均因绑定按钮 BorderThickness 为 0 失败；修正后独立三主题用例通过，最终 Presentation 全量 228 项通过。已检查三套主题实际绑定面板截图（`artifacts/player-settings/*-InputBindingFields.png`，测试宿主未接本地化，标签显示 key；不提交产物）。测试同时覆盖实际模板描边、实际文字悬停、无行外框/无空 Tip、内联原因、原捕获与 Esc 返回。
- [control.md](../../docs/spec/presentation/game-view.md) 与设计第 11 节已修正；用户澄清与视觉回归经验见 [lesson-binding-field-affordance.md](lesson-binding-field-affordance.md)。
- 按用户要求整理：将 `InputBindingCapture` 从面板文件拆到独立文件，集中捕获状态字段和构造初始化，面板只负责布局/设置投影；不改变公开类型或捕获语义。整理后输入、返回与主题相关 81 项回归通过。

## 整理提交与 Sample 字体统一

- 绑定控件整理和键值框/空 Tooltip 修正提交为 `4736341`。捕获控件、面板和动作分发各自独立，未纳入其他正在进行的宿主/媒体迁移。
- 新增 `GameTypography` 统一字体与回退配置：Sample 启动字体管理器和应用 Window、三套玩家主题共用中文优先无衬线字体；删除各布局重复字体资源及 Paper 独立 Georgia。日志的显式等宽设置保留，Editor 自身字体不改。
- 当前 Windows 字体专项两项通过，三主题实际字体均解析为 `Microsoft YaHei UI`；验证中文/标点的普通与粗体非零字形映射，以及共享资源/配置实例隔离。三主题混排渲染图输出到 `artifacts/player-settings/*-ChineseTypography.png`。
- 最终 Presentation 全量 230 项通过，三套中英混排渲染图已检查；Sample 构建成功，0 警告、0 错误。字体范围及宿主配置入口已同步到 Themes README 和 control.md。
- 配置使用系统安装字体，没有下载或随包分发字体；非 Windows 中文字体的安装与实际效果仍需真机验证。
- 当前工作树的音频迁移曾使 Presentation 编译因类型/接口不匹配失败；该路径的项目引用补齐后，按当前依赖图正常 restore 并重跑字体测试通过。本任务未修复、回退或提交那部分代码。

## 限制与后续验收

- 实体手柄尚未连接实测；当前原生适配为 Windows XInput，非 XInput 控制器和非 Windows 手柄后端尚未实现。按钮标签目前采用中立代码和 Xbox A/B 布局文案，未实现各厂商动态图标。
- 多指手势使用实际路由事件验证，尚未在真实触屏设备验证系统兼容鼠标事件与输入手感。长按在释放时确定，双击判定窗口为 280ms，长按阈值为 550ms，滑动阈值为 60 个设计单位。
- 触屏不支持按住快速，相关行禁用；手势属于离散动作。
- 两个绑定位置均有录入/选择，清除目前作用整行；逐行恢复默认与主/备用列标题等视觉细化可后续补充。
- 正式入口与宿主约定见 [control.md](../../docs/spec/presentation/game-view.md)。

## Agent 验证环境记录

受限环境内两次测试进程只输出“匹配测试文件”后无后续结果，终止后用相同已构建程序集在沙箱外重跑，NUnit 正常发现并通过。此证据只确认环境差异，不断言具体底层根因。后续同类无输出先用最小测试和沙箱外对照，不能改测试或产品代码掩盖环境问题。`.agents` 在本会话只读，本条按 capture-lesson 流程暂存于 feature。

追加回归曾遇 CS2012：Themes 的 obj PDB 被另一 .NET Host 占用。未结束未知进程、未更改编译/调试配置；占用进程退出后同一测试命令重跑通过。首次新增鼠标测试因固定坐标未命中主题布局后的按钮而失败，改用布局后控件中心坐标后原生点击测试通过。
