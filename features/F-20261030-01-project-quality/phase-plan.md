# 项目质量与性能优化计划

状态：执行中，按阶段交付。创建日期：2026-10-30。

## 目标与执行约束

来源：项目分析及用户授权“写入计划然后一步步来”；代码应简洁、清晰、可复用、少冗余、职责明确。

- 每次完成一个可独立验证的阶段，记录实际改动、验证结果及剩余工作。
- 2026-10-30 用户补充：每阶段验证完成后提交本任务改动，再进入下一阶段；不混入并行 Sample/文档工作。
- 优先修复正确性；性能改动先测量；职责重构与行为变更分开提交范围。
- 复用已有机制；只有真实重复或独立职责才提取协作者，不为了缩短文件引入转发层、通用框架或重复状态。
- 保持 Core/Runtime 与平台实现的边界、资源所有权、取消语义、作者保存语义及内容完整性校验。
- 当前 Sample/跨平台迁移由其他工作进行，保护已有改动；每阶段开始重新检查工作树。
- 用户将正式文档整理同步留到之后。本任务只维护本计划及必要验证/经验记录，不修改 docs/spec、docs/manual；文档待同步不阻断已授权实现。
- 未测量的性能收益及尚未选择的设计仅是候选，不作为已确认结论。
- 2026-10-30 用户明确暂不考虑 10 GB 大包容量保障，继续原质量计划；流式大包/超大单资源支持延期，不插入 P4 前。

## 阶段总览

| 阶段 | 目标 | 前置条件 | 状态 |
| --- | --- | --- | --- |
| P1 | 资源并发获取与释放正确性 | 已复现等待者尚未取得句柄时资源提前释放 | verified |
| P2 | PAK 解压输出边界 | P1；核对同步/异步解压使用方 | verified |
| P3a | BenchmarkDotNet 资源读取/导出基线 | P2 | verified |
| P3b | PAK 读取优化 | P3a 数据与完整性约束 | verified |
| P3c | PAK 导出优化 | P3a 数据、P3b 共享格式边界稳定 | verified |
| P4a | 编辑器 dirty/恢复快照基线 | P3；确认编辑线程与保存锁契约 | verified |
| P4b | dirty 参数比较分配优化 | P4a 测得每条目额外分配 | verified |
| P4c | 编辑器恢复持久化优化 | P4a 测量和生命周期设计；P4b | verified |
| P5 | 持久化字段比较完整性 | P4 的基线设计稳定 | verified |
| P6a | GameEngine 职责整理 | 前述行为稳定；确认状态与锁所有者 | verified |
| P6b | 内容会话协调职责整理 | P6a；单独评估发布/重载边界 | verified |
| P7 | 资源错误诊断贯通 | 资源加载职责稳定 | verified |
| P8a | 后端 PR 回归与运行证据 | P7；复用既有后端测试与 Headless 自检 | verified |
| P8b | 发布后应用运行验证 | P8a；与跨平台发行/原生验收协同 | planned |

## P1：资源并发获取与释放

模块：AssetManager、Assets 测试。

已确认问题：多个 Acquire 共享同一加载；首个句柄释放时，RefCount 可归零，但其他等待者的 continuation 尚未领取引用。当前释放逻辑忽略 PendingWaiters，导致提前销毁，后续等待者返回 null。

实现原则：复用现有 CacheEntry.Owner/PendingWaiters，将等待领取的请求纳入存活条件；统一无引用资源的移除判定，仍在锁外 Dispose。不新增公共 API、第二套引用计数或缓存策略。

验证：用可控调度顺序覆盖 ID/路径获取、首个句柄提前释放、最后等待者取消、manager 销毁；断言仅解码一次、未提前释放、最终恰好释放一次。先证实回归测试在旧实现失败，再运行 Assets 全量和架构门禁。

风险：遗漏取消后的最终清理或重复释放。退出条件：上述时序均成立，现有资源/覆盖/媒体实体化测试通过，公共契约不变。

## P2：有界解压

模块：CompressionHelper、Archive、相关格式/压缩测试。

任务：核对调用方；基于条目 OriginalLength 在解压过程中限制输出并支持取消，防止完成后才拒绝超限数据。避免复制一套压缩算法或为单个场景引入通用流框架。

验证：正常包、空数据、声明过小/过大、截断、高压缩比数据、取消；确认越界时提前停止并保留完整性拒绝行为。

风险：既有公共 API 与无长度调用方行为。退出条件：边界测试和现有格式回归通过，记录内存边界及无法承诺的限制。

实现决定（2026-10-30）：现有无长度 byte[] 解压入口保持兼容；新增接收 Stream 和精确预期长度的同步/异步入口，分块读取并检查长度，缓冲按实际输出增长且不超过预期长度，不按不可信声明预分配整块内存。Archive 直接借用数据区流并保留 SHA-256 校验。IArchive 增加带取消参数的默认实现重载，保留原成员，Archive 覆写后由资源管理器/覆盖 provider 传递取消；已有第三方实现可继续使用默认重载。PAK 格式不变。声明长度并非进程总内存预算，旧无长度 API 不获得长度上限。后续 P3 可使用 BenchmarkDotNet；P2 以确定性边界测试验收，不以耗时阈值代替正确性。

## P3：PAK 访问与导出

2026-10-30 执行拆分：将测量、读取、写出拆成三个独立可验证、可提交阶段。P3a 交付锁定版本的基准工程、夹具自检、基线证据与优化决策；不宣称有生产性能提升。P3b/P3c 分别实施并与同口径基线比较。这样能在行为变化前保留可信的测量起点。

P3b 设计决定：优先消除已测得的 provider 输入二次复制。新增仅程序集内部可用的缓冲所有权转移入口，provider 交出其新分配、未共享的数组；公共 Deserialize 继续复制数据区以隔离调用方后续写入。每个 archive 独占快照，源文件随后替换不影响已打开 archive，下次打开仍整包校验；不新增缓存/文件句柄生命周期或公共 API。非 seek stream 继续读完并关闭其自有来源。按偏移直接读和共享索引暂缓，因为完整性约束仍要求读取整包，当前先用小改动验证分配收益。

P3c 设计决定：保留现有单次读取/压缩及严格验证，先准备表头与有序数据块，再由同一输出对象写入流。byte[] 导出准确分配最终数组，文件导出直接写文件，去掉整包 MemoryStream 扩容及 ToArray 复制。所有源内容和元数据校验先完成，才打开/截断目标文件；不改变文件写入失败时原有的非原子语义，不引入临时发布框架或基础设施反向依赖。该切片仍保留全部压缩条目，不承诺恒定峰值内存；编辑器当前使用 byte[] 导出，也会受益于最终缓冲改进。单条目流式压缩、编辑器输出流迁移另需测量，当前不扩展范围。

模块：Archive、PakFileProvider、PakStreamProvider、InstalledPackageSet、资源实体化和导出路径。

任务：先记录固定样本的冷/热读取耗时、读取字节数、分配量、峰值内存与导出指标；分别实施读取和写出两个可验证切片。候选为共享不可变索引、按偏移读取、有生命周期的包访问及直接写流。选择方案前明确文件替换、文件句柄、不可 seek 来源和 hash 校验策略。

验证：包内小资源重复加载、多个资源并发、覆盖映射、源包变化、取消、非 seek 输入、导出字节确定性及加载/安装往返。

风险：缓存不能削弱会话冻结/源完整性，不能产生无界常驻内存。退出条件：测得改善且不破坏上述语义；没有收益的抽象不保留。

## P4：编辑器大项目响应

执行拆分：P4a 独立提交可重复基准与夹具自检，分别测 dirty、克隆、序列化、重复恢复和变更恢复。P4b 根据数据处理一致快照捕获与后台写入、请求合并和关闭排空；先确认失败/关闭恢复语义，不把后台操作直接塞进现有同步 Capture。源码显示 dirty 已做结构比较，恢复目前同步执行克隆、序列化/hash、写盘与裁剪，周期任务在此期间持有 capture guard。P4a 不改变生产代码或已知保存行为。

P4a 测量后修订：dirty 查询在 1,000/10,000 条目时分别分配约 63/626 KiB，随条目数线性增长。先以 P4b 小切片验证 Entry 参数经 IReadOnlyDictionary 枚举产生的分配，仍逐项比较而不加缓存；原恢复持久化切片顺延为 P4c，与行为保持优化分开提交。

P4c 实现选择：定时恢复只在 history capture guard 内克隆工作版，释放后等待后台串行持久化；沿用 PeriodicTimer 在等待时合并 tick，不新增定时快照队列。一个内部 writer 拥有接受顺序、完成错误和停止/排空；worker 仅接触脱离工作版的快照，不调用 UI 或 history。同步失败/结构/重载路径仍等待写完，保留既有返回时恢复材料可用的行为；关闭停止定时请求、排空并异步等待最后快照。直接 Dispose 停止接受并等待纯后台工作，不同步等待 UI continuation。新增确定性阻塞 writer 测试及同口径调用线程耗时基准，不把总写盘耗时误报为 UI 改善。

模块：ProjectContentSession、ProjectRecoveryStore、ProjectContentBaseline。

任务：测量大型 Graph/Group/文本文件的 dirty 查询、快照捕获、序列化和写盘成本；将一致快照捕获与后台串行持久化分开，合并重复恢复请求。按测量决定是否增加文档级比较缓存，不仅凭事件/历史游标推断 dirty。

验证：保存期间继续编辑、撤销回到基线、重复恢复请求、退出等待、写盘失败、外部重载；记录主线程占用和分配变化。

风险：快照不一致、过期完成覆盖新基线、后台任务未排空。退出条件：原有保存/关闭语义不变且基准改善。

## P5：比较契约完整性

模块：作者持久化 DTO、ProjectContentBaseline、保存状态测试。

任务：建立每个持久化字段改变均影响 dirty 的契约测试，并覆盖未知 JSON、仅单字段变化和局部保存。根据真实维护成本选择集中字段描述或生成比较；不默认引入 generator。

P5 实现选择：在测试侧枚举序列化元数据，对作者 DTO 的每个字段单独修改、还原并检查克隆；新增字段必须自动获得验证或明确补充夹具/变更规则。自定义转换器另验实际 wire 字段与变量类型，显示行号、已删除文件载荷等例外有独立断言。保留生产手写结构比较，不增加运行时反射、序列化或 generator；仅在契约测试发现实际遗漏时修复生产行为。

验证：每个字段单独变化、还原后 clean、局部保存保留其他未保存改动，新增字段遗漏能被测试检测。

风险：将缓存/瞬时 UI 字段误计入内容。退出条件：比较契约与持久化范围一致，不增加运行时全量序列化。

## P6：职责整理

模块：先单独评估 GameEngine 的播放/调试，再评估 ProjectContentSession 的发布/重载协调。

P6 拆分：P6a 单独提交 GameEngine 的职责评估与入口缓存提取，P6b 再评估内容会话。播放计时依赖 active/blocker、推进边界、读进度和 stop/revision，本轮保留在同一执行门下，避免复制状态或引入反向调用协调器。调试入口缓存将快照、payload 大小、LRU 使用序号合成单个缓存项，由内部 DebugEntranceCache 统一拥有预算、替换、命中和淘汰；DebugSession 保留捕获时机、回放统计、路线与代次。沿用 _dispatchGate，不增加锁、后台任务或公共 API。先增加公共调试 API 的行为测试并在旧代码运行，再实施重构。

任务：画清状态、锁和资源所有权；只提取独立策略/任务生命周期，保留一个权威状态入口。每个模块单独完成行为保持重构，不以文件行数为验收指标。

P6b 实现选择：内部 ProjectDiskBaseline 统一拥有会话接受的磁盘字节、settings 检查基线与契约指纹，负责首次读取、受管文件登记、重载采纳、成功发布后的写入/复制/删除更新。作者内容与 dirty 仍由 ProjectContentBaseline 管理；Repository 保留 Graph/Group 事务源校验，受管文件期望仍从保存作者快照产生。会话继续拥有保存/重载/补偿顺序、实时文档身份、历史捕获与发布门、恢复关闭生命周期；不新增公共 API、锁、回调或第二份状态。补偿只更新 settings 校验基线，保留既有外部观察语义。本阶段为行为保持重构，不宣称性能收益；新增公共行为测试先在旧实现验证。

验证：执行/快进/自动播放/调试恢复/停止交错，保存/外部重载/关闭交错及对应架构门禁。

风险：协作者间循环引用、共享状态复制、锁顺序变化。退出条件：职责可说明、依赖更简单、行为测试通过且无无效转发层。

## P7：诊断贯通

模块：AssetManager 诊断边界、宿主日志/调试展示。

任务：保留资源 ID、来源、阶段、原异常；用最小注入边界接入宿主日志，区分缺失、校验失败、解码失败，保持现有显示回退。

P7 实现选择：AssetManager 暴露可选结构化诊断事件，Assets 不引用宿主/日志库，IAssetManager 不扩展。查找结果内部携带 provider；覆盖资源额外携带实际 PAK provider 和源资源 ID。诊断区分查找、读取、解码、枚举阶段，以及缺失、无效数据、provider/读取/解码失败和缺少 decoder；保留原异常实例。共享 producer 只报告一次，provider 异常后不追加误导性缺失；独立重试仍可报告新的尝试，不建立无界失败缓存。监听回调在缓存锁外且逐个隔离异常；取消不报告，未订阅时保留 Trace 回退。编辑器各代 manager 和 Avalonia 播放器资源会话连接现有 Serilog 日志/调试视图。作者索引和启动内容加载原有直接抛错链路不改为可恢复加载。

P7 验证后修订：新增按路径的共享失败测试确定性复现同批请求产生两次 DecodeFailure。原因是路径缓存命中后，任何空句柄都触发 ID 映射删除/重查，包含已经找到文件但读取/解码失败的情况。共享 completion 改为返回“是否找到文件”（资源仍由原缓存拥有），路径分支只在未找到文件时重查；保持最后等待者/引用释放规则。不增加失败缓存或重试框架，显式后续请求仍可重试。本阶段因此包含这一经测试发现的行为修正。

验证：不同失败原因可观察、取消不误报、一次失败不重复刷屏、无监听方可工作。

风险：诊断回调干扰加载或引入反向依赖。退出条件：故障原因可追踪，层次与回退契约不变。

## P8：持续验证

模块：backend/application portability workflows、相关验证脚本。

P8 执行拆分：P8a 将手动后端矩阵接入相关路径 PR，复用现有 Core/Assets/Runtime/GameHost/Storage/Audio/Architecture 测试及 Headless 自检，输出逐步日志、TRX 计数和 passed/failed/not_run 状态；缺失依赖、零测试或缺失结果不算成功。原生音频设备 Explicit 测试及 GUI/实机运行保持明确未验收。P8b 单独推进应用发布后启动/内容读取/正常退出，与并行跨平台工作衔接；本轮不将 raw SDK 发布视为 GUI 运行。两个入口共用已有受控进程执行逻辑，避免复制进程树取消实现；性能基准仍仅保留证据，不加耗时门禁。

任务：将稳定后端回归接入相关路径 PR 检查；在已有发布矩阵上逐步补充真实启动、资源读取、正常退出。性能基准先保存证据，再按稳定性决定门禁，不盲设耗时阈值。

验证：工作流触发范围、Linux/Windows 可重复执行、缺失依赖/未运行明确报告。与并行跨平台工作核对，复用其既有验收。

风险：原生环境波动和 CI 时长。退出条件：发布成功与运行验证结果明确区分，关键后端变化自动获得回归证据。

## 执行记录

- 2026-10-30 P8a verified：后端矩阵接入相关路径 PR，复用七个测试工程和真实 Headless 自检；输出逐项状态、日志、退出码、TRX 执行/未执行数与原生库 hash。提取共用进程执行器，修正清理失败后的无限等待；架构测试规范化 ProjectReference 分隔符，修复 Linux 的 21 项误报。冻结源码 Windows 639、Linux 635 项测试通过，两端 Headless 退出 0；各自明确记录 2/6 个未执行用例。入口保护 Windows 18 项通过、Linux 17 项通过/1 项 Windows 专属跳过。证据和限制见 [p8a-backend-regressions.md](p8a-backend-regressions.md)。GitHub/macOS runner、默认 CMake 分支及 GUI/设备/NativeAOT 未在本轮实际验收；P8b 发布应用运行验证仍 planned，正式 docs 后续整理。

- 2026-10-30 P7 verified：AssetManager 增加独立于日志库的结构化诊断事件，保留阶段、分类、逻辑 ID/路径、实际 provider、覆盖源 ID、目标类型及原异常；编辑器各代资源 manager 和 Avalonia 玩家会话接入现有日志，Debug store 验证可显示完整异常。取消不误报，观察者逐个隔离并在锁外调用；provider 错误不再追加缺失。路径并发测试发现空句柄误触重复加载，改用共享完成结果的“是否找到文件”区分身份失效与读取/解码失败，真正失效仍重查、后续显式请求仍可恢复。新增 27 项用例；Assets 248、Architecture 53、Editor 资源身份 8、Release 宿主/安装集成 18、Debug 日志 3 项通过。证据、配置修正与范围见 [p7-resource-diagnostics.md](p7-resource-diagnostics.md)，经验见 `.agents/lessons/L-20261030-09-asset-path-failure-retry.md`。下一阶段 P8 持续验证补齐，正式 docs 后续整理。

- 2026-10-30 P6b verified：提取内部 ProjectDiskBaseline，统一拥有 settings 检查、契约指纹和外部文件字节的登记/比较/采纳规则；ProjectContentSession 保留发布、重载、补偿、工作文档身份与原有锁/恢复生命周期。作者 dirty 基线、Repository 源校验和受管文件期望来源保持原职责，无公共 API 或锁变化。新增 5 项公共行为测试，重构前外部重载 13/13 通过；重构后 Editor.Shared 613、Architecture 53、界面外部重载/文件操作 20 项全部通过。所有权、取舍与证据见 [p6b-content-session-responsibilities.md](p6b-content-session-responsibilities.md)。P6 当前两个切片完成；下一阶段 P7 资源错误诊断贯通，P7/P8 仍 planned，正式 docs 后续整理。

- 2026-10-30 P6a verified：完成 GameEngine 状态/锁/生命周期梳理；播放协调继续由现有执行门管理。入口缓存从 DebugSession 的三张同键字典收拢为内部 DebugEntranceCache 的单张缓存项字典，统一拥有快照、payload、LRU、预算与统计；回放计数留在定位流程。无公共 API、锁或任务生命周期变化。新增 7 项公共调试行为测试，重构前后 DebugSession 24/24 通过；Runtime 131、Debugging 10、Architecture 53、内置自动播放/指令分发集成 29 项全部通过。设计取舍、证据与限制见 [p6a-engine-responsibilities.md](p6a-engine-responsibilities.md)。下一阶段 P6b：内容会话发布/重载协调职责评估；P6 尚未整体完成，P7/P8 仍 planned。

- 2026-10-30 P5 verified：新增 87 项字段/克隆/集合/未知 JSON/局部发布契约测试；临时新增未填充可选字段和移除 StoryPackage 比较的两个反向探针均正确失败，探针已撤销。发现 ProjectVariableDefinition.Clone 隐式同步名称绕过作者校验，并使保存成功后仍 dirty；改为复用 Variable.Clone 原样捕获，名称不一致时正常拒绝发布且恢复保留完整草稿，修正后可保存并 clean。生产代码净减少 16 行，不引入运行时反射/序列化/比较缓存。Editor.Shared 608、Core 139、Architecture 53、变量面板定向 4 项全部通过。证据和边界见 [p5-content-contracts.md](p5-content-contracts.md)。下一阶段 P6：先评估 GameEngine 协调职责，再处理内容会话；P6–P8 尚未实现，正式 docs 留后同步。

- 2026-10-30 P4c verified：作者线程克隆后交给内部串行 writer；周期恢复释放 history guard 再等待，沿用 PeriodicTimer 合并 tick；关键同步恢复保留返回前完成，关闭/Dispose 排空且 worker 不回调 UI。新增六项恢复生命周期测试，Editor.Shared 521、Architecture 53 项通过，两规模夹具自检通过。专项修改前 2/2、修改后 4/4 基准成功；1k/10k 调用线程同步部分均值从 9.037/41.082 ms 降到 0.596/2.720 ms。同步完整恢复本轮为 8.278/45.579 ms，高于历史 5.853/42.528 ms；不宣称同步路径或总分配改善。单次 iteration 警告、分配计量边界及原始证据见 [benchmarks/editor-recovery-notes.md](benchmarks/editor-recovery-notes.md)。P4 已完成当前三个切片；下一阶段 P5 持久化字段比较契约，P5–P8 尚未实现。正式文档仍由用户后续整理。

- 2026-10-30 P4b verified：Entry 参数改为直接遍历具体 Dictionary，避免接口枚举器每条目装箱；身份、参数数量、键查找与 JsonElement.DeepEquals 保持不变，没有缓存/额外 dirty 状态。收紧既有 500 Entry × 20 次读取分配测试至 30 KB，旧实现 654,080 B 失败；修正后 Editor.Shared 515、Architecture 53 项通过，基准两尺寸自检通过。六项 dirty 基准全部成功，10k 完整查询分配从约 640,760 B 降至 760 B，末尾参数变化为 704 B，分配不再随 Entry 数量增长；耗时仅小幅变化，不宣称数量级加速。报告见 [benchmarks/editor-baseline-notes.md](benchmarks/editor-baseline-notes.md)。下一阶段 P4c 恢复持久化仍 planned，P4 尚未整体完成。

- 2026-10-30 P4a verified：独立 Editor.Benchmarks 工程锁定 BDN 0.15.8；1k/10k Entry、20 Group、约 1/10 MiB 文本，共 16/16 基准成功，退出码 0。两规模自检确认直接晚修改/还原、克隆隔离、恢复去重/往返；最终构建零警告/错误、locked restore 通过。10k 场景 dirty clean 2.815 ms / 625.74 KiB，克隆 5.607 ms，重复恢复 13.427 ms，session 完整恢复 42.528 ms / 约 67.8 MiB 累计分配。证据及限制见 [benchmarks/editor-baseline-notes.md](benchmarks/editor-baseline-notes.md)。无生产代码变更；下一切片先验证 dirty 枚举分配，恢复后台持久化随后处理。未把基准线程耗时当作真实窗口帧延迟。

- 2026-10-30 P3c verified：Archive 统一准备有序表头/数据块，byte[] 导出只分配准确大小的最终数组，BuildToFile 直接写数据块；全部源读取/内容/元数据验证在目标打开前完成，保留原写入访问/共享方式。没有公共 API 或压缩算法变化；仍保留压缩条目集合，不承诺恒定内存或原子发布。13 项新测试先在旧实现通过并采集无压缩格式 SHA256，改动后 Assets 223、Architecture 53、导出/安装集成 9 项通过，两尺寸基准自检及零警告/错误构建通过。初次集成构建因 Avalonia 遥测日志权限失败，按既有 lesson 设置 AVALONIA_TELEMETRY_OPTOUT=1 后成功。
  - 同口径导出基准 4/4 项成功，16 MiB 波动复测 2/2 项成功；内存导出分配减少约 24–25%，文件导出减少约 37%。64 MiB 均值下降；16 MiB 内存导出两轮均值 83.30/71.16 ms，仍高于历史 64.62 ms，因此只确认分配收益，不宣称小包更快。全部数据及限制见 [benchmarks/README.md](benchmarks/README.md)。本轮不重复测未改动的读取路径；P3 完成当前三个切片，按偏移读取/单条目流式写出等候选暂缓。下一阶段 P4：编辑器恢复快照与 dirty 成本测量。

- 2026-10-30 P3b verified：Archive 通过内部 DeserializeOwned 接收 provider 新建数组，保存数据区偏移/长度，复用唯一解析实现；公共 Deserialize 继续复制，未新增公共 API/缓存/持久文件句柄。新增 8 个快照/完整性回归组合，Assets 210、Architecture 53 项通过；两种尺寸夹具自检及基准构建通过。4/4 个读取基准成功，单条目分配约减半、四条目减少约 44.4%；64 MiB 均值由 44.296/246.320 ms 降到 37.887/184.961 ms，16 MiB 四条目则由 46.184 升到 49.743 ms，不宣称全场景更快。完整数据、限制与日志索引见 [benchmarks/README.md](benchmarks/README.md)。下一阶段为 P3c 导出；P4–P8 尚未实施。

- 2026-10-30 P3a verified：新增独立 BenchmarkDotNet 0.15.8 锁定依赖工程，固定 16/64 MiB 夹具，六种操作共 12 项成功。Release 构建零警告/错误，夹具内容往返/确定性自检通过。基线与复现说明见 [benchmarks/README.md](benchmarks/README.md)。64 MiB 小资源读取分配 134,344,353 B；下一步 P3b 优先去掉 provider 独占输入的二次复制，保持每次整包 hash 与公开输入快照语义。P3c 另行处理约 532 MB 的导出分配。当前未测峰值内存/读取字节数/冷磁盘，不宣称生产优化已完成。首次受限子工程构建失败的 NA 报告已排除，入口增加失败退出码检查；完整复跑 12/12 成功。

- 2026-10-30：创建计划。分析阶段架构测试 53 项、Assets 测试 103 项通过；隔离调度探针复现 P1。正式文档同步明确延期给用户处理。
- 2026-10-30 P1 verified：复用 CacheEntry.Owner.PendingWaiters，并由 DetachUnusedAssetLocked 统一取消/句柄释放后的移除条件；未新增公共 API、状态计数或缓存策略。四个确定性调度用例覆盖 ID/路径、提前释放、最后等待者取消及 manager 销毁，旧实现 3 失败/1 通过，修复后 Assets 全量 107 项、架构门禁 53 项通过；限定改动 diff whitespace 检查通过。原有测试分析器警告保留，本次改动文件没有新增报告。测试命令使用 Release、--no-restore、-m:1、-nr:false、-p:UseSharedCompilation=false。
  - 本地证据：`out/project-quality-p1-before.log`、`out/project-quality-p1-assets.log`、`out/project-quality-p1-architecture.log`（out 为忽略目录）；可复现用例已进入 `test/GalNet.Assets.Tests/AssetPipelineTests.cs`。
  - 本阶段结束时下一阶段为 P2；未运行全量 UI/原生平台测试，未宣称性能收益。
- 2026-10-30 P2 verified：新增 Stream + expectedLength 同步/异步解压入口，复用缓冲大小、追加和完成校验。按实际输出增长容量并限制到声明长度，最多读取一个额外解压字节识别越界；异常/取消均归还池缓冲且不关闭调用方的源流。Archive 直接借用原数据区读取，仍校验 SHA-256，PAK 格式不变。保留旧 CLR 方法签名；IArchive 默认取消重载兼容旧实现，AssetManager、资源覆盖及包 provider 传递取消。
  - 验证：新增 85 项解压边界组合、8 项实际 PAK 长度篡改、1 项 PAK 取消与 1 项旧接口实现回归；Assets 202、Core 139、Architecture 53 项全部通过。定向 diff whitespace 检查通过。证据：`out/project-quality-p2-assets-final.log`、`out/project-quality-p2-core.log`、`out/project-quality-p2-architecture.log`。
  - 首轮测试因测试替身派生 MemoryStream 的读取重载递归导致栈溢出中止；修正为组合后通过。失败与修正证据保留，经验见 `.agents/lessons/L-20261030-04-stream-test-double-overloads.md`。
  - 边界：输出累积缓冲受单条目声明长度限制，输入包、解码器内部缓冲和最终返回数组不计入此预算；真实大型条目仍可能消耗大量内存。旧无长度解压 API 保持原行为；旧 IArchive 实现的默认取消重载只能在调用前检查，需实现重载才能在内部工作期间响应。未执行旧二进制 consumer 动态装载或原生 UI 验收，不宣称这些已验证。
  - 下一阶段：P3，先使用 BenchmarkDotNet 建立资源读取/导出基线再选择优化；P3–P8 尚未实现。正式文档同步继续由用户后续处理。
