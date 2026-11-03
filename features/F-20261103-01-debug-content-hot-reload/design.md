# 开发调试内容热更新设计

日期：2026-11-03。状态：ready-for-implementation。

本设计实现 [feature.md](feature.md) 已确认的行为：未来内容直接采用新版；当前 Group 已执行前缀变化时返回 Group 开头；历史 Group 变化时重新开始游戏。首期不处理环路、程序代码和 Contracts 热替换。

## 1. 当前事实

- `GameSnapshot` 已保存当前 `NodeId`、下一编译 Entry 的 `EntryIndex` 和当前 Group 最近自然入口的变量/状态快照；尚未保存已经自然进入过的 Group。
- `DebugSession.SeekGroupStartAsync` 已能使用当前 Group 入口缓存恢复变量和场景，再从 Group 开头执行，不需要增加第二套回退运行时。
- `GameEngine` 在构造时用 `GameGraphNavigator` 复制整张 Graph，之后按 Engine 生命周期保持拓扑和 Group 内容不变。
- 调试执行已有 generation、旧请求拒绝、定位取消、展示收束和源码 Entry span。作者 Entry 可通过构建结果的 `SourceEntries` 映射到一段编译 primitive。
- Editor 当前把 `Output/Debug` 整目录原子替换，并在作者变化后禁用旧源码映射；运行中的 App 不会重新打开该内容。
- `GameResourceScope`、`AssetManager` 和呈现模块按整个玩家会话持有。`AssetManager` 以资源 ID 和目标类型缓存对象；直接换文件或清缓存无法同时保证旧句柄继续有效和后续 Acquire 读取新版。
- App 顶部 Debug 菜单已有跳到 Group 开头、重新加载资源并重启、通知和宿主重启入口；当前桌面通知是模态消息，需要增加不阻断游戏的状态提示。

## 2. 设计原则

1. Editor 构建完整候选，App 独立打开并验证，最后在运行门内提交；任何失败都保留完整旧代。
2. Runtime 只接受已经编译、已验证的 Graph 和明确的游标映射，不读取 Editor 文档或推断作者身份。
3. 作者差异、资源引用和源码映射由 Editor 构建清单表达；运行位置、已经过 Group 和当前入口缓存由 App/Engine 提供。
4. 一次提交切换 Graph、源码映射身份、文本/资源代次和调试 generation。不能让 Editor 与 App 分别“尽力更新”。
5. 当前活动 primitive 继续拥有旧对象和旧资源；热更新只改变尚未分发的内容。
6. 提示代表尚未满足的运行一致性要求，必须持续显示在菜单中；Toast 只是即时通知。

## 3. 不可变调试内容版本

### 3.1 输出布局

调试构建不再覆盖正在使用的目录。`ProjectDebugPreparation` 在 `Output/Debug/Revisions/<revision-id>/` 发布不可变候选，并把该目录交给 App。初次启动和每次热更新使用同一种版本目录，没有“初次可变、后续不可变”两套加载语义。

`revision-id` 由完整调试载荷指纹确定。指纹覆盖 Graph、Group、Assets、I18n、settings 和 Contracts 身份；当前 `DebugContentIdentity` 未覆盖 Assets，需要扩展。Contracts 字节变化生成不同契约身份并拒绝内容热更新，要求重启 App。

Editor 不在 App 仍可能读取时删除版本目录。会话结束或 App 明确释放版本后再清理；异常退出后的旧版本由下一次调试准备做有界清理。清理仍只发生在 `Output/`。

### 3.2 调试清单

每个候选包含不进入正式发行内容的调试清单，至少记录：

- project、revision、内容指纹和契约指纹；
- 作者签名；
- 每个 Group 的稳定作者 Entry 顺序、编译 span、primitive stable ID 和内容摘要；
- Graph 节点/边摘要；
- 每个资源的内容摘要、直接/间接作者使用位置，以及引用完整性；
- settings、I18n、人物、Gallery、预载纹理和效果程序等会话级内容摘要。

清单由构建的同一冻结作者快照生成。资源使用位置复用 `ResourceReferenceIndex` 和现有依赖 metadata；动态引用或依赖不完整必须标记为未知，不能猜测成未来 Entry。

## 4. 运行进度模型

`GameSnapshot` 增加按自然进入顺序去重的 `VisitedGroups`，并将格式升为 v6。Engine 在自然进入 Group 时登记；调试 Seek/回放不登记，也不覆盖自然入口。恢复存档时恢复该集合。首期不支持环，因此重复 Group 被校验为不受支持，不能静默缩成一次访问后继续热更新。

当前 Group、下一编译位置和入口状态继续分别使用 `NodeId`、`EntryIndex`、`GroupEntrance`。不再另存一份重复入口缓存。

当 Engine 停在 Choice/Branch 而没有活动 Group 时，已经离开的 Group 都属于历史，尚未进入的后继 Group 属于未来；此时没有“当前 Group 前缀”操作。返回 Group 开头只在当前节点确为 Group 且入口缓存存在时可用。

调试状态另报告：

- 当前已发布 revision；
- 已经过 Group；
- 当前活动作者 Entry/下一作者边界；
- 待处理影响等级和原因；
- 是否存在可返回的当前 Group 入口。

这些字段用于调试协议和 UI，不进入普通作者文档。

## 5. 差异与影响分类

定义三级影响，按 `RestartGame > ReturnGroupStart > Direct` 合并：

| 修改位置 | 影响 |
| --- | --- |
| 未自然进入的 Group 或当前 Group 尚未开始的作者 Entry | `Direct` |
| 当前活动作者 Entry、当前 Group 已执行作者前缀 | `ReturnGroupStart` |
| 已经离开的 Group | `RestartGame` |

当前活动作者 Entry 的整个编译 span 都属于已执行前缀，即使其中还有 primitive 尚未分发。没有活动作者 Entry 时，`EntryIndex` 所在作者 Entry 是尚未开始的后缀。零展开作者 Entry 使用构建清单中的虚拟边界参与排序。

新增、删除和调序先用稳定作者 Entry ID 对齐，再比较内容摘要和相对顺序。只要无法证明修改完全位于边界之后，就提升到 `ReturnGroupStart`；无法可靠归属到 Group 时提升到 `RestartGame`。

Graph 变化按已经实际跨过的节点/边判断。已选择并离开的路径变化要求重新开始；当前 Group 尚未跨出的出口和未来拓扑可以直接更新。Branch/Choice 变化若无法由一次性、无环路径可靠归属，则要求重新开始。

资源变化先把资源及其依赖闭包映射回作者使用位置，再采用这些位置中最严格的等级。以下内容首期直接要求重新开始游戏：

- 动态引用或依赖不完整，无法证明使用位置；
- settings、人物、Gallery catalog、默认视觉预载、全局效果程序注册等会话级内容；
- 删除或改变资源类型、资源身份/metadata，使现有解码或恢复契约发生变化。

普通图片、音频、视频等内容字节变化，只要所有静态使用位置都在未来范围，就允许直接更新；当前或历史引用按相同位置规则提升。

## 6. Engine 内容换代

### 6.1 准备

App 在执行门外打开候选 `GameResourceScope`，解析完整 Graph，验证全部 primitive 实现、项目/契约身份、调试清单、资源索引和文本。准备结果拥有自己的资源和文件生命周期，不借用候选目录中的临时流。

Runtime 增加只供显式调试会话使用的内容换代入口。它接收已经验证的候选 Graph、当前游标在候选中的目标位置、影响等级和 revision 身份，不依赖网络 DTO 或文件路径。

### 6.2 提交

提交在 `GameSession` 命令门和 Engine `_dispatchGate` 下串行执行，并再次核对 run、generation、旧 revision、当前 Node/Entry 和活动作者边界。若游戏在准备期间已经推进到不同边界，拒绝本次提交，由 Editor 用最新状态重新计算。

`GameGraphNavigator` 从一次构造的只读索引改为可准备、可整体替换的不可变快照。Engine 字段仍只指向一个当前快照，不逐节点原地修改。

- `Direct`：候选导航成为当前导航；当前 Group 的 `EntryIndex` 映射到候选中同一“下一作者边界”。已活动 primitive 继续完成，之后从候选后缀分发。
- `ReturnGroupStart`：未来节点先采用候选；当前 Group 暂时保留旧版本并登记候选 Group。用户执行返回开头时，先提升候选当前 Group，再用现有入口缓存执行 `SeekGroupStartAsync`。
- `RestartGame`：对未来执行安全的候选仍可发布，但保持“需要重新开始”标记；新游戏创建新 Engine 时完整采用最新候选并清除标记。首期无环保证不会再次自然进入已经离开的旧 Group。

每次成功提交推进调试 generation，取消旧 Seek/CatchUp，清除旧内容绑定的断点 span、中间追帧状态和迟到操作。当前 Group 的自然入口缓存只有在 `ReturnGroupStart` 时保留；普通内容换代不能将它误当作候选版本重新捕获。

提交失败释放候选，旧导航、资源代次、源码映射身份和 generation 全部保持不变。

## 7. 资源与文本代次

默认 Avalonia 组合增加调试期内容版本协调器和可换代资源门面。协调器保存“最新已发布候选”和“当前会话基础版本”；门面保存当前资源代次。新的 Acquire、文件物化和文本解析进入当前代；返回的外层 `AssetHandle`/媒体租约持有其来源代次，释放最后一个租约后才允许销毁退休代。

解码器注册和诊断订阅由门面保存，并应用到每个新代。旧代退休时取消未交付加载，但不提前释放仍由活动 primitive、呈现恢复或媒体后端持有的对象。

ImageVariants、预载纹理、效果程序、Gallery 和项目配置在当前实现中按会话构造，不经普通 Acquire 动态查询。因此首期把这些变化分类为 `RestartGame`，不为热更新增加第二套可变全局注册表。

当前 `StartNewGameAsync` 只更换 GameRun，仍会复用旧 `GameResourceScope`、Gallery、预载资源和项目配置，不能直接承担上述重建。调试协调器提供串行的“提升候选并开始新游戏”操作：先释放当前 Run，再将最新候选提升为会话基础版本，重建 visuals、Gallery resolver、文本与项目选项，按同一 profile 重新打开所需玩家服务，最后创建新 Run。它保留玩家全局数据和既有存档文件，但新运行从游戏入口开始；整个操作不重启 App 进程。

本功能只在 Developer/调试组合中启用。普通玩家运行继续直接持有单一 `GameResourceScope`，不增加文件监听或更新协议。

## 8. Editor、协议和 App UI

### 8.1 协议

调试能力增加 `contentReload`。Editor 发送候选目录、revision/内容指纹、契约指纹和期望的 run/generation/旧 revision。App 独立计算指纹并加载清单，不能信任 Editor 声称的身份。

成功响应包含新 generation、已发布 revision、影响等级、原因、当前/待提升 Group 和可用动作。`inspect` 同样返回这些字段，解决请求超时后“不知道是否已提交”的情况。

协议只传身份和本机不可变目录，不在 4 MiB JSON 帧内传 Graph 或资源字节。远程设备和公网内容传输不在本期范围。

### 8.2 Editor

作者变化不再永久禁用调试操作。用户执行“构建并应用修改”后：

1. 捕获作者签名并构建不可变候选；
2. 读取最新运行状态，计算建议影响；
3. 发送候选，App 在提交前以实际状态复核；
4. 只有成功响应后才切换 `_sources`、`_positions`、`_spans` 和作者签名；
5. 重新把仍存在的作者断点映射到新版 span，删除项明确报告。

连续保存使用单调请求代次；旧构建完成时若作者签名已改变，只保留诊断，不发布到 App。首期提供显式命令，自动保存后应用可在稳定后另行启用，不改变核心协议。

### 8.3 App 顶部菜单和提示

App 接收提交结果后发出非模态 Toast，并在 Debug 菜单持续显示：

- `ReturnGroupStart`：显示“返回当前 Group 开头应用修改”，启用现有返回能力的热更新动作；
- `RestartGame`：显示“重新开始游戏应用修改”，调用调试协调器的“提升候选并开始新游戏”；普通仅剧情更新可以复用新 Run 路径，会话级变化重建相关资源，但都不重启 App 进程；
- `Direct`：短暂提示修改已应用，无待处理菜单状态。

重新开始级别覆盖返回开头。动作成功后重新检查待处理原因，只有满足条件才清除。构建失败只在 Editor 报告，App 继续旧版本。

## 9. 候选方案与取舍

### 重建整个游戏运行

可以复用现有 `RestartAsync`，实现简单，但会丢失当前状态并把所有修改都降级为重开，不能满足未来 Entry 直接生效，因此不采用为主流程。

### 原地修改作者 Graph 或共享对象

当前 Engine 会复制 Graph；让它继续借用可变作者对象会破坏并发、校验和快照边界，也无法原子回滚，因此不采用。

### 发送差量文件或协议内字节

差量需要新的补丁格式、回滚和大帧传输。本功能是同机开发调试，完整不可变目录的验证和失败恢复更直接，因此首期发送目录身份。

### 每次更新创建第二个 Engine

新 Engine 可以加载新 Graph，但迁移活动 primitive、入口缓存、调试暂停、呈现所有权和变量会形成另一套运行替换协议。现有 Engine 已有唯一执行门和回放能力，因此选择在调试门内替换不可变导航快照。

## 10. 兼容、风险与文档

- `GameSnapshot` 升至 v6；项目当前对不匹配格式明确拒绝，不增加旧 v5 的不完整经过记录迁移。正式实现后同步运行时存档规范。
- 调试协议版本升级；旧 Editor/App 组合在握手时明确拒绝，不降级为内容热更新。
- 最大风险是活动作者 Entry 边界判断、资源退休代泄漏、App 已提交但 Editor 超时，以及当前 Group 候选提升失败。阶段验证必须分别覆盖。
- Contracts 指纹、项目 ID、候选路径安全、链接检查和完整内容指纹仍是发布前门禁。
- 实现验证后同步 `docs/spec/runtime/execution.md`、`docs/spec/editor/debugging.md` 和 `docs/manual/editor/preview-and-debugging.md`；设计过程不提前改写正式事实。

