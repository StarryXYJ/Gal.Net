# 开发调试内容热更新设计

日期：2026-11-03。状态：ready-for-implementation。

本设计实现 [feature.md](feature.md) 已确认的行为。核心边界是：Editor 判断改动影响并展示操作；客户端只同步运行事实、安装完整候选版本，并执行 Editor 指定的激活操作。首期不处理环路、程序代码和 Contracts 热替换。

## 1. 当前事实

- `GameSnapshot` 已保存当前 `NodeId`、下一编译 Entry 的 `EntryIndex` 和当前 Group 最近自然入口状态，尚未保存本次运行自然进入过的 Group ID 序列。
- `DebugSession.SeekGroupStartAsync` 已能使用当前 Group 入口缓存恢复变量和场景，再从 Group 开头执行，不需要增加第二套回退运行时。
- `GameEngine` 构造时复制完整 Graph，之后保持导航不变；活动 primitive 实例与 Graph 中尚未分发的 Entry 已经分离。
- 调试执行已有 run、generation、定位取消和作者 Entry span。客户端能报告当前编译位置及活动 primitive stable ID，Editor 可用当前版本清单映射到作者 Entry。
- Editor 已能构建 `Output/Debug/Revisions/<revision-id>/` 不可变候选并生成调试清单；P1 已实现纯清单差异分类。
- `GameResourceScope` 和部分会话服务当前按整个玩家会话持有，因此资源切换需要明确所有权，不能覆盖正在读取的目录或提前释放旧对象。

## 2. 最小职责模型

整个功能只增加两个有状态协调服务，现有构建器、Engine 和 UI 保持各自原有职责。

### 2.1 Editor：`DebugContentHotReloadService`

一个 Editor 会话级服务负责完整工作流：

1. 调用现有调试构建能力生成不可变候选和清单；
2. 将候选安装到客户端，使客户端始终持有最新完整版本；
3. 读取客户端运行事实；
4. 用当前运行版清单、最新候选清单和运行事实计算 `Direct`、`ReturnGroupStart` 或 `RestartGame`；
5. 对 Direct 发出立即激活，对另外两类维护 Editor 待处理状态；
6. 向 Editor Toast、顶部菜单和调试面板暴露同一个状态与命令；
7. 成功激活后切换源码映射和断点映射。

构建器只负责构建，分类器保持纯函数，ViewModel 只绑定服务状态。它们不再分别维护热更新流程。

### 2.2 客户端：`DebugContentRevisionService`

一个调试客户端会话级服务负责版本生命周期：

- 验证并安装完整不可变候选；
- 分别维护 `LatestRevision` 和当前运行绑定的 `RunRevision`；
- 在客户端执行门内调用 Runtime 原子激活能力；
- 返回 Group 开头时先激活最新版，再调用现有 Seek；
- 新游戏创建时默认绑定最新版；
- 持有当前、最新和必要的退休资源 scope，并负责最终释放；
- 向 inspect 报告版本身份和运行事实。

该服务不比较清单、不决定影响等级、不生成提示文案，也不维护 Editor 的待处理原因。

### 2.3 Runtime、协议与 UI

- Runtime 不增加协调服务，只扩展 `DebugSession`：报告运行事实、替换不可变导航、推进 generation，并复用现有返回 Group 开头能力。
- `GalNet.Debugging` 只放中立 DTO、清单、纯分类器和协议命令，不拥有流程状态。
- Editor UI 只绑定 `DebugContentHotReloadService`。客户端 App 不显示本功能的 Toast、菜单状态或影响原因。

## 3. 两个版本指针

客户端明确区分：

- `LatestRevision`：最近一次成功安装的完整候选；
- `RunRevision`：当前 GameRun 实际执行的版本。

每次 Editor 构建成功都先安装候选并更新 `LatestRevision`。安装本身不改变当前运行，也不需要先分类。若新的未激活候选覆盖旧候选，客户端可立即释放从未成为 RunRevision 的旧候选。

| Editor 判定 | 安装后的激活行为 |
| --- | --- |
| `Direct` | 原子地将当前运行切换到 `LatestRevision` |
| `ReturnGroupStart` | 当前运行继续使用旧版；Editor 提示用户，确认后原子激活最新版并返回当前 Group 开头 |
| `RestartGame` | 当前运行继续使用旧版；Editor 提示用户，新游戏直接绑定最新版 |

因此不需要拼接“旧当前 Group + 新未来 Group”，也不需要在客户端保存影响原因。严格等级存在时，整个当前运行保持一个一致版本，直到完成对应动作。

## 4. 运行事实

客户端通过 inspect 回传以下最小事实：

- run ID、generation、`RunRevision`、`LatestRevision`；
- 当前节点/Group ID 和下一编译 Entry index；
- 当前尚未完成的 primitive stable ID；
- 按自然进入顺序排列的 Group ID 序列；
- 当前 Group 是否有可用入口缓存；
- 是否检测到 Group 重复自然进入。

Group 序列只保存 ID，不保存 Graph、节点副本、边或状态快照。当前 Group 单独报告；Editor 将序列中除当前 Group 外的项视为历史 Group。调试 Seek、回放和断点定位不追加自然经过序列。

首期不根据经过边精确分析拓扑修改。Graph topology、Choice/Branch、Group 增删或其他仅靠 Group ID 序列无法证明安全的变化，由 Editor 保守判为 `RestartGame`。

`GameSnapshot` 升为 v6 并保存 Group ID 序列，使读档后 Editor 仍能正确分类。现有 `GroupEntrance` 继续是返回 Group 开头的唯一状态来源，不复制第二份入口缓存。

## 5. Editor 影响分类

影响等级按 `RestartGame > ReturnGroupStart > Direct` 合并：

| 修改位置 | 影响 |
| --- | --- |
| 未经过的 Group | `Direct` |
| 当前 Group 尚未开始的作者 Entry | `Direct` |
| 当前活动作者 Entry或当前 Group 已执行作者前缀 | `ReturnGroupStart` |
| 已经离开的 Group | `RestartGame` |
| Graph 拓扑或无法可靠归属的内容 | `RestartGame` |

Editor 使用当前 `RunRevision` 的清单解释客户端的编译 index 和活动 primitive ID，再与候选清单按稳定作者 Entry ID 对齐。Composite 展开的全部 primitive 归属同一个作者 Entry；活动作者 Entry 整体属于已执行前缀。

新增、删除、调序和零展开 Entry 只要无法证明完全处于未来边界，就提升等级。普通资源变化按清单记录的作者使用位置采用最严格等级；动态引用、依赖不完整和会话级资源保守提升。

Contracts、模块程序集或程序代码身份变化不进入三级分类，直接要求重启调试 App。

## 6. 安装与激活协议

协议采用简单的两段式操作。

### 6.1 安装最新版

`content.install` 只携带本机不可变候选目录及项目、revision、内容和契约身份。客户端独立复算身份、打开完整候选并验证 Graph 与 primitive 实现；成功后更新 `LatestRevision`，失败则保持原 Latest/Run 不变。

安装成功不推进 Runtime generation，因为当前执行内容尚未改变。响应返回权威 `LatestRevision` 和 `RunRevision`，Editor 可在超时后通过 inspect 对账。

### 6.2 激活当前运行

Editor 分类后使用以下两个运行操作：

- `content.activate`：用于 Direct，整体替换导航和新资源获取来源，并把下一作者边界映射到候选编译 index；
- `content.activateAndSeekGroupStart`：用户确认返回时，先整体替换最新版，再复用现有入口缓存执行 Seek。

重新开始不需要独立的内容激活命令。Editor 调用既有“开始新游戏”入口，客户端版本服务保证新 Run 使用 `LatestRevision`。若现有开始新游戏路径不能重建会话级资源，则由同一客户端版本服务扩展该路径，不另设第二个协调器。

激活请求包含预期 run、generation、RunRevision、当前 Group/index 和活动 primitive ID。客户端只做技术前置条件校验：若游戏在分类后已推进，拒绝激活；Editor 重新 inspect 并分类。客户端不得自行把 Direct 改成 Return 或 Restart。

## 7. Runtime 原子换代

Runtime 只实现一个核心原语：在现有执行门内，用已验证候选导航替换当前不可变导航，并把当前游标设置到 Editor 提供的候选编译边界。

- 活动 primitive 实例继续正常结束，不被重建；
- 下一次 Entry 分发读取候选导航；
- 成功换代推进 generation，取消旧 Seek/CatchUp，清除旧 span 绑定的断点和迟到操作；
- 失败时导航、游标和 generation 全部保持旧值；
- 普通非调试运行不暴露换代入口。

`activateAndSeekGroupStart` 在同一个客户端串行操作中先换代，再使用旧运行捕获的 Group 入口状态执行现有 Seek。若入口已失效，操作失败并回报 Editor；Editor 再提示重新开始，不由客户端自动降级。

## 8. 资源和文本

客户端版本服务拥有每个 revision 的完整 `GameResourceScope`。激活版本时只切换“后续新获取”的资源来源：

- 已持有的图片、音频、视频和活动呈现对象继续使用旧对象；
- 新 Acquire、文本解析和文件物化使用 RunRevision 对应 scope；
- 不实现逐句柄退休协调器；曾成为 RunRevision 的旧 scope 保留到当前游戏结束后统一释放；
- 从未激活且被更新候选替代的 scope 可立即释放；
- 退休版本数量设置小上限，达到上限时安装仍可保留最新版，但 Direct 激活返回明确的容量错误，由 Editor 提示重新开始。

Gallery catalog、预载纹理、全局效果注册、项目设置等会话级内容由 Editor 判为 `RestartGame`；新游戏路径通过同一个客户端版本服务重建相关会话对象。若某类对象实际上只能在 App 启动时建立，则清单将其标记为需要重启调试 App，不伪装成游戏内热更新。

## 9. Editor UI 与连续修改

Toast、顶部菜单和调试面板全部由 Editor 的同一个服务状态驱动：

- Direct：激活成功后短暂提示“修改已应用”；
- ReturnGroupStart：持续显示“返回当前 Group 开头应用修改”；
- RestartGame：持续显示“重新开始游戏应用修改”。

连续构建始终安装最新候选。Editor 每次使用实际 `RunRevision` 与最新清单重新分类，因此新的保存可以升级、降低或解除旧要求。最旧构建迟到时按作者签名/请求序号丢弃，不能覆盖更新候选。

只有客户端确认激活后，Editor 才把源码高亮、Entry 定位和断点映射切换到新版。RestartGame 在新 Run 建立并报告新版 RunRevision 后再切换。请求超时通过 inspect 的两个版本身份对账。

## 10. 取舍与风险

### 不做客户端分类

客户端没有作者旧/新清单和 Editor 交互上下文。复制分类会形成两个权威结论，因此客户端只报告事实并验证命令前置条件。

### 不做部分 Graph 合并

把新版未来 Group 与旧版当前/历史 Group 拼成临时 Graph，会增加节点合并、资源代次和多次更新覆盖规则。两个完整版本指针已经满足行为要求，故当前运行始终绑定单一完整版本。

### 不记录经过边

首期产品规则以 Group 为单位，且不处理环。仅保存有序 Group ID 足够判断历史、当前和未来；拓扑变化统一重开，换取更小的存档与协议契约。

### 主要风险

- 活动 Composite 的作者边界映射错误；必须由 Editor 清单映射而非客户端猜测。
- 安装成功但响应丢失；inspect 必须返回 Latest/Run 两个身份。
- 激活准备期间游戏推进；精确前置条件校验并拒绝旧命令。
- 旧资源 scope 保留导致占用增长；采用小上限并明确要求重新开始。

## 11. 兼容与文档

- `GameSnapshot` 升为 v6；旧格式按当前规则明确拒绝，不伪造经过序列。
- 调试协议升级；旧 Editor/App 组合在握手时明确拒绝内容热更新。
- 实现验证后同步 `docs/spec/runtime/execution.md`、`docs/spec/editor/debugging.md` 和 `docs/manual/editor/preview-and-debugging.md`。
- Developer/调试组合才创建版本服务；发行 Player 保持单一内容 scope，不增加协议或文件监听。
