# P6e3：Timeline 插值、切线与曲线

日期：2026-10-19。前置 P6e2b verified。按可验证切片实施，不纳入游戏实时预览。

## P6e3a：关键帧详情与批量切线

状态：verified。关键帧详情区保留布局空间，无选择时禁用，避免框选/拖动过程中画布换位。选择已有关键帧后显示数值、出段 Step/Linear/Hermite、入/出切线。多选支持一次统一设置一个字段，混合值留空；首帧可改值/切线但沿用既有移动/删除保护。数字使用 NumericUpDown，Enter/失焦提交，Escape 撤销未确认输入；插值立即确认。

原始切线单位为值/帧，可切换按秒显示，换算为值/秒 = 值/帧 × FPS，保存始终值/帧。平坦、线性、平滑、对齐为一次性明确动作，不引入持久自动切线模式。切到 Step/Linear 保留未使用切线。每个多键动作是一次根 JSON patch/项目历史，保留大小写、未知字段、键身份和其他参数。选择/Entry/帧/根版本改变拒绝旧详情提交，保存和切语言保留有效草稿。

详情是专属 Timeline 控件，延续既有 NumericUpDown/ComboBox 布局，不新增运行时 DTO/schema。同一键的插值属于其后区间；末键仍可保存插值但无出段效果。有限数值和已有属性端点范围沿用现有校验，不在 Editor 单方面加入不同于编译/Runtime 的 Hermite 极值拒绝规则。

验证：实际关键帧选择/详情控件、混合值与批量单历史、切换插值保留切线、按秒换算/FPS、一次性预设、过期/取消、四语及保存重开，受影响回归/解决方案构建/格式/源码指纹。

## P6e3b：值曲线与拖柄手势

状态：verified。前置 P6e3a 和本轮 Timeline 交互简化。按 2026-10-19 用户确认改为属性内联曲线 UX，不再新增独立曲线/详情区域；实时游戏预览继续排除。

1. 每个已重载属性提供展开按钮，展开为该属性的值视图，与时间轴共享时间坐标、播放头、选择和横向缩放/滚动。普通关键帧视图的位置表示时间；值视图的纵坐标表示值，支持值域缩放/平移，树与画布行高统一更新，折叠不写作者数据。
2. 关键帧右键基于当前选择提供插值和切线菜单：左侧/入切线、右侧/出切线、联动/断开、平坦/线性/自动等。右击未选键先选择该键；右击已选键保留多选集合。菜单批量动作一次历史，混合模式不伪装为统一值。
3. 自动贝塞尔需定义与 Hermite 斜率的对应关系、邻键移动/增删后的自动重算和作者模式持久化；当前“一次性平滑”不得仅改名冒充持续自动模式。首版使用非加权切线；加权柄另行评估，不作为当前必需项。
4. 值图中关键帧按值定位，显示入/出切线柄；值显示和拖柄按属性类型/能力分派，当前 float 能力先接通数值曲线，不把未来离散类型强行套成连续曲线。手势拖动本地预览，松手一次历史，Escape/捕获丢失/Entry 或源版本变化取消。复用当前单键/多键选择与作者 JSON patch，保留未知字段。0 帧不能横移/删除，但可修改值和切线。
5. 共享 Avalonia 数值曲线控件负责绘制/命中/手势，Editor 适配负责属性能力、数据/历史和模式重算。Step/Linear/Hermite 绘制与运行时 sampler 一致，不以显示钳位掩盖 overshoot。实施前核对有限属性范围的区间极值诊断和共享校验边界；接受规则变化需同步中立规则与消费者。
6. 内联编辑和右键菜单接通后，移除当前独立关键帧详情区及独立预设按钮；必要的精确数值输入放在对应键/柄的菜单或就地输入中，避免同一行为出现两套永久入口。

退出条件：真实主题下属性展开/折叠与行对齐、键/柄鼠标与菜单、批量单历史/撤销重做、自动模式邻键重算、取消/过期拒绝、四语和保存重开；采样与运行时一致，相关项目回归和解决方案构建通过。上述范围已实施，结果与保留边界见本页实施记录。

## Timeline 交互简化（2026-10-19）

状态：verified。移除更换对象按钮及相关替换路径、属性勾选和“可添加属性”模式，统一为顶部选择对象、对象行“＋”选择属性。对象/属性（含属性组）右键“取消重载”；移除对象/属性组整批提交一条内容历史，空对象和等待初值的属性仅取消本地草稿。过期树行不能通过旧菜单修改新索引。父行汇总点允许拖动：该时间点所有后代键一起移动，包含折叠子项；其他时间点不动，沿用第 0 帧/碰撞/取消保护。

真实 Semi 控件回归覆盖对象/叶属性右键菜单、整批单历史及 Undo/Redo、空对象/未知初值草稿取消、旧菜单/旧树行拒绝、未知 JSON 保留和保存重开；画布覆盖折叠对象与属性组汇总点拖动、Ctrl 部分选择补齐后代键、同帧子键一致移动而其他帧不变、碰撞及 Escape 取消。聚焦 18 项通过。首轮 17 通过/1 失败来自旧测试假定候选对象总在树第一个位置，已改为按稳定对象身份定位；失败日志 timeline-ux-focused.log 保留，修正后结果 timeline-ux-focused-2.trx。

最终隔离解决方案构建成功（105 条构建/分析器警告，0 错误）；同一源码快照 Editor 181、Core 111、Architecture 45、Presentation 243，共 580 项通过，0 失败；另有 6 项原生媒体环境用例在 TRX 中为 NotExecuted，未把控制台“已跳过 0”汇总误记为全部执行。whitespace/IDE0005 verify 与源文件 SHA-256 核对通过，四语各 725 keys；菜单和简化布局截图已检查。

证据目录沿用本文件末尾 timeline-validation，脚本 validate-timeline-ux.ps1；构建 timeline-ux-final-solution-build.log，回归 timeline-ux-final-GalNet*.trx/.log，格式 timeline-ux-final-format-verify.log，指纹 timeline-ux-final-source-sha256.json，截图 timeline-ux-override-menu.png 和 p6e1-property-tree.png。

该次交互简化结束时保留独立详情区；后续 P6e3b 已由内联曲线和菜单替换。P6e4/P6f 仍 planned。

## P6e3a 实施记录

`TimelineDocument.Curves` 在原始树上批量修改选中键，沿用现有字段大小写、新增字段使用 camelCase；整次 patch 构造成功后才提交。`TimelineKeyEdit` 捕获文档、代次和选中键集合，`TimelineEditSession` 管理当前详情草稿；成功修改后按键位置重新关联选择，其他根替换/选择/帧改变使旧对象失效。详情初次挂载和默认值不写作者数据，避免控件初始化事件补写默认参数。

平滑使用前后邻键连线斜率，端点使用相邻区间；线性分别计算入/出区间斜率；对齐取现有入/出斜率平均。中间运算使用 double，保存前检查 float 有限性；任何一个目标失败均不写入。秒制仅用于显示/输入换算，FPS 修改保留原始值/帧。

聚焦回归 4 项通过：真实 Semi 主题的鼠标多选、NumericUpDown/ComboBox/预设按钮、混合值、单历史/首帧保护、大小写/未知字段、秒制/FPS、过期/取消、四语/保存重开；混合轨道范围整批拒绝；推导斜率溢出拒绝；原关键帧画布指针手势。截图 timeline-key-details.png 已检查。测试中修正了 Headless 输入修饰键的 API 类型，以及对新写入字段 camelCase 的错误预期，未通过修改产品 JSON 规则迎合测试。

最终隔离解决方案构建成功，85 条分析器/构建警告，0 错误；同源码快照 Editor 176、Core 111、Architecture 45，共 332 项通过，0 失败、0 跳过。whitespace/IDE0005 verify 与全部 11 个变更源文件 SHA-256 一致，四语各 705 keys。没有改动 Core/Runtime/编译的 Timeline 接受规则，也没有运行与本切片无关的原生媒体用例。

证据目录：`C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation`。脚本 validate-key-details.ps1；构建 key-details-final-solution-build.log；TRX/日志 key-details-final-GalNet*；指纹 key-details-final-source-sha256.json；格式 key-details-final-format-verify.log；真实控件截图 timeline-key-details.png。聚焦结果 key-details-focused.trx 包含新建的 3 个用例及原画布指针用例。

正式行为见 [Group/Timeline](../../docs/spec/editor-groups.md)、[i18n](../../docs/spec/i18n.md)。未修改开工时已有的 feature.md 与 group-property-editor-design.md 设计改动。P6e3a 退出条件满足；后续 P6e3b 当前结果见下文，P6e4 事件/剩余语义与 P6f animate 自定义曲线随后推进。

## P6e3b 实施决策（2026-10-19）

作者键增加 Editor 专用 `editorTangents` 对象，保存 incoming/outgoing（Free/Flat/Linear/Auto）及 linked，保留其中未知成员。运行时继续只消费现有值/帧斜率和 Step/Linear/CubicHermite；Editor 提交值、增删/移动邻键时，在同一根 patch 内重算自动/线性/平坦切线。自动模式使用前后邻键中心斜率，在局部极值取零，并限制为相邻同号斜率最小绝对值的三倍；端点采用相邻区间斜率。此为明确的非加权、限制过冲的自动贝塞尔行为，不宣称逐项复制 Unity/Premiere 的内部算法。

左右侧可独立设模式，单侧选择断开联动；联动动作先平均入/出斜率并转自由模式，随后拖柄/精确输入同步另一侧。自动模式直接保存派生数值，保存重开仍保留编辑模式；编译/运行时不需要新的动态重算协议。旧无模式键默认自由且断开，打开页面不补写作者数据。

属性叶默认收起，展开增加就地值图，原时间坐标和播放头共享；树/画布采用同一可变行高。图形采样直接调用 Core TimelineSampler，适应值域包含 Hermite 区间极值；不更改原有属性端点/Runtime 有限键的接受规则，不新增 Editor 独有的区间越界拒绝。图中值域不钳制，所以已有 overshoot 可见。曲线摄像机是视图状态，Alt+滚轮缩放、Alt+拖动平移、适应值域按钮不写历史。

关键帧时间/值及入出柄拖动统一局部预览和松手单历史，首帧只允许改值/切线；非法手势释放取消，不执行隐式覆盖碰撞键。普通时间轴原有显式碰撞替换保留。右键未选键先选中，右键已选键保留多选；插值、左右/双侧模式、联动/断开、精确数值弹窗替代独立详情区。

验证已完成，见下方最终记录；P6e4/P6f 和实时游戏预览未混入本切片。

共享采样边界复现：新增 Step 前段之后内部键的精确帧与前后分数帧断言，原实现 Evaluate(10) 返回 0、预期 1，基线日志 inline-curve-step-before-fix.log/TRX 已保留；Core 区间查找改为 <=，不改变现有接受范围。经验见 `.agents/lessons/L-20261019-02-timeline-exact-key-frame.md`。两次初始构建分别暴露 Avalonia Point 减法结果不能直接取 Length，以及迁移测试缺少 Avalonia 引用，均已修正，日志 inline-curve-initial-build.log 和 inline-curve-second-build.log 保留；不属于产品运行失败。

聚焦回归 20 项通过；追加的精确值域/极值、摄像机缩放锚点、Alt 平移、范围/非有限切线整批拒绝及选择变化取消等边界聚焦 3 项通过。最终完整构建与六项目回归正在验证，退出状态以最终结果为准。

主工作区完整构建通过（121 警告、0 错误），但 Editor 总回归为 181 通过/3 失败：BrowserAddressBlocksDropAndGlobalSearchExitFollowSelection 的目录块点击、GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor 的尾部留白、GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave 的宽度。工作区当时另有非本轮 Condition/EntryList 控件及 Condition.Placeholder 修改，未纳入本轮提交；文件面板点击此前已有偶发记录。不凭猜测修改这些文件，也不隐藏失败日志 inline-curve-final-GalNet.Editor.Tests.*。

为确认本轮候选，使用 git archive 117d6c1 加明确 Timeline/Core 源文件和仅 12 个 Timeline.Curve.* 新 key 创建隔离快照，排除 Condition.Placeholder 和未提交控件改动；依赖用清空 packageSources 的 config 从全局缓存离线还原。第一次快照构建因默认契约固定 Content 路径 MSB3030 失败；以快照自身 artifacts 产物补齐 DefaultContracts 与 schema fixture 的普通 bin 预期位置后，重建成功（38 警告、0 错误）。相关前置条件见 L-20261019-03 lesson，未改业务 csproj。隔离六项目回归与指纹待最终结果。

### P6e3b 最终验证

隔离候选源码六项目回归通过：Editor 184、Core 111、Architecture 45、Presentation 243、Primitives.Builtins 117、Integration 110，共 810 项，0 失败；Presentation TRX 另有 6 项 NotExecuted（原生媒体环境），未误用控制台“已跳过 0”汇总。四语各 737 keys，新增的 12 个 Timeline.Curve.* 文案一致；主工作区另有 Condition.Placeholder，不纳入候选或提交。

菜单截图 timeline-inline-curve-menu.png 已检查。收尾复核发现精确输入 Flyout 关闭后 attached flyout/content 仍能保留草稿引用，已补关闭时解除绑定、清空 DataContext/Content/attached flyout 的释放逻辑及实际弹窗断言；同一候选快照受影响 Editor 184 项重新全部通过。其他五项目的代码和输入未变化，未重复运行。whitespace/IDE0005 verify 和原工作区/候选快照源码指纹一致性通过。

最终证据：inline-curve-isolated-build-2.log；inline-curve-isolated-GalNet*.trx/.log；弹窗修正 inline-curve-popup-build.log、inline-curve-popup-GalNet.Editor.Tests.trx/.log；格式 inline-curve-final-format-verify.log 与 inline-curve-popup-format-verify.log；最终指纹 inline-curve-main-final-source-sha256.json、inline-curve-snapshot-final-source-sha256.json。证据目录沿用本页 timeline-validation。主工作区首次 3 项非 Timeline 失败保留在原日志，不伪报原工作区全绿；本轮候选已排除并保留另一组控件改动。临时源码快照验证后清理，避免测试项目递归扫描误识别重复 csproj；HEAD 源码归档和输入清单保留。

P6e3a/P6e3b 退出条件均满足，P6e3 verified；P6e 整体仍 in-progress。后续 P6e4 事件/剩余语义、P6f animate 自定义曲线；加权切线和实时游戏预览不在本轮范围。
