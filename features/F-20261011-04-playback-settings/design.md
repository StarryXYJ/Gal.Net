# 播放控制与分类设置设计

状态：Design draft。2026-10-11。关联 [feature.md](feature.md)。本文描述目标设计，不代表已实现事实；接口名称可在实现时按项目惯例微调，语义不能省略。

## 目标与已确认方向

原语自己处理正常、快速、追帧调用，基类提供回退；Engine 管理活动实例、自动阻塞集合、推进边界及间隔。自动模式不需要文本与语音配对，也不模拟鼠标操作。

设置服务提供只读分类列表及类型化设置项。整数与浮点各自通过编辑方式选择 Slider / NumericUpDown；Step 仅为操作步长。动态 Choice 失效回退默认值；可见性、可用性及原因 key 可由委托计算，并有明确刷新机制。各主题提供模板，自定义项由工厂构造编辑控件。不引入 AutoSettingUI。

## 当前事实及主要改动

- PrimitiveInstance 有 Dispatch、Skip、IsBlocking、IsSkippable、IsCompleted 和 Completed。Dispatch 只能调用一次；当前 Skip 是玩家手动跳过当前 batch 的请求，不一定完成实例。
- 对白/NVL 在文本显示完成后仍等待推进；音频原语在提交命令后立即完成。不能直接通过所有实例的 Completed 判断自动计时起点。
- CompositeGameView 当前创建并立即 dispatch 实例。Engine Track 在返回后发生，必须兼容实例在 dispatch 内同步结束。
- Engine 自然阻塞完成时自动续跑，自动模式计时需要介入该续跑路径，不能只包装公开 AdvanceAsync。
- GalgroupCompiler 已输出稳定字符串 ID，但 GalgroupLoader 转成顺序整数并丢弃字符串。需要保留稳定身份；EntryIndex 与整数 Id 的位置语义不变。
- Avalonia.GameView 不引用 Runtime。不得为播放控制或设置模型引入反向引用；宿主负责连接 Engine 与共享 UI。
- 完整 GameSnapshot v3 继续使用。旧 replay-save feature 已暂缓，本设计只新增向前追帧执行。

## 分层与职责

| 层 | 新职责 |
| --- | --- |
| Core | primitive 模式/自动阻塞契约、稳定条目身份、纯玩家播放/音量配置值 |
| Presentation.Abstractions | 分发模式输入及原语能力查询，媒体完成/立即完成端口 |
| Runtime | 活动实例、自动阻塞集合、推进策略、计时与取消、稳定进度身份、音频播放 ticket |
| Builtins | 正常/快速/追帧语义，文本自动阻塞状态，语音有限播放实例及最终场景语义 |
| Storage.FileSystem | 已读格式与玩家偏好持久化适配，不拥有播放策略 |
| Avalonia.GameView | 分类设置、共享页面状态、输入与确认行为、主题无关编辑承载 |
| Avalonia.Themes | 设置模板、局部标签页、ToggleButton、确认与禁用原因布局 |
| Samples / Editor | 设置应用与保存、窗口能力、Engine/UI 桥接、媒体后端和资源生命周期 |

Engine 保持运行事实与队列的唯一拥有者，不新增重复解释剧情状态的通用 Runner。内部 PlaybackPolicy/调度协作者可以拆出纯逻辑，但使用 Engine 的同一个执行门和生命周期。

## 原语模式与自动阻塞协议

概念接口：

```csharp
enum PrimitiveInvocationMode { Normal, Quick, CatchUp }

// 保留已有 Dispatch 单次约束、Skip 与 Completed。
bool IsAutoBlocking { get; }
event Action<PrimitiveInstance>? AutoBlockingChanged;
void Dispatch(PrimitiveInvocationMode mode);
void ApplyPlaybackMode(PrimitiveInvocationMode mode); // 已启动实例切换策略

protected abstract void OnDispatch();
protected virtual void OnQuickDispatch() => OnDispatch();
protected virtual void OnCatchUpDispatch() => OnDispatch();
protected void SetAutoBlocking(bool value);
```

模式入口在首次 Dispatch 时只选一个 hook，绝不先正常执行再重复执行快速/追帧 hook。基类快速与追帧默认调用正常实现；该回退仍遵守原语完成和等待语义，不把它当成无副作用或已完成操作。即时变量/解锁等原语通常直接复用正常实现。

已有 Skip/OnSkip 保持手动分段语义。快速/追帧 hook 不复用 OnSkip 名称，避免把视频分段 seek、文本 `\\skip` 边界与完整快速完成混淆。ApplyPlaybackMode 对已启动的实例调用幂等模式变更 hook，不重新 Dispatch；默认不改变当前执行。切回 Normal 不恢复已截断声音或已显示文字，只恢复之后的调用策略。

IsAutoBlocking 为动态事实，允许 false→true 或 true→false；事件仅在实际改变时触发。普通有限 blocking 实例默认在未完成时阻塞自动，Completed 后为 false。长期 non-blocking 默认不阻塞自动。文本/NVL 单独在呈现结束时置 false，但仍保持 IsCompleted=false 等待推进。

TryComplete 原子地结束实例、保证自动阻塞解除，然后发布状态/完成通知；取消或呈现失败不标记“成功已读”。SetAutoBlocking 对已结束实例不能重新设置 true。状态可跨线程改变，事件不直接重入 Engine；Engine 通过调度门重新读取权威状态。

快速/追帧需要立即终结的异步操作提供可观察的完成通知或 Task，不能仅发送完成请求就认为成功。停止/读档先取消 scope，后台清理使用播放 identity 检查，不能删除新实例。

对白的推进等待通过独立的 IAdvanceWaitInstance 概念能力暴露 IsWaitingForAdvance 和 CompleteAdvance，而不是令 Engine 根据具体对白类或 typeId 分支。进入/退出推进等待同样通知 Engine 重评估边界。CompleteAdvance 只接受已经呈现完成的实例；手动点击仍经原有 Skip 执行分段显示。已完成实例、非等待阶段与旧 run 请求均幂等拒绝，不以 SkipText 代替自动推进。

原语注册/描述端口提供阅读边界、长期播放和追帧安全能力查询，供 Engine 在 dispatch 前判定。状态性的 IsAutoBlocking 仍由实例报告，不能只使用静态 descriptor 判断播放结束。编译期阅读 metadata 由 schema/composite 贡献，执行实现对其支持在组合期校验；Runtime 不引用具体 Builtins 类型。

### 内置行为

| 类别 | Normal / Auto | Quick | CatchUp |
| --- | --- | --- | --- |
| 变量、条件、Gallery 解锁 | 执行逻辑 | 同正常 | 同正常；每次真实经过只执行一次 |
| ADV/NVL 文本 | 正常打字；打字结束解除自动阻塞，保留推进等待 | 全文立即显示并解除自动阻塞，保留快速间隔等待 | 已读文本不逐句呈现，更新必要文本逻辑并完成；未读目标正常呈现 |
| flow.wait | 等自然结束 | 可跳过时立即结束 | 省略等待 |
| Once 动画/plan | 完成后解除自动阻塞 | 可跳过时提交末态并结束 | 提交正确末态，不播放中间帧，不补发瞬时 burst |
| Loop/PingPong 动画、持续粒子 | 不默认参与自动等待 | 保持长期状态 | 重建定义与规定起始状态，停止时统一呈现 |
| 有限语音 | 播放 ticket 结束解除自动阻塞 | 停止对应播放并解除等待 | 省略中间语音播放 |
| 普通音效 / BGM | 默认不阻塞自动，可显式声明有限播放参与 | 按原语声明处理 | 瞬时音效省略，长期音频保留最终逻辑状态 |
| 视频 | 阻塞视频等待结束；非阻塞视频默认不参与自动等待 | 可跳过视频直接完成，不循环调用分段 seek | 阻塞视频按终态处理；非阻塞持久图层在停止点从头重建 |
| 自定义原语 | 默认正常运行 | 默认正常运行，保留等待 | 默认正常运行，保留等待；可声明安全追帧或停止边界 |

有 skippable=false 的播放不得强制终结。追帧遇到没有声明安全压缩语义的异步/外部原语时，回退正常执行并等待或停在显式边界，不能假装已经跳过。扩展文档明确这一点；不恢复旧 replay-save 要求所有扩展都支持静默 Replay 的规则。

场景 terminal 写入只能执行一次：当前非阻塞动画已经在 Dispatch 提交末态，后续快速/追帧不得重复叠加 Additive 数值。plan 的持久事件与末态使用统一的逻辑状态计算，瞬时 presenter 事件按模式过滤。

## 自动阻塞集合、推进边界与时序

Engine 为当前 run 跟踪所有活动 primitive；以实例 identity 维护 HashSet<PrimitiveInstance> 自动阻塞集合，数量由集合取得。不是只维护裸整数，亦不是从 SceneInstances 枚举：后者保存图层等持久对象，不等价于执行实例。

注册顺序为订阅事件、读取 IsAutoBlocking/IsCompleted、幂等协调集合。由于当前 view 可能已经 dispatch 并同步完成，初始协调必须读取实际状态。随后事件排入 Engine 的执行门，再读取状态；不能按旧事件无条件 +1/-1。所有订阅移除与集合清理使用同一执行门。

阻塞集合为空只是必要条件，计时还必须满足：

1. 当前模式为 Auto，运行会话和推进边界仍有效；
2. 正常批次已经分发到 blocking/对白推进边界，当前不存在尚未分发的同轮前置指令；
3. 没有待选 Choice、追帧/确认操作、终态或页面暂停；
4. 当前有限 flow blocker 均已结束，剩余对白仅等待推进；
5. 当前所有显式自动阻塞实例均已解除。

自然阻塞完成后，Engine 先判断策略再执行 continuation。Normal 延用现有续跑；Auto 在已达到的边界进入间隔等待，不直接跑到下一句；Quick 使用快速完成及间隔规则。纯即时语句不制造计时间隔；无文本的有限阻塞边界也可在自然完成后使用自动间隔。

文本与语音无一对一关联。尚未结束且声明自动阻塞的语音即使来自上一批，也会阻止本次自动推进，直到结束/停止；若作者不希望这样，应令该播放不参与自动阻塞。长期 BGM 不参与。

Engine 内部边界标识包含 RunGeneration、BoundaryRevision；计时任务另有 ModeRevision 和 linked cancellation。到期后重新取得执行门并校验全部条件，才释放当前对白的推进等待并继续。Completed、AutoBlockingChanged、玩家操作或设置变更不能产生两个有效计时任务。

计时期间新增自动阻塞：取消计时，等待再次清空后从完整间隔重新开始。间隔设置改变时同样重新计时。关闭模式立即使旧 ModeRevision 失效；等于零的间隔仍异步调度/让出，不产生同步递归。

进入 Choice、页面离开游戏、确认框、读档、重载、终态与 scope 销毁均切回 Manual 并撤销计时，回到游戏不自行恢复。模式只保存本会话状态。

## 音频完成与玩家音量

AudioRuntime.Play 增加可观察的逻辑播放 ticket；ticket 在 enqueue 前建立，覆盖等待队列、加载、播放、结束、失败、替换、清队和取消，不只代表 voice 已开始。ReplaceIfDifferent 命中已有 voice 时，ticket 关联实际仍在播放的同一代对象；暂停保持未完成，恢复后继续。

声明 autoBlocking 的 audio.play 产生 non-blocking 有限播放实例：普通剧情仍可立即分发文本，但实例保持活动直到 ticket 结束。语音 composite 只需为音频设置 autoBlocking=true，不绑定具体文本。普通音频默认 false；允许作者显式选择，循环/无限播放不能声明自动阻塞。

后端不存在、Start 请求抛错、资源失败、被溢出规则丢弃等路径也必须完成 ticket 并诊断，否则自动模式可能永久等待。Ended/Failed 用 voice identity + generation 过滤；播放实例与后台命令的生命周期独立排空。

玩家音量使用 Master、Bgm、Sfx、Vocal 及对应静音值。有效增益由内容 gain × track DefaultGain × bus 玩家音量 × master 玩家音量组成，静音后为零，并在最终边界限制到后端合法范围。设置改变刷新当前 voice 及后续 voice，不能改写作者内容 gain。SEffect 自己的 gain 保持独立。

追帧维护会话内音频逻辑投影，按原有 placement/stop/queue 规则处理命令但不启动中间后端播放；忽略瞬时 SFX/语音，停止点只恢复最终应持续播放的轨道。该投影不进入本期存档格式，不扩大为精确 seek、shuffle 或音频持久化 feature。

## 已读身份与向前追帧

PrimitiveEntry 增加稳定字符串身份，loader 保留已编译 envelope.Id，CompositeGameView 规范化时原样复制。整数 Id/EntryIndex 继续表示位置，不能作为进度身份。

进度键使用 graph Group node ID + 稳定文本 ID；与语言无关。ADV/NVL 阅读原语统一声明文本边界，Engine 不通过硬编码唯一 dialogue.text 判断。当前句开始前捕获 WasRead；全文成功呈现后 MarkRead，失败/取消不标记。改动文本而保留同 ID 仍视为同条目；作者需要重新视为未读时更换稳定 ID。

语音 composite 在 audio 前增加可供 Engine 查询的阅读边界 metadata，引用随后稳定文本 ID。这只用于进入前的未读检测/停止，不关联音文完成。编译器/loader 验证边界连续性、身份和 condition 一致性；普通文本自身也有边界。不通过字符串解析 '#n' 猜源条目，不复用 BatchId。

如果某个 composite 内有不同条件，必须拆成可验证的阅读边界，不能把未执行文本作为停止目标。用户快速仅已读设置在边界进入前生效；开启当前句快速仍使用缓存 WasRead。

progress.json 使用 v2 稳定键集合；旧顺序数字键格式直接拒绝，不备份、清空或迁移。需要新玩家配置或显式清空进度后使用；拒绝读取不修改文件。该决定遵循用户新增“不考虑向后兼容”的要求。

追帧在确认后从当前真实 Runtime 向前执行，不搜索目标后直接跳游标，不载入旧路径变量快照。停止检查在目标阅读边界分发前、Choice 建立前进行；到达未读目标后恢复 Normal 并呈现该句。若当前句 WasRead=false，不跨过当前句。

追帧有单一操作 token，按有限指令预算分段让出，并在每段检查取消。预算用于 UI 响应，不把循环当错误或硬跳出口。左键取消追帧时保留已经执行的变量和逻辑状态，在当前合法边界同步展示；不会回滚已发生的外部动作。

Builtins 的追帧语义直接更新 Runtime 状态并暂缓展示。宿主提供窄的 Begin/EndCatchUp presentation 协调端口，停止/取消时用同一 scope 的最终场景重放及文本/NVL 可见状态同步；Engine 只等待端口，不依赖 Builtins/具体 renderer。恢复失败明确报告并停止继续执行，不能展示旧画面后假装成功。

进入追帧时已有播放必须停止或转入声明的压缩状态，隔离晚清理，不能只把 future Dispatch 改为 CatchUp。取消追帧不取消整个 Engine run scope；模式 token 与 run token 分离。

2026-10-12 接线细化：PrimitiveInstance.PresentationCompletion 将逻辑完成与异步展示 finally 清理区分；Engine 进入追帧先等待旧展示任务，BuiltinPresentationReplay 在 dispatch 前向宿主交付 continuation，Sample 跟踪并排空恢复实例。SampleGamePresentation 本身实现 ICatchUpPresentation，复用现有 restorer 和音频模块，不新增执行协调框架。SceneState 不包含文本/NVL 历史，故通过可选 ICatchUpTextRecorder 在追帧中记录已解析文本；AvaloniaDialoguePresenter 缓存最后对白、NVL 历史和可见状态，最终一次重建。该投影仅在当前展示会话有效，不扩展 snapshot v3，也不承诺恢复存档中的 NVL 历史。

## 输入与主题

GamePage 左键 tunnel 行为先撤销旧模式/追帧请求；只过滤左键，其他鼠标键不误推进。保留 IsPlayerInteraction 对推进的屏蔽，但它不屏蔽模式取消。

剧情区域点击取消旧模式并执行一次手动 Advance；工具栏仅执行按钮命令。模式按钮需保存 PointerPressed 前的选中状态，按该状态决定 Toggle，避免 tunnel 先清空状态导致无法关闭已选按钮；命令路径和键盘激活同样一致。点击另一模式直接切换，只有一个 enum 状态源。

带主题资源的确认 overlay 位于 GameShell 玩家根，独立于页面导航历史。确认期间禁止背景推进，防止重复弹框；关闭会话取消等待。新增确认 ViewModel/主题模板按组合注册校验，不借用宿主调试窗口的 Ursa MessageBox 样式。

Phase 4 实施细化：确认只含现有播放 VM 的两条命令和一个可见状态，因此复用 GamePageViewModel，不增加独立导航页或确认 VM；GameShell 持有 SkipConfirmation 行为控件，主题提供 ControlTemplate。根 PageHost 在确认期间禁用，Escape 取消。该选择减少无独立生命周期的样板类型，不改变确认和主题所有权。

## 分类设置模型

模型留在 Avalonia.GameView 的设置边界，普通项不持有 Control。现有 CommunityToolkit.ObservableObject 与本地化适配继续使用，不新增生产项目或第三方 UI 依赖。

```csharp
IPlayerSettingsService.Categories : IReadOnlyList<SettingCategory>
SettingCategory { Id, NameKey, IReadOnlyList<PlayerSetting> Items }
PlayerSetting { Id, NameKey, DescriptionKey, ValueType, Availability, ErrorKey }
PlayerSetting<T> { Value, DefaultValue, TrySetValueAsync(...), RefreshValue() }
NumericEditorKind { NumericUpDown, Slider }
SettingAvailability { IsVisible, IsEnabled, ReasonKey }
```

分类和设置项结构在组合期复制/冻结，Id 在服务内唯一。值、可用性和 Choice 候选集可以动态变化。基类 ValueType 从 T 推导，类型擦除仅用于异构列表，不做任意 object/string 强制转换。

内置 IntSetting、DoubleSetting、StringSetting、BoolSetting、ChoiceSetting<T>、ActionSetting。数值项包含 Minimum、Maximum、Step、EditorKind、UnitKey、Format；构造时验证范围和 Step 合法，写值只按类型、有限值、范围及业务 validator 验证，不按 Step 的倍数限制。Slider 不启用步长吸附；NumericUpDown 允许输入范围内任意合法值。

Choice 有稳定选项 ID、类型化 Value 和名称 key（动态语言可提供显示名称适配）；更新候选集时验证唯一性，当前值失效选择有效 DefaultValue。默认值由静态值或默认值工厂给出；如果默认值也无效，候选提供者须先更新默认值，否则禁用设置并报告配置错误，不随意选首项。空列表同样禁用，待恢复候选后重新协调。有效回退经过正常应用与持久化，不只刷新控件。

### 可见性与可用性

每项可选 Func<SettingAvailability> EvaluateAvailability，默认可见可用；结果缓存并通知 PropertyChanged。一次求值返回三项状态，避免分开委托观察到不一致状态。求值无副作用，不在 getter 内保存或改配置。

服务在任意设置成功生效后统一刷新各项 availability（项数较小，无需新增依赖表达式框架）。宿主能力/外部偏好变化通过可释放订阅触发 RefreshAvailability，并调度到 UI 线程。只刷新变化的结果，不递归写 Value；释放服务时解除订阅。

禁用原因 key 由共享本地化适配响应语言切换。设置行可接收悬浮/键盘焦点提示，编辑控件禁用但原因容器不整体禁用；在触摸端支持点按说明或行内说明。隐藏项不占布局。不可用状态由 TrySetValueAsync 再检查，不能仅依赖控件 IsEnabled。

### 应用、保存、默认值与强制值

每项绑定类型化读取/应用协作者，服务负责持久化。顺序为校验→实际应用→读取生效值→通知 UI/依赖→保存；UI Value 的绑定入口调度该流程并公开 IsApplying/ErrorKey。应用失败恢复实际值，保存失败保留已生效值并报告/保留待重试状态，不假装写盘成功。

同项/相互依赖的应用通过服务串行门协调。窗口或媒体异步应用不在 UI 上同步等待；新编辑可排队，退回/释放等待已提交工作或明确取消。恢复偏好不逐项重复保存，候选修正与默认回退完成后按需保存一次。正常退出 flush，清空玩家数据恢复默认。

有效值优先级为开发者强制值→合法已保存玩家值→默认值。UI 删除/隐藏和策略固定独立。默认服务可删项但仍保留其运行默认配置，不因缺少控件停止设置消费。

Sample 延用 player-variables.json 和现有 locale/textSpeed 键，其他内置值使用 player.galnet.settings.* 稳定命名空间。共享页面不引用 FileVariableService。设置值的校验、类型与序列化在宿主适配边界完成，运行时播放/音量值在启动前应用。

### 主题模板与自定义控件

各主题根据具体设置项和 NumericEditorKind 提供 DataTemplate。开放泛型 Choice 用非泛型编辑 adapter 暴露选项/选中项，值转换在类型化设置内部校验，主题不反射枚举 T。

共享 SettingsRow 统一呈现名称、说明、单位、可用性、错误及编辑内容；三主题可替换模板和布局。数值编辑器处理 NumericUpDown 的 decimal↔int/double 安全转换，越界/溢出给出错误，不通过字符串文化隐式转换。

CustomSetting<T> 的 Avalonia adapter 持有 Func<SettingEditorContext, Control> 工厂，传入类型化项、主题/本地化/服务上下文；每次创建新控件，工厂自行构造绑定但经统一值应用入口写入。承载方提供编辑器租约/可释放 hook，切换分类或页面销毁解除事件订阅，不把临时 detach 当作永久释放。普通配置值与运行协议均不保存该 factory。

未知项类型提供可见诊断；自定义模板注册/内置模板完整性在组合期或主题验收中检查。

## 设置分类与平台能力

SettingsPageViewModel 暴露 Categories/SelectedCategory；无 navigation 子页面。主题使用 TabControl 或统一局部 selector，保留当前分类；被移除或全隐藏分类回退首个可见分类。

| 分类 | 默认项 |
| --- | --- |
| 游戏 | 语言、文本速度、仅已读快速、快速间隔、自动间隔 |
| 画面 | 显示模式、窗口尺寸/支持的显示分辨率 |
| 音频 | Master/Bgm/Sfx/Vocal 音量和静音 |
| 控制 | 本期不注册空分类，后续快捷键 feature 追加 |

默认文本速度 30 字符/秒（0 即时），快速间隔 50 毫秒，自动间隔 2000 毫秒，快速仅已读 true；范围分别为 0–120、0–5000 毫秒、0–30000 毫秒。2026-10-12 用户确认等待时间采用整数毫秒：设置使用 IntSetting/NumericUpDown，运行配置及持久偏好均为 int，字段名显式包含 Milliseconds；调整步长分别为 10/100 毫秒，仍不参与校验。旧浮点秒偏好不迁移，按类型无效回退默认值。音量 0–1，初始 1 避免与作者轨道默认 gain 重复衰减。开发者可覆盖范围/default/模板。

窗口设置通过宿主提供的 IPlayerDisplayService 概念端口协商 capabilities、候选项、实际状态及 ApplyAsync。Sample desktop 支持窗口/无边框全屏，独占全屏仅后端明确支持时提供，不能把 Avalonia FullScreen 名称当作独占保证。无窗口平台/Editor 嵌入预览隐藏不适用项。窗口尺寸不是项目设计画布分辨率；全屏模式下窗口尺寸禁用并解释原因。应用失败恢复生效值；危险显示模式的确认/自动恢复仅在支持实际显示模式切换的宿主实现。

已有 Ursa 依赖不意味着内置玩家主题采用其样式；Choice 默认使用当前主题下的 ComboBox/selector，未来可覆盖为 enum selector。无新增 Ursa/AutoSettingUI 依赖要求。

## 候选方案与取舍

- 定时模拟点击：无法表达自动就绪、全文快速与追帧区别，排除。
- 等所有实例 Completed：对白等待推进形成循环，循环媒体无终态，音频命令已完成但声音仍播放，排除。
- 为每句文本绑定语音：额外维护关联并限制自定义原语，采用所有声明自动阻塞实例的集合取代。
- 裸阻塞计数：容易重复扣减和遗留，采用 identity 集合与状态重读。
- 强制所有原语实现三套逻辑：重复即时逻辑，采用基类正常回退与特殊原语覆写。
- 每种数值 UI 一个值类型：重复验证，采用 int/double + EditorKind。
- AutoSettingUI/属性反射：用户明确不采用，沿用可扩展设置项与主题模板。

## 兼容、迁移与风险

原语 hook 的正常调用默认行为是扩展语义复用，不是历史接口适配。IGameView 只保留模式感知的 Dispatch 契约；对白/NVL presenter 必须实现真正的全文显示，删除以一次 Skip 假装全文显示的旧接口回退。实现者需更新到当前接口，不承诺旧扩展兼容。

阅读 metadata 改变编译内容契约，需要版本化并更新编译、加载、导出、安装与 samples。raw 无结构变化可保留版本，compiled 独立升级版本并拒绝旧产物、要求重编译；不要只改共享版本常量导致无理由拒绝旧 raw。GameSnapshot 结构仍 v3；旧内容保存位置无法证明与重编译内容一致时不得承诺跨内容兼容，本期不扩大为存档迁移。

主要风险为自然完成抢先续跑、计时事件重入、同步完成漏订阅、声音加载失败不释放等待、动画末态重复叠加、追帧结束晚回调污染，以及异步设置更新/关闭的竞争。run/mode/boundary identity、集合协调、单次逻辑提交、可取消等待和明确错误反馈是共同约束。

本设计不新增跨项目依赖或替换存档架构，无需单独 ADR；未来若实施中引入新跨层依赖或长期存档协议，应另行提出 Proposed ADR。

## 验证与验收影响

- Runtime：同步完成/事件乱序去重、多个自动 blocker、阻塞重新出现重置计时、零间隔、Choice/终态停止、自然 continuation 策略、取消/读档代际隔离。
- Builtins：Normal/Quick/CatchUp 只执行一次，文本完整呈现与手动分段区别，NVL、失败不已读，动画/plan Additive 末态不重复提交。
- Audio：排队/替换/失败/无后端/静音 ticket 终止、late generation、语音与文本无关联仍正确等待、当前 voice 音量即时生效。
- Core/Loader/Storage：稳定 ID 保留与位置分离、语音 composite 未读前停止、条件边界、旧 progress 拒绝且不修改与新格式。
- Settings：Step 非倍数合法、数值转换、Choice/default/空候选、强制策略、委托刷新与释放、应用失败/保存失败/关闭 flush。
- Presentation：三主题模板、禁用原因可访问、分类切换、自定义 editor 生命周期、Toggle/左键事件顺序、确认框屏蔽背景。
- Integration：Sample/Editor 构造、跨语言已读、音文播放自动计时、追帧变量/解锁/场景结果、平台能力与持久偏好。

本阶段只保存设计并检查文档一致性，不运行与业务代码改动无关的构建测试。下一阶段编写可验证的 Phase Plan 后再进入实现。
