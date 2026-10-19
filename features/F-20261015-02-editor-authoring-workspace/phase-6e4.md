# P6e4：Timeline 事件与剩余状态语义

2026-10-19，前置 P6e3 verified。用户授权继续，按可验证切片推进；不加入实时游戏预览。

## P6e4a：事件能力与帧内场景查询

状态：verified（2026-10-19，保留以下已复现的基线布局失败）。本轮交付统一内置事件能力清单/校验入口、指定 Timeline 帧与事件前位置的纯场景查询、事件创建对象的 Locator、当前 Timeline 对象选择和属性初值接入。支持类型沿用现有十种运行时事件，不扩大执行授权。

- 按帧排序，同帧按作者数组原序处理；事件前查询只处理严格更早的帧和同帧前面的事件，当前帧轨道尚未采样。缺目标操作跳过，不虚构对象。
- 当前帧查询描述当前 Timeline 第一次正向行程，先事件后轨道纯采样；不运行游戏、不调用 presenter。事件前对于已跟踪属性不猜测上一显示采样值。Additive 与事件基值存在未覆盖组合时明确清除该属性唯一值并提示不完整。
- 已阻塞完成的 Once Timeline（以及零时长 Once）可向后续剧情提供终态事实；非阻塞、循环与 PingPong 向后续传播保守可能集合，不假定已播放完成。复杂播放生命周期仍待后续。
- 事件对象 Locator 使用所属 Group/Entry、输出槽位和事件身份（有 editorId 使用它，否则按声明输出槽位/不透明输出身份定位），不依赖数组下标；作者界面仅显示创建帧/资源/来源。尚未创建的候选明确显示未来帧、可能存在且没有当前数值。
- 属性新增的第 0 帧初值来自第 0 帧查询，当前显示值来自当前帧；不把中途事件之后的值误写为第 0 帧基值。未解析/不完整作者 JSON 保留，不因查询写回默认值。
- 查询缓存按作者快照代次失效；选择 lease 包含帧/事件位置，语言/缩略图刷新不得丢失位置。项目关闭、作者编辑/重排及帧切换使旧提交失效。

验证：能力清单和契约一致；十种状态事件覆盖、同帧顺序/事件前、未来创建、重建/缺目标、阻塞/非阻塞/循环、轨道纯采样与未知值；查询无写入、缓存/关闭/过期、实际选择器与初值/单历史/保存重开、四语。完成后提交 Git；P6e4 整体不因 a 完成而标记 verified。

### 验证过程与隔离范围

- 主工作区场景查询专项 17 项通过；实际对象选择器与 Timeline 专项 24 项通过。四语候选创建帧、未来/当前标签、Frame lease、查询不写历史、第 0 帧与当前帧分别取值、单历史撤销/重做和保存重开均有覆盖。
- 期间 HEAD 从 `fc06963` 推进到另一项 Group UI 提交 `59ce326`，该提交四语重复 `Entry.Parameter.z`，英文另重复 `Entry.Parameter.opacity`，使 EditorLocalizationService 初始化失败；首次 Editor 专项 23 失败/1 通过尚未进入业务断言。保留日志，移除重复定义后 24 项全部通过；保留新 Group 文案。此修复随本切片提交。
- 最终候选基于 `59ce326` 加明确清单内 13 个 C# 文件和四语小改动；不混入工作区游戏本地化、schema 或服务注册等未完成改动。独立默认契约 DLL、schema fixture 均由同一候选源码构建，不借用主工作区旧产物。
- 最终候选解决方案构建成功，125 警告、0 错误；六项目共 797 通过、2 失败、0 跳过：Editor 183/2，Editor.Shared 231/0，Core 111/0，Architecture 45/0，Primitives.Builtins 117/0，Integration 110/0。完整 Editor 包含本轮实际选择器与既有 Timeline 回归。未运行 Presentation 原生媒体用例。
- 两项失败分别为 `GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor`（尾部留白 0，期望 ≥40）与 `GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave`（窄面板宽度 482，期望 <480）。在同一隔离快照移除本轮 13 文件改动、还原 `59ce326` 并仅修复重复翻译键后独立复测，仍为相同 2 失败/0 通过；不是本轮引入，未修改 Group 布局或放宽断言。P6e4a 的 verified 仅指本切片，不宣称完整 Editor 绿灯；该布局问题仍待处理。
- 最终候选 whitespace/IDE0005 verify 通过，13 文件源码 SHA256 与主工作区一致；候选源码另存 archive，基线复测和清理均只操作已核对绝对路径的临时快照。
- 原始证据保存在本机 `C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation/`，前缀 `events-a-`、`timeline-events-`，含 base archive、owned 清单、构建/测试日志、TRX、源码 SHA256 和格式检查。

## P6e4b：事件作者界面

状态：planned，依赖 a。独立事件行、有限类型选择器、添加/移动/多选/删除和同帧排序；详情复用 DTO 生成面板，字段 Locator 使用事件前位置；新增创建者身份、raw patch 和未知 JSON 保留、每次手势单历史及草稿/过期保护。这里才交付事件的 UI 新增和编辑，不将查询入口冒称事件编辑器。

## P6e4c：剩余状态能力与引用

状态：planned。转场及其他未覆盖内置语义、effect metadata/flipbook、播放实例定位器、Graph/转场复制引用重映射，以及缺目标运行时独立核验。循环/并发动画的存在性和值保持保守，未经验证不提升为确定事实。
