# P6a：GameEngine 状态所有权与调试缓存

## 责任与锁边界

| 所有者 | 负责的状态/行为 | 同步边界 |
| --- | --- | --- |
| GameEngine / IGameRuntime | 引擎推进、运行状态、scope 取消；runtime 持有逻辑状态、变量与游标 | `_dispatchGate` 保护推进和恢复转换 |
| GameGraphNavigator | 会话内图结构与后继查找 | 引擎已有执行门；保持现有不可变图快照 |
| ActivePrimitiveRegistry | 活动实例、阻塞顺序与完成订阅 | `_dispatchGate`；实例通知调度回引擎 |
| GameEngine 生命周期 | 后台任务登记、Stop/Dispose 任务和终止请求 | `_lifecycleSync`；停止在释放执行门后排空后台及 seek 工作 |
| GameEngine.Playback / CatchUp | mode/options、计时代次、推进/阅读边界、追帧目标与信号 | 现有执行门与取消/代次检查；计时/追帧任务仍由引擎生命周期拥有 |
| DebugSession | 暂停、断点、代次、定位路线、捕获时机和回放统计 | `_seeks` 串行化定位请求；运行状态转换使用 `_dispatchGate` |
| DebugEntranceCache（本阶段新增） | 每 Group 最近入口快照、大小、LRU、预算与淘汰 | 仅在已有 `_dispatchGate` 下调用，不自建锁或任务 |
| EngineNotifications | 排队的状态、播放与 checkpoint 通知 | 执行门内捕获，释放后投递 |

现有导航、活动实例与通知已拥有独立职责。播放计时直接依赖 active/blocker、推进边界、已读标记与 stop/revision；此时提取“播放控制器”会要求回调或复制这些运行状态，收益不足。本阶段保留这部分协调关系，不以 GameEngine 文件行数为目标。内容会话的发布/重载在 P6b 单独处理。

## 实现

原入口缓存分散在 DebugSession 的多个 partial 文件，用快照、payload 大小、使用序号三张同键字典及总量/时钟共同维护。现在用单张字典保存 Entrance（Snapshot、Bytes、LastUse），由内部 DebugEntranceCache 统一执行捕获/替换、命中复制、LRU 更新、预算裁剪、清空及统计。DebugSession 不再直接操作缓存账目。

缓存接收同步快照工厂，调用完即释放该委托，不保存 GameEngine/DebugSession 引用；保留“先移除旧入口，关闭时不捕获，捕获超预算则不保留”的顺序。捕获仍由原 CaptureScene 生成，仅带逻辑场景并补齐内容身份；命中仍通过 SnapshotCopy.Clone 返回独立副本。初始场景、路线验证、变量保留和代次策略仍由 DebugSession 拥有。

回放指令计数与最近回放统计放回定位实现；普通外部恢复清空缓存和统计，调试重建保留自然入口。GameDebugCacheStats/GameDebugReplayStats 移到独立文件，命名空间、公开类型和成员保持不变。没有新增公共 API、反向依赖、锁、取消源或后台任务。

预算沿用原来 UTF-8 payload 字节加固定开销的算法，以及 256 Group / 64 MiB 默认值；它不是托管堆/媒体资源的内存上限。LRU 淘汰仍扫描有界字典，没有为当前规模增加链表、通用缓存框架或第二套索引。本阶段确认职责归属和行为保持，不宣称性能提升。

## 验证

新增 7 项公共调试 API 用例，重构前后均运行同一组 DebugSession 测试，24/24 通过。新增覆盖：旧入口命中后保护其 LRU 顺序；自然重入替换且计数不累加；命中后的工作版变化不污染缓存；普通恢复清空缓存和回放统计；两种独立零预算；精确容量边界及 payload 预算独立淘汰；超预算替换删除旧入口而保留其他 Group，最终总量回到零。

- Runtime 全量：131 项通过，含自动/快速播放、读进度、追帧、定位/停止/恢复交错和内容身份门禁。
- Debugging 全量：10 项通过，验证协议仍可消费缓存/回放状态。
- Architecture：53 项通过。
- 内置自动播放与引擎指令分发集成：29 项通过，filter 为 `FullyQualifiedName~BuiltinAutoPlaybackTests|FullyQualifiedName~GameEnginePrimitiveDispatchTests`；日志 `out/project-quality-p6a-integration.log`，构建时设置 `AVALONIA_TELEMETRY_OPTOUT=1`。

命令统一使用 Release、`--no-restore -m:1 -nr:false -p:UseSharedCompilation=false`。本地证据：`out/project-quality-p6a-debug-baseline.log`、`out/project-quality-p6a-debug-after.log`、`out/project-quality-p6a-runtime.log`、`out/project-quality-p6a-debugging.log`、`out/project-quality-p6a-architecture.log`。保留已有编译/分析器警告；新增测试有两处 CA1861 常量数组断言建议。没有原生窗口人工验证或峰值内存测量，正式 docs 仍留给用户后续整理。
