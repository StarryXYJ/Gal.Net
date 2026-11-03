# 开发调试内容热更新阶段计划

状态：planned。设计依据见 [design.md](design.md)。

依赖顺序：P1 → P2 → P3 → P4 → P5 → P6。每个 Phase 达到退出条件后单独提交相关代码、测试和文档，不混入工作区其他任务改动。

## P1 — 调试版本清单与影响分类（verified）

实现结果（2026-11-03）：调试准备改为发布 `Output/Debug/Revisions/<revision>` 不可变目录；清单记录内容/契约/作者身份、Graph 摘要、最终编译作者 span 与会话文件摘要。调试内容指纹纳入 Assets。纯分类器按稳定作者前缀区分 Direct、ReturnGroupStart 和 RestartGame，当前无法归属的 Graph/资源保持保守重开。

验证：`GalNet.Debugging.Tests` 16/16 通过；`ProjectContentBuilderTests` 5/5 通过；Debugging 与 Editor.Shared 定向构建成功（Editor.Shared 保留 29 个既有 analyzer warning，0 error）。

### 目标

构建可并存的不可变调试版本，并用纯数据准确判断修改属于直接应用、返回 Group 开头或重新开始。

### 前置条件

- 现有 `ProjectContentBuilder` 的冻结构建、作者 `SourceEntries` 和原子目录发布可用。
- `ResourceReferenceIndex` 能提供静态资源及间接依赖的作者使用位置。

### 涉及模块

- `GalNet.Editor.Shared`
- `GalNet.Editor.Abstraction`（仅必要 DTO）
- `GalNet.Debugging` 的中立清单/结果 DTO

### 任务

- 把调试输出改为 `Output/Debug/Revisions/<revision-id>` 不可变目录，保留初次启动和热更新同一加载路径。
- 扩展调试内容指纹以覆盖 Graph、Group、Assets、I18n、settings 和 Contracts 身份。
- 由同一作者快照生成调试清单：作者 Entry/span、Group/Graph 摘要、资源摘要、引用位置和完整性。
- 实现不依赖 UI/Engine 的纯差异分类器，支持稳定 ID 对齐、Composite span、零展开 Entry、新增/删除/调序和最严格等级合并。
- 对动态资源、会话级内容和不能可靠归属的 Graph 变化保守提升等级。
- 为版本目录增加会话外有界清理规则，不删除仍被 App 声明占用的版本。

### 测试

- 相同输入得到相同 revision、清单和摘要。
- 当前边界前后各种 Entry 修改的分类表测试。
- Composite、零展开、删除当前 Entry、跨边界调序和未知资源引用。
- 构建失败、作者签名在构建中变化、并发候选迟到时不发布。
- 路径链接、损坏资源和 Contracts 身份变化明确拒绝或提升。

### 文档

- 在本 Phase 记录清单格式和分类用例；暂不修改正式 spec。

### 风险

- 仅比较作者 JSON 会漏掉编译器或资源 metadata 变化；摘要必须来自最终构建载荷。
- 资源依赖不完整时误判为 Direct；未知必须保守处理。

### 退出条件

- 可同时保留两个完整调试版本并独立验证身份。
- 纯分类器覆盖 feature 的三类行为，所有不确定输入均不会被误降为 Direct。
- 本阶段测试通过并提交。

## P2 — 运行进度与 Engine 导航换代（planned）

### 目标

让 Runtime 在调试执行门内采用候选 Graph，同时保留当前活动实例和入口缓存，并正确拒绝旧操作。

### 前置条件

- P1 清单和分类 DTO 稳定。
- 候选 Graph 已由 App 外部准备并验证。

### 涉及模块

- `GalNet.Core`
- `GalNet.Runtime`
- `GalNet.GameHost`
- `GalNet.Debugging`

### 任务

- `GameSnapshot` v6 增加自然经过 Group；新游戏、快照、复制、恢复和校验保持一致。
- Engine 仅在自然 Group 入口更新经过集合；Seek/回放不污染记录，首期检测重复自然进入并禁用热更新。
- 将 `GameGraphNavigator` 调整为可离线构造、门内整体替换的不可变导航快照。
- 增加调试专用内容换代 API：复核 generation/位置、验证 primitive 实现、映射当前作者边界并提交候选。
- 实现 Direct 的未来 Group/当前后缀切换；活动 primitive 保持旧实例，下一分发读取新版。
- 实现 ReturnGroupStart 的候选当前 Group 暂存与提升，复用 `SeekGroupStartAsync` 和原入口缓存。
- 记录 RestartGame 状态；未来安全内容可以发布，但历史要求保持到新运行。
- 成功提交推进 generation，并使旧断点 span、Seek、CatchUp 和异步结果失效；失败保持完整旧状态。

### 测试

- Snapshot v6 的经过 Group、当前入口及恢复往返。
- 调试回放不新增经过 Group；重复自然进入按首期约束拒绝热更新。
- 当前 Entry 完成期间提交、活动 Composite、Choice/Branch 边界和 Group 末尾。
- 当前后缀映射正确，不重复已执行 primitive。
- 返回 Group 开头先提升候选，再用旧入口状态执行新版。
- 位置在准备期间变化、取消、停止、读档和旧 generation 请求均拒绝提交。
- 导航候选缺实现、坏边或坏 Group 时旧运行可继续。

### 文档

- 记录实际 API 和 Snapshot 格式变化，正式 spec 留到 P6 汇总。

### 风险

- `EntryIndex` 在活动 Composite 中已越过首个 primitive，必须按作者 span 而非单个索引分类。
- 内容提交与现有 Seek 同时发生可能互相等待；统一使用现有取消和执行门顺序。

### 退出条件

- 纯 Runtime/Debugging 测试证明三种影响状态及当前后缀换代行为。
- 不创建第二个 Engine，不破坏普通非调试执行。
- 本阶段测试通过并提交。

## P3 — Editor/App 构建与提交闭环（planned）

### 目标

在真实跨进程调试连接中完成候选构建、App 独立校验、Engine 提交及 Editor 源码映射切换。

### 前置条件

- P1 能产生不可变候选和清单。
- P2 能在 App 内提交已验证 Graph。

### 涉及模块

- `GalNet.Editor.Shared`
- `GalNet.Editor`
- `GalNet.Debugging`
- `GalNet.Avalonia.Debugging`
- 默认 Sample Developer 组合根

### 任务

- 协议升级并增加 `contentReload` capability、请求、结果和 inspect 中的 revision/影响状态。
- App 仅从安全的不可变目录打开候选，独立复算项目、内容和契约身份。
- 默认 Avalonia 调试适配器准备候选资源 scope，在 `GameSession` 命令门与 Engine 门按固定顺序提交。
- Editor 增加显式“构建并应用修改”；连续请求使用作者签名/请求代次淘汰旧结果。
- 仅在 App 成功提交后切换 `_sources`、`_positions`、`_spans` 和作者签名。
- 重新映射仍存在的断点；删除或不再展开的作者 Entry 给出明确结果。
- 请求超时后通过 inspect 的 revision 对账，不重复盲发。
- 将构建、验证、提交耗时和失败阶段写入既有有界 debug-launch 日志。

### 测试

- 真实 loopback Editor/App：未来 Group、当前后缀、当前前缀和历史 Group 四类更新。
- App 指纹复算不一致、契约变化、坏候选、断连和响应丢失。
- Editor 成功/失败/超时后的源码高亮、定位和断点身份。
- 连续快速构建乱序完成时只有最新作者签名可提交。
- App 在标题页、无运行、运行停止和运行切换时的能力与错误。

### 文档

- 在阶段记录协议版本、诊断字段和人工联调命令。

### 风险

- App 已提交而 Editor 未收到响应；inspect 必须能恢复权威 revision 和映射状态。
- 候选准备较慢时游戏继续推进；提交前必须复核边界并允许重新分类/重试。

### 退出条件

- Developer Sample 保持同一进程和游戏状态，能够实际执行新版未来剧情。
- 失败和超时不会留下 Editor/App 身份分裂。
- 本阶段跨进程及相关回归通过并提交。

## P4 — 待处理动作、顶部菜单与非模态提示（planned）

### 目标

把 ReturnGroupStart 和 RestartGame 变成清晰、持续、可执行的 App 行为。

### 前置条件

- P3 能向 App 返回并保持权威影响状态。

### 涉及模块

- `GalNet.Avalonia.Debugging`
- Sample Desktop/Overlay 调试 UI
- Editor 调试面板的状态展示

### 任务

- 为 Debug ViewModel 增加待处理影响、原因和动作可用性。
- 增加非模态 Toast/状态通知通道；不复用会阻断游戏的确认对话框。
- 顶部菜单显示“返回当前 Group 开头应用修改”或“重新开始游戏应用修改”。
- 返回动作调用 P2 的候选提升 + 现有 Group start Seek；入口不存在时明确降级为重新开始。
- 重新开始调用调试内容协调器：先提升最新候选，再创建新 Run；不重启 App、不清除玩家全局数据。仅剧情候选可走轻量 Run 替换，会话级候选由 P5 补齐完整资源重建。
- 多次修改合并最严格等级；动作后重新计算，仅在所有原因满足后清除。
- Editor 同步显示 App 权威待处理状态，避免只看一次 Toast。

### 测试

- ViewModel 状态、命令启用、菜单文案及非模态通知。
- ReturnGroupStart 成功、入口淘汰/缺失、执行中取消和失败保留状态。
- RestartGame 覆盖 ReturnGroupStart，新运行采用最新 revision 并清除要求。
- Desktop 标题栏菜单和嵌入 Overlay 两种宿主路径。

### 文档

- 更新阶段人工验收步骤和四语言资源键清单。

### 风险

- 仅 Toast 容易丢失；菜单状态必须是权威长期入口。
- “重启 App”和“重新开始游戏”文案容易混淆，命令和提示必须明确区分。

### 退出条件

- 用户无需回到 Editor 即可完成返回或新游戏动作。
- 提示不阻断游戏输入，待处理状态不会因窗口关闭或一次通知消失。
- UI 与集成测试通过并提交。

## P5 — 资源与文本代次（planned）

### 目标

使未来 Entry 的普通资源和文本读取采用新版，同时保证当前对象、媒体和文件租约正常结束。

### 前置条件

- P3 已能准备并提交完整候选 `GameResourceScope`。
- P4 已能处理需要返回或重开的分类结果。

### 涉及模块

- `GalNet.Assets`
- `GalNet.Storage.FileSystem`
- `GalNet.Avalonia.GameHost`
- `GalNet.Avalonia.Debugging`
- 内置图片、音频、视频和文本消费路径

### 任务

- 增加只在调试组合启用的版本化资源/文本门面，统一路由新 Acquire、文件物化和文本解析。
- 增加候选版本协调器；新 Run 必须从最新已发布候选取得 Graph/内容，不能继续读取初始化时固定的 `_resources.Content`。
- 外层 handle/媒体租约持有来源代次；退休代在全部交付和持有者释放后销毁。
- 新代复制解码器注册和诊断订阅；迟到加载不能写入新代缓存。
- 按 P1 资源使用位置分类普通字节变化；当前/历史引用提升相应影响等级。
- 会话级 catalog、预载纹理、效果程序、配置、人物和 Gallery 变化保持 RestartGame，不增加可变全局注册表。
- RestartGame 在 GameSession 命令门下释放当前 Run、提升候选 scope，重建 visuals/Gallery/text/project options，并按同一 profile 重新接入玩家服务后开始新游戏。
- 验证返回 Group 开头后旧场景释放、新场景采用新版资源。

### 测试

- 同资源 ID 的旧 handle 与新 handle 同时存在且内容分别正确。
- 图片、音频、视频文件租约跨提交，旧播放正常结束后退休代释放。
- 并发 Acquire、取消、解码失败、路径缓存和旧加载迟到。
- 未来资源 Direct、当前资源 ReturnGroupStart、历史资源 RestartGame。
- I18n 文本在未来 Entry 使用新版；会话级内容按设计要求重新开始。
- 多轮更新后代次数和内存/文件句柄有界，无泄漏或提前释放。

### 文档

- 记录首期可直接热更与需重新开始的资源类别。

### 风险

- 媒体物化文件寿命长于普通 handle；必须纳入同一退休代所有权。
- 预载和全局 catalog 被误当成普通按需资源会产生半新半旧状态。

### 退出条件

- 真实图片、音频和视频证明“当前继续旧版、后续加载新版”。
- 退休代在最后租约释放后可靠清理，重复更新资源占用有界。
- 本阶段测试通过并提交。

## P6 — 失败恢复、完整验收与正式文档（planned）

### 目标

完成竞态、生命周期、真实 Sample 验收和正式契约同步，形成可交付结论。

### 前置条件

- P1–P5 分别 verified。

### 涉及模块

- 全部上述模块、相关集成测试和文档。

### 任务

- 覆盖停止、读档、返回标题页、App 断开、项目关闭、连续保存和进程退出时的取消/清理。
- 验证 Snapshot v6、调试协议版本、普通非调试运行和发行 Player 不获得热更新入口。
- 执行 Developer Sample 真实窗口流程：直接应用、返回 Group 开头、重新开始、资源更新、失败重试和断点/高亮。
- 检查版本目录、资源退休代、日志和后台任务全部有界清理。
- 同步 Runtime/Editor spec、Editor 操作手册和必要的 Sample 扩展说明。
- 生成 `summary.md` 和独立 `review.md`，记录未执行的平台/媒体边界，不把子范围通过写成整体完成。

### 测试

- 受影响项目的单元、集成、架构和格式检查。
- 外部 Sample 真实进程与窗口测试；按风险选择原生媒体用例。
- 普通新游戏、存档/读档、调试 Seek/Step/Breakpoints 和资源加载回归。
- 构建失败、协议超时、App 崩溃恢复和旧版本目录清理。

### 文档

- `docs/spec/runtime/execution.md`
- `docs/spec/editor/debugging.md`
- `docs/manual/editor/preview-and-debugging.md`
- 若资源门面形成可复用正式契约，再更新相应资源/生命周期规范。

### 风险

- 平台媒体后端对文件租约行为不同，未执行的平台必须明确保留为未验收。
- 完整回归可能暴露既有环境失败，需区分本 feature 回归与已记录问题。

### 退出条件

- feature 的 10 条验收标准均有自动或人工证据。
- 完整构建及约定回归通过，或剩余环境限制有明确证据和用户决定。
- 正式文档、总结、review 与分阶段提交齐全，feature 可进入 closeout。

