# Sample 操作设置与多输入设备快捷键设计

日期：2026-10-16。状态：已实施；以下保留目标设计，实际接入方式及验证边界见 [summary.md](summary.md)。操作是设置页新增的一级分类；快捷键设备 Tab 是该分类内部的二级局部选择，不是新的导航页面。

## 1. 当前事实与目标差异

| 当前代码事实 | 目标状态 |
| --- | --- |
| `SettingsCategories` 只有一个分类 TabControl，`game` 分类直接渲染普通设置项 | 同一分类 TabControl 新增 `operation` 分类，`game` 分类保持原有内容 |
| Sample 的 `game` 分类已有语言、文本速度、快速/自动参数 | 这些参数继续留在“游戏”，快捷键集中在新的“操作”分类 |
| 工具栏按钮直接调用 `GamePageViewModel` 命令 | 工具栏、键鼠、手柄和触屏共享稳定的 `GameActionId` 动作入口 |
| `GamePage` 只对左键剧情点击和键盘事件做少量处理 | GamePage/GameShell 组合一个有上下文和生命周期的输入路由器；设置页捕获输入时优先消费原始事件 |
| `IGameSessionService.SaveAsync` 需要调用者先给出槽位 | 增加宿主级“保存到第一个空槽”语义，在执行时重新确认槽位，避免竞态覆盖 |
| 玩家变量只保存标量值 | 快捷键映射编码为一个版本化 JSON 字符串玩家变量，仍不进入 `GameSnapshot` |

## 2. 设置页面结构与主题复用

### 2.1 一级与二级层级

设置页保持现有页面外壳和一级分类 TabControl，只新增一个 `operation` 分类：

```text
设置
├─ 游戏
│  └─ 现有游戏参数：语言、文字速度、快速限制、快速/自动间隔
├─ 操作
│  └─ 快捷键绑定
│     ├─ 键鼠
│     ├─ 手柄
│     └─ 触屏
├─ 画面
└─ 音频
```

“操作”不是 `OperationSettingsPageViewModel` 导航页，而是现有设置页中 `operation` 分类的内容。它复用当前 `SettingsPageViewModel`、`SettingsCategories` 和三套主题的外层 `Classic/Minimal/PaperSettingsPage`，因此不新增普通页面映射，不改变设置页返回按钮、页头、滚动区域、卡片和“修改立即生效”提示。

`operation` 分类内部使用一个专用的 `InputBindingsPanel`。它的外观遵循现有设置行：左侧名称/说明，右侧编辑控件、可用性原因和错误文本；面板内部再使用局部 `TabControl`。每次从其他页面进入设置并首次打开操作分类时，按最近一次有意输入设备选择子页：手柄操作进入则选手柄，触摸进入则选触屏，键鼠进入则选键鼠；没有记录时回退键鼠。只连接手柄、鼠标轻微移动或摇杆漂移不算有意输入；手柄按钮、超过死区的有效方向输入、键鼠点击/按键、真实触摸才更新设备记录。

进入后用户可自由切换子页；同一次设置访问内不因其他设备事件自动跳页。输入捕获锁定当前设备子页，避免录入时切页。最近输入设备和子页选择是会话 UI 状态，不持久化到玩家变量；不支持的设备子页仍可显示能力说明。

三套主题只负责布局和视觉资源，不复制快捷键业务逻辑：

- Classic/Minimal/Paper 继续使用各自的设置页头、返回按钮、内容宽度和滚动容器。
- `InputBindingsPanel` 使用现有 `GalNet.Brush.*`、主题间距、焦点和禁用状态资源。
- 主题可以替换绑定行/捕获按钮模板，但动作 ID、冲突校验、持久化和设备能力由共享模型负责。
- 操作分类使用 `Settings.Operation`、`Settings.OperationDescription` 等本地化键；不把显示文本写入输入配置。

### 2.2 绑定编辑行

每个动作显示一行：动作名称、简短说明、主绑定、备用绑定、恢复默认。点击绑定框进入捕获状态：

- 键鼠和手柄都是显式点击/确认绑定框后开始捕获下一次输入；启动捕获的点击或确认键必须完全释放后才开始收集，不把激活编辑器的键录进去。
- 键鼠支持单键、鼠标按钮和修饰键组合，例如 Ctrl+S、Ctrl+Shift+S、Shift+鼠标侧键。初版不支持按先后顺序输入的多段快捷键或任意多个普通键同时按下。
- 手柄支持单按钮及同时按住的按钮组合，例如 LB+A、LB+RB；方向按钮可以作为数字输入，连续轴仍在范围之外。
- 捕获期间显示当前组合草稿，在触发键/按钮释放、组合确定后校验并一次提交；不能在首个按钮按下时立刻提交，导致肩键组合无法输入。组合按规范化顺序去重，保存与匹配不依赖按下先后。
- Escape 或当前手柄布局的取消键优先取消本次捕获并恢复原值；捕获不会先清空旧值。取消键在捕获期间保留，不允许录入为游戏绑定。改键冲突保留原值并提示。
- 清空使用独立的“清除”按钮/菜单操作，以允许 Backspace/Delete 自身成为键盘绑定；恢复默认也走统一校验/保存入口。
- 触屏用 ComboBox 选择：未绑定、单击、双击、长按、双指点击、双指长按、左滑、右滑、上滑、下滑。只提供宿主实际支持的选项，并显示受限原因。选择成功后按现有 Choice 设置流程生效与保存。
- 绑定冲突在提交前检查，冲突行显示动作名称和本地化错误，不能保存半成品。

设置模型不持有 Avalonia 控件。捕获控件位于 Presentation/Themes 边界，使用 `IPlayerInputSettingsService` 的类型化操作提交值；页面销毁时解除事件订阅和捕获状态。

控件复用调查：当前 `Directory.Packages.props` 引用 Irihi.Ursa 2.1.0，本地该版本 Ursa.dll 中已确认存在 `KeyGestureInput` 类型。其鼠标/手柄支持与实际按下/释放行为尚未验证，不能据类型名承诺全部功能。用户已确认键盘与鼠标必须在同一个“键鼠”子页和同一个绑定录入控件内处理，包括鼠标左/中/右键、侧键、滚轮上/下及 Ctrl/Shift/Alt 等修饰键与键鼠输入的组合。滚轮是离散脉冲，在录入框捕获期间消费滚轮并阻止页面滚动；普通设置页滚动不触发录入。

实现先验证 KeyGestureInput 能否完整承载该交互；若需要拆成键盘/鼠标两个编辑器，或无法可靠取消/收集组合，直接参考其做法实现统一的 `InputBindingCapture` 控件。允许复用内部逻辑，但产品只呈现一个“点击后输入”入口。手柄复用捕获会话/草稿/提交生命周期，输入适配器独立。玩家主题提供控件模板和画刷。参考 [Ursa 官方仓库](https://github.com/irihitech/Ursa.Avalonia)。

### 2.3 手柄确认布局与界面导航

手柄子页顶部提供 Choice 设置“确认/取消布局”：A 确认、B 取消；或 B 确认、A 取消。内部用 South/East 物理位置记录，并根据手柄类型显示 A/B、叉/圆等标签，避免不同厂商的字母位置混淆。默认南键确认、东键取消，允许玩家选择互换；这是玩家偏好，和游戏快捷键一起保存。

布局统一影响按钮激活、ComboBox 确认、剧情选项确认、返回与绑定捕获取消提示。它与剧情的 advance 动作有不同上下文：选择列表内确认选中项，剧情区域内才推进，不能一次输入同时执行两者。布局切换使用旧布局确认本次选择，成功后在所有按钮释放后启用新布局。

方向键/左摇杆移动焦点，摇杆有死区与长按重复间隔；LB/RB 切换一级设置分类，设备 Tab 获得焦点后左右切换、上下进入绑定行。按钮确认进入捕获，取消按捕获、弹出列表、当前页面的顺序处理。滑块左右调整；ComboBox 确认展开、上下选择、确认提交、取消保留原值。切页恢复该页焦点并滚动到可见位置。捕获期间暂停这些导航规则，取消输入拥有最高优先级。

### 2.4 设备不可用

三个二级 Tab 默认都是可发现的。每个设备 profile 提供 `IsSupported` 和 `ReasonKey`：

- 支持时显示绑定行并允许编辑。
- 不支持时保留 Tab，显示“当前平台未提供手柄/触屏输入”及原因，不显示失效的编辑控件。
- 设备能力变化时刷新 Tab 与绑定行，不丢弃已保存的其他设备绑定。

### 2.5 与现有设置数据流保持一致

“操作”分类应像游戏、画面、音频一样由 `IPlayerSettingsService.Categories` 统一提供，使用同一个设置页实例和同一个保存协调器；不额外创建一套设置窗口或独立保存入口。由于快捷键面板包含设备 Tab 和输入捕获，它可以作为一个由设置服务提供的专用设置内容项接入现有 `SettingsCategories`，但面板内部仍只消费 `IPlayerInputSettingsService`，不直接访问文件或存档。

启动及显示数据流：

```text
SampleSaveSession / PlayerVariableService
        ↓ AttachAsync
SamplePlayerSettingsService
  ├─ game category：游戏参数
  ├─ operation category：InputBindingsPanel 数据上下文
  ├─ graphics category：IPlayerDisplayService
  └─ audio category：GameSettings + AudioRuntime
        ↓
SettingsPageViewModel
        ↓
SettingsCategories（一级 Tab）
        ↓ 选择 operation
InputBindingsPanel（局部 Tab：键鼠/手柄/触屏）
```

编辑及保存数据流：

```text
绑定捕获
  → 规范化 InputGesture
  → IPlayerInputSettingsService 校验/冲突检测
  → 更新内存 profile 和 InputBindingsPanel
  → SettingsCoordinator 串行排队
  → player.galnet.input.bindings
  → PlayerVariableService.FlushPlayerVariablesAsync
```

这与现有语言、音量和画面设置的“读取实际值 → 应用 → 通知 UI → 通过协调器保存”顺序一致；恢复配置时只更新内存，不逐项写回。

## 3. 动作目录与默认语义

动作 ID 使用稳定、不随文案变化的字符串。初版目录建议为：

| 动作 ID | 设置名称 | 执行语义 |
| --- | --- | --- |
| `advance` | 下一步/确认 | 推进当前剧情或确认当前交互 |
| `hold.quick` | 按住快速 | 按住时启用 Quick，释放/失焦/切页时停止；新增生命周期适配，尚待用户确认纳入 |
| `toggle.quick` | 快速 | 调用现有 Quick 模式 Toggle；不代替跳过未读 |
| `toggle.auto` | 自动 | 调用现有 Auto 模式 Toggle |
| `skip.unread` | 跳过已读 | 调用现有带确认的 Skip；停止边界不变 |
| `toggle.ui` | 隐藏界面 | 在可见/隐藏状态之间切换 UI；恢复行为与手动点击统一 |
| `save.first-empty` | 快速存档 | 执行“第一个空普通槽保存” |
| `open.save` | 打开存档页 | 由玩家选择槽位，沿用现有工具栏存档入口 |
| `open.load` | 读取存档 | 打开现有读档页，不直接加载未经选择的槽位 |
| `open.settings` | 设置 | 打开设置页 |
| `screenshot` | 截图 | 调用现有截图请求 |
| `toggle.fullscreen` | 切换全屏 | 借用画面设置服务切换窗口/无边框全屏，按宿主能力提供 |

以上共 12 个建议游戏动作，其中现有执行行为可复用的项目与新增适配必须区分；按住快速和全屏快捷入口属于本轮动作清单建议，不表示用户已经授权扩展全部功能。`advance` 复用现有推进：文本未完成时按当前呈现语义补全文本，完成后进入下一句，不额外创建行为重复的“显示全文”动作。

界面导航是另一组上下文动作：确认、取消/返回、上/下/左/右、上一/下一 Tab、上一/下一页、向上/下滚动。它们服务设置页、存档页与剧情选项，不应作为 12 条游戏动作重复平铺。手柄 A/B 布局影响这里的确认/取消；同一按钮可在不相交的上下文中承担剧情推进或界面确认。

其他 Gal 常见动作作为后续候选，不因添加绑定而自动承诺实现：

| 候选动作 | 本项目接入前提 |
| --- | --- |
| 打开对白历史 / Backlog | 增加完整对白历史模型与页面；当前 NVL 展示历史不能充当全游戏历史 |
| 回退 / 向前恢复 | 增加剧情回滚状态与恢复语义；不能仅修改执行游标 |
| 重播当前语音 | 增加当前句语音身份、资源与重播生命周期 |
| 快速读档 | 定义读取哪个槽、确认和加载流程；当前用户只要求快速保存到首个空槽 |
| 跳过当前视频 | 尊重视频自身可跳过/阻塞定义，并接通独立动作 |
| 返回标题 / 退出游戏 | 宿主操作和确认；默认不绑定 |
| 全局静音 | 可复用已有音频设置，作为可选便利动作 |

参考依据：Ren'Py 把按住快速、切换快速、隐藏窗口、截图、全屏与菜单分开；Naninovel 同时列出界面导航、剧情推进、按住/切换快速、自动、UI、历史与回退，并区分设备输入。这里只借鉴动作分类，默认键位与支持范围按 GalNet 现有能力制定。[Ren'Py keymap](https://www.renpy.org/doc/html/keymap.html)、[Naninovel Input Processing](https://naninovel.com/guide/input-processing)

### 3.1 建议默认绑定

默认值只用于新玩家或恢复默认，不覆盖用户已经保存的绑定。键鼠默认值可以先采用；这些是建议值，其他动作可先保持未绑定：

| 动作 | 建议默认 |
| --- | --- |
| 下一步/确认 | `Space`、`Enter` |
| 快速 | `Tab` |
| 自动 | `A` |
| 隐藏界面 | `H` |
| 快速存档 | `Ctrl+S` |
| 读取存档 | `Ctrl+L` |
| 截图 | `F12` |

手柄建议以南键确认/推进、Start 打开设置或菜单、肩键承担快速/自动等离散动作；触屏建议以点击推进、双指点击隐藏界面，保存默认不绑定手势以避免误触。最终默认值应在实现前结合实际 Sample 输入后端确认。

这些默认值参考成熟 Gal 引擎把跳过、隐藏界面、菜单、截图和保存作为独立动作的做法，但 GalNet 仍以自身现有动作语义为准；例如当前“跳过”必须保留确认和未读停止边界。[Ren'Py keymap](https://www.renpy.org/doc/html/keymap.html)、[Ren'Py screen actions](https://www.renpy.org/doc/html/screen_actions.html)

## 4. 输入模型、服务边界与运行时数据流

建议在 `GalNet.Avalonia.GameView` 定义平台无关的展示侧契约；不把 Avalonia 的 `Key`、Windows 虚拟键或具体手柄 SDK 类型暴露到 Core/Runtime。

概念模型如下：

```text
GameActionDefinition
  Id / NameKey / DescriptionKey
  Contexts / TriggerKind / RequiresConfirmation
  DefaultBindings[DeviceKind]

InputGesture
  DeviceKind
  Codes（规范化同时按键集合）/ Modifiers / GestureKind

InputDeviceProfile
  DeviceKind / IsSupported / ReasonKey
  Bindings[actionId] -> primary + optional secondary

IPlayerInputSettingsService
  Actions / Profiles
  GamepadConfirmLayout
  SetBindingAsync / ClearBindingAsync
  ResetDeviceAsync / ResetAllAsync
  BindingChanged / CapabilityChanged
```

`InputGesture` 只使用 GameView 能理解的规范化代码；平台适配器负责把 Avalonia/系统事件转成规范化手势。`IPlayerInputSettingsService` 负责值校验、冲突检测、默认值和持久化，不负责执行游戏动作。

输入执行另设一个窄的动作入口，例如 `IGameActionDispatcher`。它接收 `GameActionId` 和当前输入上下文，交给当前游戏页/宿主：

- Quick/Auto/Skip/Hide UI/Screenshot/Settings/Load 复用已有 ViewModel 或导航命令。
- Save 使用宿主的 `SaveFirstEmptyAsync`，不让设置服务直接依赖文件存档实现。
- 工具栏按钮最终也应调用同一 dispatcher 或同一组动作方法，避免键盘和按钮行为逐渐分叉。

运行时执行数据流为：

```text
键盘/鼠标、手柄或触屏原始事件
        ↓ 平台适配/规范化
InputRouter（判断当前页面、焦点和重复事件）
        ↓
IPlayerInputSettingsService.ResolveGesture
        ↓ GameActionId
IGameActionDispatcher
  ├─ GamePageViewModel：快速/自动/跳过/隐藏 UI/截图/设置/读档
  └─ IGameSessionService：保存到第一个空槽
```

输入设置页面本身不参与游戏动作执行；它只修改 `ResolveGesture` 使用的 profile。这使得设置页、工具栏和实际输入不会互相持有页面控件，也便于在设置页打开时暂停游戏快捷键路由。

### 4.1 输入优先级与事件消费

1. 快捷键捕获控件优先消费原始事件，不能让正在编辑的键同时触发游戏动作。
2. 文本框、选择器、存档页和设置页中的输入不触发剧情页快捷键；必要的返回/取消属于页面自己的导航行为。
3. 游戏页可见或隐藏 GUI 时，游戏动作路由仍然存在；隐藏 GUI 只改变视觉层，不销毁输入路由。
4. 单次按下触发一次动作；只有动作定义明确允许 Hold/Repeat 时才处理按键重复。
5. 现有左键剧情推进继续走 GamePage 的固定交互路径，避免自定义鼠标绑定与左键推进重复执行；可配置动作在其事件已被玩家控件消费时不再向剧情传播。

组合与手势识别需要解决前缀竞争：绑定 LB+A 时不能先执行 LB 的单键动作；更具体组合优先，单键在可判定未形成组合时执行一次。冲突检查也检查单键/组合的重叠，在不能保证无歧义执行时拒绝绑定。触屏同样由一个识别器裁决单击/双击、单指/双指、点击/长按/滑动；识别完成后只分发一个动作。为识别双击而延迟单击的反馈需在实际触屏验收中验证，不允许先推进再撤回。触摸生成的兼容鼠标事件必须去重。

## 5. 存档动作的精确定义

“快速存档”在本 feature 中不是 `FileSaveService.QuickSaveAsync`，而是普通槽位操作：

1. 动作执行时由 `IGameSessionService`/Sample session 在保存门内读取最新槽位状态。
2. 选择 `IsEmpty == true` 且不是损坏槽的最小 `SlotIndex`。
3. 生成当前一致的 `GameSnapshot`，等待玩家变量/进度需要的 flush，再写入该普通槽。
4. 保存成功后刷新槽位列表并显示结果；保存失败不改变已有槽位。
5. 没有空槽时转到现有存档页，让用户明确选择覆盖；快捷键路径本身不能自动覆盖。

选择逻辑必须在会话/存档服务内完成，不能只根据设置页打开时缓存的槽位列表决定目标。重复触发应由保存门串行化或直接拒绝，并给出“正在保存”状态。

## 6. 持久化与生命周期

快捷键属于跨存档的玩家偏好，与语言、音量、显示偏好同级。Sample 建议使用一个新的玩家变量：

```text
player.galnet.input.bindings = JSON string
```

JSON 顶层带 `version: 1`，按设备保存动作 ID 到手势列表。缺失变量加载默认；JSON 损坏、版本未知或单项不合法时忽略该项并保留其他合法 profile，必要时在下一次用户修改/显式恢复默认时写回规范化值。该配置不进入 `GameSnapshot`，也不随读档回滚。

`SamplePlayerSettingsService.AttachAsync` 负责恢复，`SettingsCoordinator` 负责把绑定变化与其他玩家设置的 flush 排队。会话停止、清空玩家数据和设置页销毁前都要等待挂起的保存操作；读取恢复阶段不应逐项产生保存写入。

## 7. 候选方案与取舍

| 方案 | 取舍 | 结论 |
| --- | --- | --- |
| 把快捷键平铺成现有 `SettingItem` | 能快速复用表单，但无法自然表达设备 Tab、主/备用绑定、捕获和冲突 | 不采用 |
| 为操作设置创建新的导航子页面 | 语义直观，但增加三套主题页面映射并污染设置导航历史 | 不采用 |
| 将快捷键追加到现有“游戏”分类 | 会把剧情/播放参数和输入设备设置混在一起，不符合用户期望的“操作”条目 | 不采用 |
| 新增“操作”一级分类并复用现有设置页管线 | 保持“游戏/操作/画面/音频”信息架构，二级设备 Tab 清晰，改动集中 | 采用 |
| 设置服务直接执行保存/导航 | 简单但会把玩家偏好模型与运行会话、文件存档耦合 | 不采用 |
| 所有平台只显示实际可用 Tab | 能减少空内容，但用户无法发现支持范围，且设备能力变化会改布局 | 三个 Tab 固定可发现，不支持时禁用并解释 |

## 8. 兼容性、风险与验证影响

- 没有现有快捷键配置格式，不需要迁移旧绑定；新变量缺失时使用默认值。
- 不修改 Core/Runtime/快照格式，不影响既有存档兼容性。
- 主要风险是手柄/触屏事件源尚不存在、输入捕获与剧情推进事件重入、按键自动重复导致保存/导航重复、保存槽位列表过期以及隐藏 GUI 后输入失联。
- 验证应覆盖设置页三套主题、绑定编辑与冲突、重启恢复、清空恢复默认、设备不可用状态、键盘焦点隔离、重复按键、快速/自动互斥、跳过确认、SaveFirstEmpty 并发与无空槽回退。
- 本文只定义目标设计；正式实现前需要单独 Phase Plan，明确先落地操作分类、键鼠 UI 及数据流，再接入手柄/触屏适配器，或确认三种设备后端可以同批实现。

## 9. 2026-10-16 游戏快捷键路由修正

用户反馈游戏页快捷键无效，并要求模块化。回归证实两处路由缺陷：仅监听 GameShell 无法接收窗口本身持有焦点的按键；将所有 Button 视作编辑器又使工具栏焦点屏蔽了 H 等快捷键。

- 采用无视觉的 `KeyboardMouseShortcutListener`，而不是隐藏的可聚焦控件。键盘 tunnel 订阅所在 TopLevel，指针仍限制在播放器根控件；随根控件 attach/detach 自动接线，Dispose 可重复调用。窗口失活、根内焦点离开、页面切换和释放会取消按住状态及延迟判定。
- 监听器只负责手势解析、重复过滤、组合键等待和焦点范围；通过委托查询能否接收并提交动作，不依赖 GamePageViewModel 或会话服务。窗口无游戏区域焦点可接收，壳外宿主控件和内部文本/选择/范围编辑器不接收。工具栏保留 Enter/Space 原生激活及全部鼠标交互，其余游戏快捷键可用。
- `GameActionDispatcher` 集中执行设备无关动作、运行状态及选择/跳过确认/捕获保护；继续调用现有 VM 命令和会话服务，不复制播放或存档逻辑。键鼠、手柄、触屏共用该执行器。
- `PlayerInputRouter` 保留播放器级组合、重置和手柄导航职责；`PlayerInputSettings` 的绑定解析与持久化不变。本次不添加跨平台原生手柄后端。
- 回归新增窗口/工具栏焦点、编辑器/壳外输入隔离、原生按钮激活、捕获优先、重复挂载/释放、修饰键组合及延迟取消，并在三套主题的真实 GamePage 视觉树验证动作。

## 10. 设置项可调整提示与统一 Esc 返回（视觉部分被第 11 节修正）

2026-10-16 用户追加要求：设置项应更容易看出可以调整；设置、存档、鉴赏等页面默认支持 Esc 返回，不能为每个页面重复输入代码。

- 设置分类仍走原 SchemaForm/SettingsCoordinator，主题侧为设置行和快捷键动作行增加 `player-setting-row`：浅底色、1px 描边、主题圆角、内边距及悬停/焦点描边。编辑控件保留自己的原生交互与错误信息，整行不是伪装成按钮的点击区域。
- 用 `BackNavigablePageViewModel` 提供唯一 `BackCommand` 和默认历史返回；设置、存读档、鉴赏（含分类/详情）与关于页面继承，标题/加载/剧情页面不继承。特殊清理通过 `OnBack()` 扩展，视频详情先停止播放再调用基类返回。
- `PageBackInputListener` 是 GameShell 一次接入的非视觉模块，随根控件订阅 TopLevel；在 bubble 阶段处理未消费的纯 Esc，内部捕获/下拉框/局部取消有更高优先级。tunnel 只跟踪按下/释放，按住 Esc 即使页面变化也只返回一次；失活/卸载/释放清空状态。
- Esc、页面返回按钮及手柄页面返回均调用同一个命令，不复制页面行为。模块对输入设置服务无强依赖，未启用可配置游戏快捷键的宿主也具备默认 Esc 返回。
- 验证用真实窗口和 GameShell 覆盖三主题设置/存档/鉴赏导航、标题页无返回、捕获取消、下拉框/局部优先、长按去重、宿主范围及卸载重挂，并检查设置行实际边框/焦点画刷与渲染截图。

## 11. 绑定编辑框与空 Tooltip 修正

用户明确指出框应加在 Control 等键值编辑入口本身，不是每条配置项的整行。删除普通设置项和快捷键动作行的 `player-setting-row` 外框，恢复原有无逐行卡片的布局。`InputBindingCapture` 显式借用 Button 样式键，并使用 `player-input-binding` 提供独立描边、主题底色/圆角、左对齐键值和悬停时的前景色/描边变化；悬停颜色同时作用实际模板文字，而不是只改变根属性。

设置模板原本无条件创建 ToolTip 内容控件，即使 ReasonKey 为空、内部文字隐藏也仍有非空 Tip 对象。去掉重复 Tooltip，以原有内联可用性原因保留说明。Esc 返回、录入捕获与保存流程不变。
