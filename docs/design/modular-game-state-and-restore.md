# 模块化游戏状态与存档恢复

状态：已实施，2026-10-15；验收见 [feature 总结](../../features/F-20261015-03-modular-game-state/summary.md)。中央类型化容器、持久数据与运行实例分离、模块贡献恢复逻辑是已确认方向。用户进一步确认：项目快速迭代，不保留旧逻辑或旧档兼容；音频轨道与 SEffect 同批纳入；以逻辑清晰、没有冗余为约束。本文保留设计依据；实际 API 与生命周期以 runtime spec、development 和下方实施决策为准。

## 已确认的实施约束

1. 直接替换旧路径，迁移完成后删除旧 SceneState 列表、中央类型分支、双写镜像和旧恢复入口，不保留长期适配器或双格式读写。
2. 不提供旧存档迁移、旧字段回退、未知 section 的透明兼容。旧档拒绝读取；验证时创建新存档，不批量改写磁盘上已有存档。
3. 音频同批完成轨道和轨道 SEffect 的持久化、恢复、状态增删与生命周期验证，采用与 layer/effect 一致的状态注册方式。
4. 只保留必要的数据、索引和生命周期结构；不为可能的未来需求增加恢复策略框架、多种存储模型、自动发现或多层包装。
5. 更新实际 Sample 契约/配置/剧情文件及测试生产者，使其生成新格式；不在测试中保留旧生产路径来模拟兼容。


## 1. 变更前的问题与事实

变更前采用稳定快照，GameSnapshot v3 保存剧情位置、Save 变量和固定结构的 SceneState；读档并非重新执行先前所有 primitive。

| 位置 | 当前职责与扩展成本 |
| --- | --- |
| `SceneState`（已删除） | 固定声明 Layers、ActiveEffects、ActiveParticleEmitters、ActiveAnimations |
| [SnapshotCopy](../../src/Shared/GalNet.Core/Runtime/SnapshotCopy.cs) | 逐类型、逐字段深复制；新增持久字段需要同步修改 |
| [GameRuntime](../../src/Shared/GalNet.Runtime/Runtime/GameRuntime.cs) | 显式从各类描述重建 EffectInstance、ParticleEmitterInstance、AnimationPlaybackInstance |
| [SceneInstanceManager](../../src/Shared/GalNet.Runtime/Runtime/SceneInstanceManager.cs) | 维护全局场景句柄索引，并特殊处理 Layer 的持久列表增删 |
| `BuiltinPresentationReplay`（已删除） | 分别恢复图层、视频、效果、粒子、动画；动画仍按 EntryType 分支 |
| `PersistentSceneReplaySession`（已删除） | 统一拥有恢复产生的 continuation，并在下次恢复/释放前等待清理 |
| [动画值同步](../../src/Shared/GalNet.Primitives.Builtins/RuntimeActions/BuiltinRuntimeActions.AnimationValues.cs) | 按实例类型 switch，将效果/粒子动画值复制回持久描述 |
| [效果/粒子状态操作](../../src/Shared/GalNet.Primitives.Builtins/RuntimeActions/BuiltinRuntimeActions.EffectParticleState.cs) | 同时增删运行实例和持久列表，维护两份相关状态 |
| [AudioRuntime](../../src/Shared/GalNet.Runtime/Audio/AudioRuntime.cs) | TrackState 混合轨道定义、活动 voice、队列、ticket、generation 和效果；没有存档接口 |

Layer 与其他类型也不一致：Layer 本身既是可序列化状态又是动画目标，并包含 JsonIgnore 的 VideoPlaybackId；效果/粒子则另有 Active*State 描述。仅将四个 List 改成 Dictionary<Type, object>，不能消除这些差异和中央分支。

已有不变量见 [runtime spec](../spec/runtime.md)、[资源所有权](runtime-resource-ownership.md)、[执行注册设计](entry-registration-refactor.md) 和 [渲染管线计划](render-effects-pipeline-plan.md)。本设计不替代这些长期设计，也不改写 Accepted ADR-0002。

## 2. 目标与边界

目标：新增一个持久状态类型及其恢复实现时，只在所属模块/宿主组合根注册，不修改中央快照字段、深复制 switch、Runtime 恢复分支和内置重放分支。

中央容器统一持有可恢复事实。模块定义业务含义；通用机制负责索引、快照、版本和恢复生命周期。Core/Runtime 不引用平台类型或 Builtins 的具体模块。

剧情位置、Save/Player 变量、项目配置、玩家设置、资源服务继续有清晰边界；不立即将它们全部塞进通用字典。UI 页面、历史日志、单次截图、一次性 burst、Task 与后端资源不是自动持久化对象。

音频是同批必需的实现和验收消费者，轨道与 SEffect 都必须使用该机制，不允许只保留“未来可接入”的接口。实施结果见 feature 总结。

## 3. 中央容器存必要状态，不存执行对象

建议将覆盖视觉和音频的容器称为 GameState。Scene 可以作为它的领域视图；不再让名称暗示“只有画面”。概念结构为：

```text
GameState（一次运行代）
  Type → StateTable<T>（稳定 item ID → 状态记录）
  派生的场景句柄/动画目标索引

GameSnapshot（稳定检查点）
  流程位置 + Save 变量 + StateId → { Version, Payload }

运行期对象
  PrimitiveInstance、播放任务、ticket、generation、取消源、平台句柄
```

类型化表 API 的示意，不是最终接口：

```csharp
state.Table<LayerState>().TryGet(handleId, out var layer);
state.Update(batch => batch.Table<LayerState>().Set(handleId, updatedLayer));
```

选择 Type → 按 ID 索引的表，而非直接暴露 Type → List<T>：按 ID 更新和删除无需扫描；同表重复 ID 明确失败；不能绕过入口修改集合。确有顺序语义的数据应在记录中保存有序列表或显式顺序字段，不能依赖 Dictionary 的遍历顺序。集合为空与单例默认值应由状态注册声明，读取未注册类型报错，不隐式注册。

首期可将单例放在约定 key 的表中，不必同时推出多套 Singleton/List/Map 容器。若实际消费者证明单例很多，再增加薄的便捷 API。

| 数据 | 持久化策略 |
| --- | --- |
| 图层资源 ID、transform、z、opacity、visible | 作为 LayerState 保存 |
| 持续效果、粒子定义与稳定参数/动画值 | 作为状态记录保存 |
| 循环动画/计划的完整定义与稳定起始值 | 保存定义，默认从头重建 |
| non-blocking Once 动画 | 保持现有末值语义，不保存中间展示游标 |
| 阻塞操作的执行中间状态 | 保持现有稳定检查点策略，不新增中途存档 |
| 音频内容身份、内容 gain、暂停状态、有序逻辑队列 | 作为 AudioTrackState 保存，读档从头恢复活动内容 |
| 音频轨道 SEffect 的类型、参数、实例身份、所属轨道 | 作为 AudioSEffectState 保存，先恢复轨道绑定再建立声音效果 |
| PrimitiveInstance、Task、ticket、事件、CTS、VideoPlaybackId、平台 voice ID | 不保存，恢复时重新生成 |

Options 表达一次指令输入，State 表达当前可恢复事实，Request 表达给展示端口的操作；三者不能直接视为同一种数据。例如停止/替换后的状态、动画稳定起始值并不等于原始 Options。可共享中立值对象，不能把原始 Options 自动原样当存档。

## 4. 唯一事实来源与动画索引

建议新状态采用不可变记录/深度不可变值，通过 Set/Remove 或同步批量 Update 替换。单纯使用 record 并不能保证内部 List/Dictionary/自定义值不可变；注册类型必须满足状态契约，且通过序列化 round-trip 与别名隔离测试。

运行实例可以拥有展示临时状态，也可以持有该运行代的表引用；不得另存一份需要手工同步的权威业务状态。Effect/Particle 的动画目标适配器应读写同一状态记录，取代 SyncEffectAnimationState / SyncParticleAnimationState。适配器与按句柄的索引是派生缓存，不再进入存档。

仍需尊重当前全局场景句柄语义：Type 分表不能使同一个视觉句柄悄悄同时属于图层和效果。注册时为可被场景动画寻址的类型提供 ID/属性访问适配器，派生索引继续检查跨类型冲突；音频 track 等独立身份域不参与视觉句柄冲突。首期保留已有剧情 handle 格式，避免顺带修改内容协议。

删除与替换应由统一写入入口维护表和派生索引，不能再由 SceneInstanceManager 对 Layer 做特殊持久化处理。旧异步清理除校验运行代外，还应校验 item revision/播放 identity，不能仅凭相同字符串 ID 删除后来替换的记录。

## 5. 模块注册、序列化与校验

内存 Type 仅用于类型化访问；存档使用显式稳定 StateId，例如 builtin.layers、builtin.effects、builtin.audio.tracks，另有 section version。禁止将 AssemblyQualifiedName 或任意反射类型名作为存档类型协议。

注册示意：

```csharp
states.RegisterTable<LayerState>("builtin.layers", version: 1, key: value => value.Id);
restore.Register("builtin.layers", restoreLayers);
```

StateCatalog 是平台无关、冻结的元数据；恢复处理器在执行/展示组合期绑定服务。二者可由同一领域模块贡献，但不要求所有 IPrimitiveModule 都实现存档接口，不为无状态指令增加空方法。延续 GameRegistration 的显式组合：不扫描程序集、不全局服务定位、不按 CLR 类型自动发现插件。

组合根必须把同一份冻结 StateCatalog 提供给 Runtime 和存档边界，并把对应恢复处理器提供给恢复协调器；执行 schema-only 编译不得因此创建平台服务。具体 builder 方法名留待实施阶段确定，避免为这些产物新增多层会话包装对象。

通用机制承担：StateId/CLR 类型/恢复 ID 唯一性、item key、section version、JSON 编解码、载荷隔离、恢复依赖检查，以及已注册的业务校验回调。JSON 值必须由容器拥有，不能依赖已释放 JsonDocument 的生命周期。

业务模块承担：字段之外的领域规则、跨记录引用和恢复策略。例如效果目标必须存在、动画首帧与轨道合法、声音是否应在读档后重启。应复用现有中立校验函数，不能假定 Entry 字段 schema 就足以验证当前状态。

首期建议使用受限的纯数据 DTO 和通用 JSON codec。复杂类型显式提供 converter/codec；不要求每类手写 Clone。注册一份 payload codec，快照捕获时创建容器拥有的不可变 JSON 载荷，读档时解码新对象，避免再维护逐字段 SnapshotCopy。序列化只发生在快照/读档边界，不在每次状态更新时执行。

对未知 section、重复 StateId/item ID、版本不支持、payload 不合法、必要引用缺失均明确拒绝，且在停止现有运行前完成验证。没有旧格式回退、自动迁移、未知载荷透明保留或兼容读取路径。新存档写出所有已注册 section，空集合也显式写出；读档按当前格式要求其完整，避免“缺失时猜测默认状态”掩盖损坏文件。

整体容器格式改变升级 GameSnapshot 版本（已由 v3 升至 v4），与 section version 分工：前者约束封套结构，后者约束模块载荷。两者都只接受当前支持的版本，保留版本字段用于明确拒绝不匹配，而不是引入迁移器。测试直接新建新格式存档；磁盘旧档无需转换。

## 6. 稳定快照与并发

保留 Engine 现有稳定边界：游标已提交、没有未完成 blocking instance、没有 pending Choice。保存使用最后稳定检查点，而非临时读取当前容器。

状态写入和快照冻结必须共享一个短的同步边界。一次 Update 同步完成跨表变更与索引更新，锁内禁止 await、调用 presenter 或触发用户回调。稳定检查点需要把流程位置、Save 变量和所有状态表绑定到同一代/同一提交边界，不能分别异步采集后拼成存档。

后台完成回调通过运行代绑定的写入口更新逻辑状态；快照捕获期间不能出现“效果已删除、动画引用尚未删除”的半次更新。锁顺序须固定：Engine 执行门 → 状态提交边界；后端回调不得持有 AudioRuntime 锁再等待 Engine，避免与保存/停止相互等待。音频后端通知应先形成带 generation 的短事件，退出后端锁后再提交所需状态更新。

先正确实现完整冻结/编码，再测量实际快照成本。可缓存未变化 section 的不可变 payload，但必须有可靠 revision，不能因直接修改内部对象漏增 revision。不要首期就实现增量日志、事件溯源或完整 MVCC。

## 7. 读档与展示恢复协议

建议恢复流程：

1. 解码、校验版本与状态，构造隔离的新 GameState 和派生索引；检查恢复依赖。验证失败保留当前运行。
2. 进入 Engine 执行门，暂停推进，取消旧 scope/Choice，停止并等待旧恢复 continuation 与相关后端清理。
3. 原子切换流程位置、Save 变量、新 GameState 与新运行代。Player 变量、已读进度、玩家音量不随普通读档回滚。
4. 向模块提供绑定新代的上下文，按注册的依赖顺序重建展示；生成新的 voice/video identity，不恢复旧 Task/ticket。
5. 恢复完成后才允许剧情继续。后端重建失败则保持可诊断的恢复失败状态并阻止推进；不声称能够回滚已释放的平台资源。重试先清理本次部分资源，再按同一稳定状态恢复。

运行代隔离不能只靠 CancellationToken：旧实例必须继续持有旧 GameState/旧表，而不是通过共享 Runtime 动态取得新表。模块异步回调还需检查代/identity。音频模块通常跨多个运行代复用，故旧 scope 的 Reset 回调不能在新音频启动后清空新代；恢复协议必须先排空旧代并重新绑定模块资源。

恢复协调器不认识 Layer/Audio/Particle，模块处理器只恢复本领域，并声明依赖。例如：图层 → 依赖图层的效果 → 控制这些对象的动画；音频可独立恢复。依赖拓扑在组合时检查循环与缺失，同序稳定排序；首期顺序执行，未经测量不引入并行恢复。

恢复处理器返回其拥有的 continuation/清理句柄。协调器统一跟踪、取消、等待和逆序释放，复用当前 PersistentSceneReplaySession 已验证的职责，而非每个模块重新实现同一套任务管理。恢复协调器借用执行模块与 presenter，宿主先停止 Engine/恢复工作，再释放执行 view 和后端。

从存档恢复和 CatchUp 结束后的展示同步可共用“按状态构建展示”能力，但调用原因与策略必须显式：例如已有的 CatchUp BGM 投影不能被读档 Reset 策略误清空。重建动画时不得重放已有状态修改/一次性事件，避免 additive 累加或重复 burst；恢复路径应区分“建立展示 continuation”和“执行一次新指令”。

## 8. 音频轨道与 SEffect：同批实现

当前音频没有存档恢复。TrackState/VoiceState 不能直接序列化：它们包含 ticket、generation、运行集合与轨道定义引用。按 layer/effect 的目标结构拆分两类状态：

| 类型 | 保存内容 | 不保存内容 |
| --- | --- | --- |
| AudioTrackState | 轨道 ID、当前逻辑播放项、内容 gain、暂停状态、有序待播队列；仅保留恢复所需且现有系统支持的字段 | host、播放器、ticket、后端 voice ID/generation、分发任务、轨道配置副本、玩家音量副本 |
| AudioSEffectState | 所属 track ID、轨道内唯一的 effect instance ID、SEffect 类型 ID、参数 | 后端滤镜对象、播放器绑定、平台资源 |

两个状态表分别注册为 builtin.audio.tracks / builtin.audio.seffects。SEffect 通过 track ID 引用轨道，实例身份保持现有“轨道内唯一”的语义，复合索引键必须无碰撞，不能用未经转义的字符串拼接。删除/替换操作在一次状态提交内维护引用完整性。

所有仍存在的轨道逻辑状态采用同一持久化规则，不在中央按 BGM/SFX/Vocal 写分支，也不为首期再引入可选的轨道恢复策略框架。已经自然结束或被停止清除的播放项不恢复；仍活动或排队的内容会进入快照，包括 SFX/Vocal。这样恢复行为由检查点中实际存在的状态决定；当前内容在读档后从头播放，不承诺在句中精确续播。

SEffect 是轨道上的持久逻辑对象。没有 active voice 时也保留；恢复后必须继续作用于该轨道后来启动的声音，不能只恢复到读档当时的 voice。Stop/Clear、删除轨道和 StopSEffect 必须明确区分清除播放项与清除轨道效果，避免把现有操作混成一个 Reset。

恢复顺序为：验证项目轨道配置与中立 SEffect 定义 → 还原轨道/效果逻辑记录 → 建立效果绑定 → 用新后端 identity 启动活动项并应用当前玩家音量与暂停状态 → 恢复队列调度。音量仍按当前玩家设置与内容/轨道 gain 合成；项目路由定义不复制进存档。暂停成为明确的逻辑轨道状态，恢复时不能先短暂有声播放再遗漏暂停；新入队内容也必须遵守同一暂停语义。

默认从头恢复内容，不保存淡化墙钟进度。当前 host 协议没有通用 seek/游标查询，因此不增加精确续播、后台播放器快照或新的媒体时钟。现有系统尚未实现的循环播放、item scope effect、音频参数动画不因加入存档而顺带新增。仍需完成现有 gain、lowPass、highPass 及项目自定义轨道 SEffect 的恢复链路。

音频自然结束会改变活动项/队列，因此不仅 Play/Stop 指令，后端完成事件也必须通过代际隔离入口提交状态。后端 ticket/voice 映射是派生运行对象，不能再维护一套独立权威的内容队列后手动同步 AudioTrackState。capture 不向播放器异步查询，以保持检查点的一致性。

没有 host 时仍维护逻辑状态，展示不可用不会导致存档数据消失；流程等待按明确的无后端策略完成。自定义 SEffect 的中立描述/参数校验应能独立于平台播放器获得，不能为了读档校验先创建播放器。headless 验证需覆盖轨道状态、队列和效果 round-trip，不能只以 GUI 播放成功作为证据。

## 9. 可进一步优化的项目

| 优先级 | 改动与代码依据 | 收益与代价 |
| --- | --- | --- |
| P0 | 注册式状态表、codec 和恢复处理器，替换 SceneState / SnapshotCopy / GameRuntime / Replay 的类型清单 | 实现模块扩展目标；跨 Core、Runtime、Builtins、Sample，不能当局部字典替换 |
| P0 | 统一效果/粒子持久事实，删除同步镜像路径 | 消除双写、漏同步和按列表查找；要迁移动画目标读写，保留句柄和稳定值语义 |
| P0 | 恢复阶段、运行代、continuation 所有权统一 | 新模块自动得到取消/等待/失败门禁；不改变每个领域对状态含义的定义 |
| P1 | 循环动画从 EntryType + Dictionary<string,string> 改为注册的类型化持久定义 | 去除 ToReplayParameters 与反向解析，以及中央动画类型分支；状态表示仍须独立于一次调用 Options |
| P1 | 正常执行和恢复共用 State → Request 映射/中立值对象 | 减少重复字段搬运；不同路径仍保留“是否修改逻辑状态/产生事件”的显式区别 |
| P0 | 音频轨道/SEffect 同批落地，并作为新增状态类型的扩展验收 | 验证无需修改中央协议即可恢复音频；消除混合音频状态和额外镜像，覆盖全部仍存在的轨道状态 |
| P1 | 持久类型与适配器逐步归领域所有，Core 仅留中立协议 | 降低 Core 的内置领域耦合；会影响公开类型引用，适合在状态迁移后分步做 |
| P2 | 不变 section payload 按 revision 复用，ID 查找取代 FirstOrDefault/RemoveAll | 降低快照和大集合成本；先基准测试，不提前承诺性能或净删行 |
| 暂缓 | 自动捕获 CLR 对象图、反射插件发现、统一业务 DSL、任意时间点精确媒体续播 | 不能从纯类型推导领域恢复语义，容易把资源/生命周期带进存档，超出当前问题 |

这次值得优先减少的是“一个领域在五处登记/同步”的重复职责，而不是增加通用容器后继续保留全部旧镜像。完整迁移的行数应同时计算新状态/恢复协议、适配器和删除的中央分支；成本统计见 feature 总结，不只计算删除的中央分支。

## 10. 迁移顺序与验收建议

实施时建议先形成正式 feature/Phase 计划，再按下列可验证切片推进：

1. 建立状态注册、类型化表、封套 codec、稳定捕获与代际隔离。用一个自定义测试状态证明 round-trip 不要求修改中央代码。
2. 迁移图层，并验证视频临时 identity 不入存档、旧代引用不能影响新代、跨类型视觉句柄冲突仍报错。
3. 迁移效果/粒子/动画与统一恢复协调器，删除对应旧列表、镜像同步和中央重建分支；保持 Once/Loop/阻塞和计划事件语义。
4. 同批接入 AudioTrackState / AudioSEffectState，证明新增状态无需修改中央协议；覆盖自然结束、多 voice、暂停、队列、SEffect、玩家设置、无 host 与旧代回调。
5. 删除剩余旧场景状态/中央重放路径及临时过渡代码，更新正式 spec、Sample 实际文件和开发者示例；新建存档进行验证，不迁移磁盘旧档。

关键验收：自定义模块可独立扩展；快照无 mutable 别名；输入验证失败不破坏当前运行；跨表提交一致；旧任务与旧 scope 的 Reset 不污染新代；恢复失败不推进剧情；重复恢复不重复副作用；未知版本/重复 ID/依赖环明确报错；取消/部分重建失败后所有资源有所有者并能清理。

## 11. 清晰与无冗余的退出条件

- 每类可恢复事实只有一个持久状态来源；不允许“新状态表 + 旧 SceneState 列表 + AudioRuntime 私有权威队列”长期双写。
- 类型化访问、编码、恢复职责各有一个入口；增加模块不需要在 Core/Runtime/中央 replay 新增类型分支。
- 运行期索引、适配器和 ticket 映射明确为派生数据，可从状态重建；只有性能/后端身份所需的索引才保留。
- 正常执行与恢复共用数据转换，恢复不重放业务副作用；不为了复用少量字段把 Options、State、Request 强行合成一个大 DTO。
- 不保留旧 API 别名、旧存档转换器、双路径开关、反射兼容层或无人消费的抽象。
- 音频按两个状态类型完整接通，SEffect 的后续 voice 继承、暂停和自然结束后队列变化都有实际验证。
- 测试通过实际生产路径生成新快照；Sample 使用更新后的真实文件，不靠旧格式 fixture 维持假兼容。
- 审核同时计算公共机制和适配器成本，不能只报告中央代码的删行；若某个抽象只搬移代码而没有消除职责重复，应收敛回直接实现。

用户的三项范围约束已经确认。具体接口签名和每个实现切片仍需在实施计划中落定，不再将旧档兼容或是否包含音频列为待确认问题。

## 12. 实施决策

- 最终注册入口为 `PrimitiveRegistry.State<T>` / `Restore` / `ValidateState`，view 提供冻结的 StateCatalog。GameSnapshot v4 使用 `States`，中央机制不枚举领域类型。
- 复用 Layer、EffectInstance、ParticleEmitterInstance 的中立数据与动画属性操作作为唯一记录；没有为它们再增加 State DTO/适配器壳。记录仍可变，字段修改必须在 `State.Update` 内完成，捕获通过 JSON 形成隔离载荷。不可变记录是后续可选演进，并非本次已经实现的保证。
- 场景索引由实现 ISceneInstance 的注册记录自动派生；动画停止控制另属瞬时索引。视频每次建立图层都替换记录引用，旧任务仅能清理其持有的记录。
- StateRestoreSession 跟踪 continuation 和逆序清理回调，取消等待保留所有权，清理错误聚合报告；已经完成的释放不会在下次重试重复执行。
- 音频轨道/SEffect 的逻辑表为唯一事实，voice generation、ticket 和派生绑定不保存。内置 SEffect 中立描述由 Core 提供，平台处理器复用；自定义描述可注入 AudioRuntime。
- 验证器按领域显式注册。通用 codec 不能推导所有业务约束，不自动校验资源文件存在性或任意自定义模块的跨表语义；这些职责仍属于模块。项目轨道、音效参数、恢复依赖与重复身份均有实际校验。
- 保留稳定检查点语义：保存的是检查点当时的音频状态；后续自然结束不会改写历史快照。没有精确 seek、淡化游标或旧档迁移。
