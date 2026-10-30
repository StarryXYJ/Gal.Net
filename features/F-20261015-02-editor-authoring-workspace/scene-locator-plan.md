# 场景定位器复用与 Timeline 对象入口

日期：2026-10-18。状态：verified。用户授权实施并明确复用 Assets Picker、抽取指定剧情位置的场景查询服务，以及顶部选对象/对象行添加属性的两步交互。最终结果与证据见末节。

实施前 P6e1 已提供 SceneLocator（Group/Entry/输出槽位）、SceneTarget 和 ProjectSceneTargets.Before 查询，没有可复用的作者场景目标选择控件。资产选择的数据源是文件/资产，场景目标是某次创建的实例；同一图片创建的两个对象必须可区分。候选展示可复用，身份和查询域分别适配。

## P6e2b：场景定位器与对象选择

**前置**：P6e2 及交互简化 verified。先于 P6e3 曲线编辑，从 P6e4 提前提取当前已支持目标的选择闭环，不等待 Timeline 事件或全部自定义语义。

**目标与模块**：ProjectSceneTargets 是项目 scope 的场景状态查询服务，输入 Group/Entry 位置，返回该位置执行前的对象、Locator、内部 handle、存在性、支持属性和已知值；null Entry 表示 Group 末尾。缓存与有限状态推导由服务承担，不依赖控件、不打开弹窗。SceneTargetCatalog 将查询事实适配为本地化候选并解析引用；SceneTargetSelection 是控件使用的短期选择上下文。共享 Avalonia 候选展示控件不依赖 Editor 业务服务；普通字段 binding 与 Timeline 各自提交结果，不抽统一弹窗调度服务。通过特定 DTO 成员的语义注册接入，不能给所有 string 注册目标选择器。首批覆盖现有查询支持的层/粒子/效果及目标字段；新增播放实例与 Timeline 帧内查询仍留 P6e4。

用户进一步澄清复用层次：从现有 Assets Picker 提取搜索/卡片/缩略图/键鼠确认的 ResourcePickerPanel，原 Assets Picker 本身改用该组件；Editor 公共 SceneTargetSelection/SceneTargetPicker 统一候选、显示、选择、过期校验和 handle 解析。普通字段与 Timeline 直接复用这一对公共对象。资产文件和场上实例的数据源/身份仍独立，不再增加 Timeline 专属的对象/属性合并表单。

**选择行为**：可搜索资源名/对象名、类型和创建来源，显示一定/可能存在；查询当前条目执行前的状态。内部以 Locator 识别创建者输出并解析 handle，作者无需看到或输入 handle。不同创建实例分开列出；未解析引用保留并允许重选，刷新不清空参数；不支持语义显示候选可能不完整。

**Timeline“＋”**：根据用户最新澄清，交互分为两步。顶部“＋”只显示对象卡片选择器；确认后在对象栏显示目标，对象行左侧“＋”弹出可加入重载的属性列表。选属性后在叶行编辑，不在选择器中配置初值/模式。已知值直接创建轨道，每个属性一次历史；未知值先显示空值草稿行，明确输入后才创建。没有属性的对象仅是当前面板草稿，切换 Entry 清除，不新增空运行时对象数据；取消不写内容，重复对象/属性不重复创建。默认只显示已创作轨道和显式选择的对象，原可用属性显示选项仍保留。

**更换对象**：父对象行提供入口，复用同一定位器；一次更新该组轨道目标，保留帧、值、插值、切线与未知字段，一次撤销整体恢复。候选需支持全部已有属性，且不产生重复目标/属性；不静默删轨道或合并数据。新增/替换取消旧手势，提交检查原目标身份和作者版本。

**验证**：普通 DTO 字段与 Timeline 共用组件/查询；同资源多实例、跨 Group、两类存在性、搜索/四语、未解析引用、生产者删除/重排/复制、候选更新/过期确认；真实顶部“＋”选择对象/对象行“＋”选择属性/取消/更换对象/重复防护、单历史及保存重开。按变更运行 Editor/Shared/Presentation/Architecture/Integration 回归和解决方案构建。

**风险与退出**：避免把资产文件当作场上实例，或仅换输入框外观仍要求作者理解 ID。至少一个普通 Entry 字段和 Timeline“＋”/更换对象完成真实选择→提交→撤销→保存重开的同一服务闭环；共享层无 Editor 依赖，旧引用无静默丢失，并有验证证据。

后续顺序：P6e2b → P6e3 曲线/切线 → P6e4 事件/剩余语义 → P6f animate 曲线 → P7 验收。实时游戏预览不纳入。

## 实施与验证记录

本阶段扩展既有 ProjectSceneTargets 查询入口，而不是新建第二套分析：查询接受 Group/稳定 Entry 位置，null 查询 Group 末尾；缓存当前作者快照，项目 scope 释放时使旧选择过期。SceneTargetCatalog 适配显示候选、语义类型筛选、资源名称和定位器解析；SceneTargetSelection 是短期选择上下文；SceneTargetPicker 仍是控件，不提供弹窗调度服务。用户对“服务”的澄清已经纳入此边界。

原 Assets Picker 改用共享 ResourcePickerPanel，保留资产类型筛选、搜索、文件信息和选择结果。普通内置 DTO 目标字段通过成员注册选择控件，创建者输出从生成表单隐藏；未解析引用不因加载/刷新丢失。空目标与 effect.apply 的全局目标分别显示，语言切换按稳定语义 key 更新标签，不通过比较翻译文本决定行为。

Timeline 对象选择后不立即写作者数据，对象行“＋”逐项添加属性。首个已知属性产生首帧和当前帧键；未知初值行保持草稿，输入后才提交。父对象更换目标仍一次处理所有已有轨道，拒绝重复属性并保留所有键及未知 JSON。原显示可用属性模式仍保留。

聚焦验证覆盖资产实际按钮打开、共享搜索/键盘确认/空值/Escape；位置查询、同资源多实例与两类存在性、保存不换代、修改/撤销/关闭失效；普通字段未解析提示、单历史、过期确认、语言切换；Timeline 实际“＋”选择对象、对象行“＋”属性列表、更换对象、数值控件、四语草稿、逐项撤销/重做、未知值草稿、取消、换帧失效、保存重开以及切线/未知 JSON 保留。初轮 5 项聚焦和六项目 905 项回归通过；用户澄清两步交互后的 3 项聚焦通过，最终门禁结果待下方记录。

过程修正：首次关闭项目回归暴露仅依赖 IsLoaded 不足以使旧查询失效，已通过服务自身 Dispose/代次解决，见 .agents lesson L-20261018-04。实际更换对象按钮回归暴露打开弹窗前重建树会移除锚点，现改为取消输入而保留节点挂载，真实入口重跑通过。早期测试直接引用内部 TimelineDocument 的编译错误已改为通过公开编辑会话验证，未扩大生产可见性。搜索文本变化的测试等待 UI TextChanged 处理后再发方向键，不把测试中的同步属性赋值当作完整输入事件。

## 最终结果

初轮六项目回归共 905 项通过（Editor 173、Shared 223、Core 111、Architecture 45、Integration 110、Presentation 243），解决方案构建与格式/源码指纹通过。用户随后明确拆为两步选择，本次最终改动限定在 Editor 对象/属性交互与相关 i18n；共享查询分析、资产面板与运行时没有新增语义变更。按修订范围重新执行解决方案构建、Editor/Architecture/Presentation 全量回归，共 461 项通过（173/45/243），0 失败；whitespace/IDE0005 verify 及全部变更源文件 SHA-256 一致。6 项需要原生 LibVLC 的显式用例未执行，不算通过。

最终构建 36 条分析器/既有警告、0 错误，四语各 691 keys。Editor 真实 Semi 主题测试覆盖两级“＋”入口、列表选属性、初值已知/未知、单属性单历史、旧行/旧选择失效、整个对象替换和保存重开。截图 scene-target-select.png 与 scene-property-menu.png 已检查，选择器仅含对象搜索/卡片，对象行左侧按钮显示属性菜单，无原对象/属性合并表单。

证据目录：`C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation`。初轮脚本/结果为 validate-locator.ps1、locator-GalNet*；最终脚本 validate-locator-final.ps1，构建 locator-final-solution-build.log，TRX/日志 locator-final-GalNet*，指纹 locator-final-source-sha256.json，格式 locator-final-format-verify.log。两步交互聚焦结果 locator-two-step.trx。

正式行为已同步 [场景查询](../../docs/spec/editor/scene-targets.md)、[Group/Timeline](../../docs/spec/editor/groups.md)、[共享控件](../../docs/spec/presentation/game-view.md) 和 [i18n](../../docs/spec/content/localization.md)。P6e2b 退出条件满足；P6e3 曲线/切线、P6e4 剩余语义/事件/引用、P6f animate 曲线和 P7 仍未完成；没有实现实时游戏预览。
