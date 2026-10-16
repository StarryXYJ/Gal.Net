# Sample 操作设置与多输入设备快捷键实施计划

日期：2026-10-16。状态：代码已实施，自动化验证通过；实体手柄/触屏与人工视觉验收待做。设计依据：[design.md](design.md)，证据与实际偏差见 [summary.md](summary.md)。

## 实施原则

- “操作”是现有设置页的一级分类，不新建设置导航页或三套普通主题页面。
- 保持 `GameView`、主题、Sample、Core/Runtime 的边界；输入映射和设备类型不进入 Core/Runtime 或 `GameSnapshot`。
- 先形成可工作的键鼠垂直切片，再扩展手柄和触屏；不先铺开三套空 UI 再等待动作语义。
- 每个 Phase 必须能独立验证；未通过退出条件不得把后续 Phase 标为完成。
- 现有工作树中的 Editor 改动不属于本 feature，不在 Phase 中整理、回退或格式化。

## Phase 1：操作分类与设置页管线

状态：verified（自动化）；三套主题真实视觉树挂载/释放验证通过。

### 目标

把新的 `operation` 分类接入现有设置数据流，建立输入设置模型和专用 `InputBindingsPanel` 的最小承载，不改变现有游戏、画面、音频分类。

### 前置条件

- `feature.md`、`design.md` 已确认。
- 保留当前 `SettingsPageViewModel`、`SettingsCategories` 和三套主题设置页作为页面入口。
- 确认本阶段只添加模型、页面承载和测试夹具，不要求真实输入动作已经可用。

### 涉及模块

- `src/Presentation/GalNet.Avalonia.GameView/Services`
- `src/Presentation/GalNet.Avalonia.GameView/Settings`
- `src/Presentation/GalNet.Avalonia.GameView/ViewModels/SettingsPageViewModel.cs`
- `src/Presentation/GalNet.Avalonia.Themes/Common/SettingsCategories.axaml(.cs)`
- `src/Presentation/GalNet.Avalonia.Themes/Layouts/*/*SettingsPage.axaml`
- `src/Samples/GalNet.Sample.Avalonia/Services/SamplePlayerSettingsService.cs`
- Sample 和 Presentation 的本地化资源、设置页测试

### 任务

1. 定义稳定的 `GameActionId`、`InputDeviceKind`、规范化 `InputGesture`、动作定义和 profile 读取/变更契约；不引用 Avalonia `Key` 或具体设备 SDK。
2. 在 Sample 设置服务的 `Categories` 中新增 `operation`，标签使用 `Settings.Operation`，顺序明确为游戏、操作、画面、音频或经产品确认的等价顺序。
3. 在现有 `SettingsCategories` 内容模板中接入操作分类专用面板；保持现有设置行的名称、说明、滚动、可用性、错误和主题画刷约定。
4. 实现三个固定的设备 Tab、不可用状态、动作列表、主/备用绑定的只读投影和本地化键。
5. 用内存 profile/测试服务验证面板可切换、数据变更通知和控件释放；不要在本阶段接入真实按键捕获。

### 测试与验证

- Presentation：`operation` 分类可见，原有三分类仍能切换，三个设备 Tab 可见，当前 Tab 切换不创建导航历史。
- Presentation/UI：三主题下操作分类的外层布局、滚动、焦点、不可用说明和错误区域与现有设置页一致。
- Settings model：动作 ID 唯一、设备 profile 唯一、绑定列表可观察，释放后不残留事件订阅。
- 只运行与本 Phase 相关的测试；不以全解决方案构建替代页面验收。

### 文档

- 保持 `docs/spec/` 不变，因目标尚未实现。
- 在本 feature 记录实际选择的分类顺序、面板接入方式和未支持设备表现。

### 风险

- 现有 `SettingCategory` 是普通 `SettingItem` 列表，专用面板接入方式可能需要在 `SettingsCategories` 增加明确的内容分支；不能通过把 Avalonia 控件塞入普通设置模型来绕过生命周期。
- 三套主题的局部样式可能覆盖绑定按钮的焦点/禁用状态，需要检查实际视觉树而不仅是属性值。

### 退出条件

- Sample 实际返回 `operation` 分类，SettingsPageViewModel 能显示它。
- 三套主题均能打开操作分类，看到三个设备 Tab 和可读的不可用状态。
- 现有游戏、画面、音频设置及设置页返回/滚动行为没有回归。
- 相关自动化测试通过，并记录面板控件/事件的释放验证。

## Phase 2：键鼠绑定编辑与玩家偏好持久化

状态：verified（自动化）；真实键鼠录入、Esc 取消、冲突、并发编辑、重启恢复和无重复写入通过。

### 目标

完成键鼠 Tab 的端到端功能：捕获、规范化、冲突检测、清空、恢复默认、保存和重启恢复。

### 依赖

- Phase 1 通过。
- 明确主/备用绑定上限、默认键位和保留键规则；未确认的默认值以设计建议实现为可替换配置，不写死在 UI。

### 涉及模块

- GameView 输入设置模型、捕获控件/适配器和 `SettingsCoordinator` 接入
- `SamplePlayerSettingsService`、`PlayerVariableService` 使用边界
- `src/Samples/GalNet.Sample.Avalonia/Localization/*.json`
- Presentation、IntegrationTests 和必要的 UI 测试

### 任务

1. 为键、鼠标按钮、修饰键组合建立规范化手势和显示名称；区分单次按下、允许重复和保留输入。
2. 评估现有 Ursa 2.1.0 KeyGestureInput 能否通过同一个编辑器完成键盘、鼠标键/侧键/滚轮及修饰键组合捕获；若不满足则参考其实现统一 InputBindingCapture，不拆出键盘和鼠标入口。显式激活并等待激活键释放后录入下一次输入/组合；Escape 取消保留原值，清除使用独立按钮，控件卸载取消草稿并解除订阅。
3. 在同一设备、同一上下文内做重复冲突检测；错误指出冲突动作，失败不修改内存 profile 或持久化数据。
4. 采用新的 `player.galnet.input.bindings` 字符串变量保存版本化 JSON；恢复阶段只更新内存，缺失/损坏/未知版本安全回退默认。
5. 将输入设置变更接入 Sample 现有 `SettingsCoordinator`，沿用已有保存失败可重试、会话结束 flush、清空玩家数据恢复默认的语义。
6. 验证键盘焦点在设置文本框/捕获框时不会误触发游戏快捷键。

### 测试与验证

- 绑定捕获、规范化显示、清空、恢复默认、主/备用绑定和冲突拒绝。
- 取消捕获、控件 detach、切换 Tab/离开设置后无晚到事件写入。
- 新变量首次加载、合法 JSON 恢复、损坏 JSON、未知版本、未知动作和未知设备项。
- 保存失败后重试只保存未提交配置，不重复应用已经生效的值。
- 清空玩家数据后绑定恢复默认；重启/重新 Attach 后值一致。

### 文档

- 在 feature 中记录最终 JSON version、默认键位和保留键规则。
- 若发现输入捕获/主题模板的通用边界，可追加 feature agent lesson；不提前写入正式 spec。

### 风险

- Avalonia 键盘事件的修饰键、自动重复和焦点路由可能与游戏页现有 tunnel handler 交错。
- JSON 是标量玩家变量中的字符串，必须避免文化相关格式和隐式键名变化。

### 退出条件

- 键鼠 Tab 可真实修改并恢复绑定；设置重启后保持。
- 冲突、取消、清空和保存失败都有可见且本地化反馈。
- 键盘输入在设置编辑和游戏页之间没有串线；相关测试通过。

## Phase 3：统一游戏动作路由与第一个空槽存档

状态：verified（自动化）；重复事件/页面隔离/历史回调、首空槽并发及满槽不覆盖通过。2026-10-16 追加窗口与工具栏焦点缺陷修复，将键鼠监听和动作执行拆为独立模块；三主题真实 GamePage、编辑器隔离、捕获优先、按住组合键及卸载重挂均有回归，Presentation 最终 217 项通过。

### 目标

让键鼠绑定真正驱动游戏动作，并将工具栏、快捷键与页面动作收敛到同一语义；完成安全的“保存到第一个空普通槽”。

### 依赖

- Phase 2 的键鼠 profile 和捕获流程通过。
- 现有 Quick/Auto/Skip、Hide UI、导航、截图和 Sample session 生命周期保持基线。

### 涉及模块

- `src/Presentation/GalNet.Avalonia.GameView/Page/GamePage.cs`
- `src/Presentation/GalNet.Avalonia.GameView/Page/GameShell.axaml.cs`
- `src/Presentation/GalNet.Avalonia.GameView/ViewModels/GamePageViewModel*.cs`
- `src/Presentation/GalNet.Avalonia.GameView/Services/IGameSessionService.cs`
- `src/Samples/GalNet.Sample.Avalonia/Services/SampleGameSessionService.cs`
- Sample 保存/通知和工具栏相关主题代码

### 任务

1. 在 GamePage/GameShell 组合输入路由器，统一处理页面上下文、焦点、自动重复、事件消费和隐藏 UI 状态。
2. 将 `GameActionId` 分发到已有 ViewModel/导航行为；不复制 Quick/Auto/Skip 的运行时逻辑。
3. 保持左键剧情推进的既有 tunnel 顺序，避免新路由与 PointerPressed 产生双推进；模式按钮仍按点击前状态正确切换。
4. 为会话增加概念上的 `SaveFirstEmptyAsync` 入口：在保存门内重新读取/确认槽位，选择最小空普通槽，保存失败不覆盖已有槽。
5. 没有空槽时打开现有存档页；重复保存请求串行化或明确拒绝；结果写入玩家可见状态/日志，不让设置服务直接操作文件。
6. 将工具栏按钮与快捷键共用动作方法/dispatcher；对需要确认的 Skip、危险导航保留原有确认语义。

### 测试与验证

- 键盘默认绑定和自定义绑定能驱动 Quick/Auto/Skip/Hide UI/Settings/Load/Screenshot。
- 快速和自动互斥，Skip 仍需确认并停在既有边界；按键重复不会重复导航或保存。
- 隐藏 UI 后仍能接收允许的输入，点击/键盘恢复语义不回归。
- 保存选择槽位：第一个空槽、前面槽已占用、所有槽占用、损坏槽、保存失败、并发重复触发。
- 保存快捷键不调用 `FileSaveService.QuickSaveAsync`，不覆盖已有普通槽；保存后槽位列表刷新。
- GamePage、设置页、存档页和读档页切换时旧输入路由被解除，旧会话回调不再执行。

### 文档

- 更新 feature 设计中的动作实际映射与 SaveFirstEmpty 结果语义。
- 若 `IGameSessionService` 公共接口实际变化，记录受影响宿主和测试，不提前修改 runtime spec。

### 风险

- GamePage 当前已有 `KeyDown` 清理播放指针；新增路由必须保留该时序，避免模式按钮再次出现已知的点击/状态竞态。
- 保存动作跨 GameView、Sample session、变量/进度 flush 和文件保存，需确保生命周期取消不会留下半完成 UI 状态。

### 退出条件

- 至少一个完整键鼠动作链路通过：输入 → 动作解析 → 页面/宿主操作 → 可观察结果。
- SaveFirstEmpty 的槽位选择和无空槽回退经过集成测试。
- 工具栏与快捷键的同一动作没有行为分叉；旧播放/导航测试不回归。

## Phase 4：手柄设备适配

状态：in-progress；Windows XInput 代码和注入按钮事件验证完成，实体手柄验证待做。

### 目标

在已验证的宿主能力基础上接入手柄按钮输入；如果当前平台没有可靠后端，完成可发现、可解释、不会误报支持的禁用路径。

### 依赖

- Phase 3 统一动作路由通过。
- 明确 Avalonia/平台实际提供的手柄事件源及生命周期；若没有，先选择受支持的宿主适配边界，不把 SDK 类型带入 GameView 契约。

### 涉及模块

- GameView 规范化输入/能力契约和 InputRouter
- Sample 手柄事件适配与能力查询
- 操作页手柄 Tab、默认绑定和本地化
- Presentation/IntegrationTests

### 任务

1. 将数字按钮、方向按钮和必要的 Start/Back 等离散输入转换为 `InputGesture`。
2. 处理设备连接/断开和能力变化；断开时不删除已保存绑定，页面显示不可用原因。
3. 复用 Phase 3 的 `GameActionId`，不为手柄建立第二套游戏命令。
4. 在 Tab 中捕获、显示、冲突检查和恢复默认手柄绑定；连续轴保持未实现并明确说明。
5. 支持按钮组合，等待激活键释放后录入，取消键按当前布局退出并保留原绑定；验证组合优先级、单键前缀竞争与导航暂停。
6. 增加 A 确认/B 取消及互换 Choice，保存为玩家偏好；接通方向焦点、列表、滑块、一级分类切换、二级 Tab 和选项确认，验证同次输入不会同时确认与推进。
7. 按最近有意输入设备初始化操作子页；忽略摇杆漂移、鼠标移动，进入后和捕获期间不自动切页。

### 测试与验证

- 有后端时真实按钮到动作的映射、按键重复和连接变化。
- 无后端时 Tab 可见、编辑禁用、原因可读，且键鼠动作不受影响。
- 手柄 profile 的保存/恢复与未知按钮码处理。

### 文档与退出条件

- 记录实际支持的平台和按钮规范化名称；更新 feature 限制。
- 手柄后端存在时至少一个动作端到端通过；不存在时禁用路径和自动化测试通过。

## Phase 5：触屏手势适配

状态：in-progress；触屏识别与 ComboBox 已实现，路由事件验证通过，实体多点触屏验证待做。

### 目标

接入触屏全局手势并完成触屏 Tab；不能实现的手势必须显示为不可用，不以鼠标模拟结果冒充触屏支持。

### 依赖

- Phase 3 统一动作路由通过。
- 明确目标平台触屏事件和手势识别能力；优先支持有限、可验证的全局手势。

### 涉及模块

- GameView 手势规范化、捕获和 InputRouter
- Sample 触屏适配器与能力查询
- 操作页触屏 Tab、手势文案和默认绑定
- Presentation/IntegrationTests

### 任务

1. 用 ComboBox 提供未绑定、单击、双击、长按、双指点击、双指长按、左右/上下滑动，候选按宿主能力提供。通过共享手势识别器验证单击/双击及单指/双指竞争；一次手势只分发一个动作。
2. 处理手势与既有左键/触摸推进的去重，避免一次触摸同时触发推进和自定义动作。
3. 对保存等高风险操作默认不绑定容易误触的手势；用户显式绑定后仍走统一保存安全门。
4. 触屏不可用时保持 Tab 可发现并展示原因。

### 测试与验证

- 支持手势的动作映射、取消/完成、重复触摸和手势冲突。
- 触屏手势不绕过 Skip 确认、不造成双推进、不在设置捕获时泄漏到游戏页。
- 触屏 profile 的保存/恢复、能力变化和无后端路径。

### 文档与退出条件

- 记录实际支持手势和平台范围，更新默认绑定说明。
- 至少一个触屏动作完成端到端验证，或明确验证无后端时的安全降级路径。

## Phase 6：跨主题、生命周期与收尾验证

状态：in-progress；Presentation 回归和 Sample 集成已验证，正式文档及总结已更新；硬件与人工视觉验收待做。

### 目标

完成三套主题、语言、生命周期、清空/重启和完整回归，形成可审核的 feature 证据；本 Phase 仍不扩大到 Editor。

### 依赖

- Phase 1–5 按实际能力完成，或对未支持设备完成安全降级。

### 涉及模块

- 三套主题和 Common 设置控件
- Sample 本地化、设置、会话和调试清空流程
- Presentation/Integration/UI tests
- feature 文档和正式 spec 同步候选

### 任务

1. 在 Classic、Minimal、Paper 及现有配色下检查操作分类、二级 Tab、捕获控件、焦点和错误布局。
2. 验证语言切换后动作名称、设备名称、冲突和不可用原因刷新，不改变绑定 ID/值。
3. 验证设置页关闭、返回游戏、读档、停止会话、重建 Sample scope 时没有晚到输入事件或保存任务污染新会话。
4. 验证清空玩家状态、重启、设置保存失败重试、设备能力变化和旧/损坏配置回退。
5. 汇总测试证据、实际偏差和未支持平台；实现完成后再同步 `docs/spec/control.md` 或相关正式文档。

### 测试与验证

- 主题×配色的视觉树/交互测试，不只断言外层属性。
- 设置模型、输入路由、Sample 集成和保存服务的分层测试。
- 代表性 UI smoke：进入设置 → 操作 → 捕获键 → 保存 → 返回游戏 → 触发动作 → 重启恢复。
- `git diff --check`、目标项目构建和受影响测试；区分环境/SDK限制与业务失败。

### 风险

- 主题模板、输入事件源和 Sample 会话生命周期的组合问题可能只在真实 UI pump 下出现。
- 跨语言绑定刷新和异步保存关闭需要实际等待验证，不能只靠同步单元测试。

### 退出条件

- feature 验收标准全部有测试或明确的能力降级证据。
- 三套主题和 Sample 主流程可用；不存在已知的输入泄漏、重复保存、覆盖普通槽或跨会话回调问题。
- 更新 `feature.md` 状态、生成 `summary.md`/review 所需的验证记录，并决定需要同步的正式文档。

## 追加切片：设置项视觉提示与页面 Esc 返回

状态：verified（自动化与离屏截图）；初轮 Presentation 225 项、Sample/Gallery/截图集成 26 项通过；用户纠正视觉目标后，本轮 Presentation 完整回归 228 项通过。

- 视觉部分后经用户纠正：撤销 `player-setting-row` 逐行外框；改为 `InputBindingCapture` 键值编辑入口自身的描边与悬停前景反馈，取消重复空白 Tooltip。详见 design.md 第 11 节；返回模块不受影响。
- 新增 `BackNavigablePageViewModel`，迁移设置/存读档/鉴赏与详情/关于页面；保留视频返回时停止播放的扩展点。
- GameShell 统一挂载 `PageBackInputListener`，且不依赖可配置快捷键是否启用；Esc、返回按钮和手柄页面返回走同一命令。
- Esc 专项覆盖局部消费和下拉框优先、录入取消后不退页、长按去重、壳外输入隔离、卸载/重挂/Dispose。
- 三主题真实 GameShell 分别验证设置、存档、鉴赏返回标题；标题页 Esc 不导航。
- 本切片未完成此前实体手柄/触屏、全配色人工验收，Phase 4–6 的限制保留。

## 变更记录

- 2026-10-15：依据用户确认的最终信息架构建立计划；将“操作”作为新增一级设置分类，保留现有“游戏”分类职责。
- 2026-10-16：按用户补充加入最近设备默认子页、手柄确认布局、下一次输入及组合捕获、取消保留旧值、Ursa 控件评估和触屏 ComboBox；清除改为独立按钮，保留 Backspace/Delete 可绑定能力。
- 2026-10-16：按游戏页快捷键反馈修正 Phase 3 的焦点监听范围与按钮过滤，抽取 KeyboardMouseShortcutListener / GameActionDispatcher；详细决策与回归证据见 design.md 第 9 节、summary.md。Phase 4–6 的实体设备/人工验收状态不变。
- 2026-10-16：用户追加设置项描边与默认 Esc 返回；按 design.md 第 10 节增加共享返回基类、壳级模块及设置行样式，先提交此前实现为 b62bff6。
