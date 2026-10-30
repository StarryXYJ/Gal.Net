# 运行状态、快照与存档

## GameRuntime 与存档

`GameRuntime` 是游戏事实的唯一来源：

内容 provider 将作者 settings.json 或安装文档包内配置解析为 `GameContent.Configuration`（Core.Configuration.ProjectConfiguration）。它保存版本化 section ID 和 JSON，不携带固定游戏模型；宿主通过 GameContentLoadOptions 选择配置 schema 和文化投影。默认模板选择 Contracts.Builtins 的 DefaultProjectOptions；Editor 已按声明提供通用字段表单和配置草稿/历史。未知 section 与字段按文档契约保留，缺省值读取不自动写盘。

宿主把轨道定义显式传入 BuiltinAudioModule，把 PlaybackOptions 传给 Engine；GameRuntime 和 IGameContext 不携带完整设置或设置容器。未配置音频模块轨道时为空，不隐式创建默认轨道。

| 状态 | 说明 |
| --- | --- |
| `CurrentNodeId` / `EntryIndex` | 当前流程位置 |
| `IsGameEnded` | 结束标记 |
| `TextResolver` | 宿主提供的剧情文本解析器；默认原样返回 |
| `State` | 按注册类型/ID 存储的通用 GameState；契约可选择 keySpace 共享跨表身份索引 |
| 变量存储 | Player / Save 作用域变量与表达式求值 |

`GameSnapshot` 当前版本为 5，旧版本直接拒绝，不提供迁移兼容。它包含 NodeId、EntryIndex、Save 变量、ContentBuildId、StoryPackages 与 States（稳定 StateId → section version / JSON payload）。安装内容校验基础构建与准备时已安装的全部剧情包，缺少记录包不通过跳过降级恢复；开发目录构建标识为空。模块仍显式注册数据表及恢复器，未注册类型、重复 ID、缺失/未知 section、版本不符及无效载荷报错。builtin.animations、builtin.layers 以及音频 tracks/seffects 的 section version 为 2；音频还注册 channels 状态。各表按实际注册版本校验，不能把所有其他表统一视为版本 1。每份存档包含全部注册表（含空表），不写 CLR 类型名、Task 或后端对象。

差分 Layer 保存逻辑图集 ID、完整请求、最后保护集合、变体键、解析原因及可选 UsesResourceOverride（缺省 false）。解码检查来源互斥与状态结构，ValidateState 按会话图集解析。普通内容核对实际变体/图片/原因，不一致拒绝；明确的图集资源覆盖在安装或移除后，以选择意图重算候选状态的显示缓存和标记，保留输入存档及旧运行根。未知维度/值仍报错。展示恢复在呈现前复核；定义在准备阶段异步加载，show/select 同步。继续选择保留曾因缺组合而未显示的服装等意图。

状态编解码支持显式 `JsonTypeInfo<T[]>`：Core 使用 `StateCatalogBuilder.Register` 的 `jsonTypeInfo` 参数，执行模块可使用 metadata 在首位的 `PrimitiveRegistry.State` 重载。内置模块已注册源生成数组元数据，自定义 AOT 状态需在组合期提供相同契约。`GameState.JsonOptions` 公开冻结的存档设置，配置 section 也可显式提供 DTO resolver；接入见 [NativeAOT 手册](../../manual/publishing/native-aot.md)。

`CreateSaveData()` 返回 Engine 保存的最后稳定快照。状态表编码共享短同步边界，JSON 载荷和变量与运行对象隔离；不再由中央代码逐类型复制字段。读档先解码/校验候选状态与模块规则，再取消旧 scope 并替换整个 GameState；可选场景模块按新状态根解析独立场景服务。旧回调保留旧状态根，不能清理新代。展示恢复成功前暂停推进，恢复失败进入 Faulted。

`GameSnapshot.Attachments` 为可选的宿主附加 JSON 数据字典；SnapshotCopy 隔离复制，Engine 不解释或导入其内容。默认 Avalonia 宿主实际保存时附加对白历史，运行时快照不携带完整历史正文。`readingBoundaryId` 前导原语到其阅读原语之间不发布新的稳定快照，避免带语音对白读档落在音频和文字之间。`IPrimitiveDispatcher.OnInstructionSkipped` 是条件未执行通知；`OnPlaybackContinuing` 在有效手动推进（含仅补全文字）、自动推进、有效选择及启用自动/快速时通知宿主清理临时交互；`OnCheckpointCreating` 在执行门内提交已完成的宿主观察，随后捕获状态，防止展示完成的异步续体滞后导致游标和剧情位置不一致。这些方法默认空实现，不引入具体媒体依赖。

Save scope 随存档槽保存和恢复；Player scope 由宿主变量服务独立持久化，普通读档不得回滚。内置系统可以通过 `SystemVariableDefinition` 注册生成变量。Gallery 为每个 item 注册默认 `false` 的 Player bool `gallery_<item-id>_unlocked`；`gallery.unlock` 校验 item ID 后把该变量设为 `true`。

稳定快照的更新条件：

- 当前剧情游标已经提交到明确边界。
- 不存在未完成 blocking instance。
- 不存在 pending Choice。

未完成的纯 non-blocking 呈现实例不会单独阻止快照；因此 non-blocking primitive 必须在 `Dispatch()` 返回前先提交最终可存档逻辑状态。读档不恢复 Task、CancellationToken、平台控件或活动 `PrimitiveInstance`；宿主通过 `StateRestoreSession` 调用模块注册的恢复处理器重建展示；处理器依赖在组合时排序并校验缺失/循环。

动画按稳定语义存档，而不是保存展示层播放游标：

- `Once` 的 non-blocking 动画在分发时提交末值，读档直接显示末值；
- `Loop` / `PingPong` 保持第 0 帧逻辑状态，并在 `LoopAnimationState` 表保存类型化 request 或 timeline，读档后从第 0 帧重新播放；
- blocking 动画运行期间不会产生新的稳定快照，因此不保存其中间帧；
- 循环粒子属性动画保存 emitter、初始属性值与动画定义，不保存单颗粒子、渲染资源或时钟。

## 已读进度提交边界

存档槽、玩家变量和已读进度的文件写入共用替换工具：每次写入使用同目录下独立临时文件，成功写完并检查取消后替换目标，失败时清理本次临时文件。各服务继续负责自身的锁、数据快照和提交时机；进度 revision 只在替换成功后推进。

`ReadProgressIndex` 在 Runtime 中维护内存索引与 revision。文件实现 MarkRead 不执行磁盘 I/O，默认每 2 秒批量写入；同一个进度实例串行化 flush/clear，写入期间的新标记保留 dirty 状态。后台失败由 LastPersistenceError 暴露并在下个周期重试。Engine StopAsync 在执行与 continuation 退出后等待 FlushAsync，失败仍发布 Stopped 并向调用者传播；Sample 保存前 flush，销毁进度实例时取消定时器并完成最终提交。强制终止可能丢失自上次成功 flush 以来的标记（正常 I/O 时最多一个周期，持续失败时不保证时间上限）。存档槽恢复不回滚玩家已读进度。

## 宿主显式变量修改

GameEngine.SetVariablesAsync 接收带 player./save. 前缀的有限基础值批次，复制并预验证输入后，通过唯一执行门及 GameState 同步边界提交；恢复或停止使已排队的旧 scope 请求失效。它不推进剧情，也不捕获未完成的呈现状态，仅把本批显式存档变量合入原稳定快照，并在执行锁外发布检查点通知。玩家变量由原变量桥接回写，持久化仍由宿主协调。变量观察者接收独立副本，异常记录日志且不截断其他更新。

UpdateVariablesAsync 可在同一门内读取独立变量快照并同步计算修改批次，计算/校验失败不提交，适合条件扣款和计数累加。该入口不提供任意异步业务事务或阻塞活动原地恢复；项目通过模块状态及明确检查点策略处理复杂行为。通过 Runtime 对象直接写入不会自动刷新 Engine 的稳定存档快照，外部应用应使用此入口。
