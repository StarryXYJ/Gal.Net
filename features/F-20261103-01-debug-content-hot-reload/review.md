# 开发调试内容热更新简化审查

日期：2026-11-03
结论：resolved

2026-11-03 复核：下列收缩项已落实。协议与清单不再携带内容 revision；inspect 不再回传 Latest/Run；Editor 删除主动对账、请求序号和 Notice 副本；Runtime 删除环标志与重复入口能力布尔；顶部菜单仅保留应用入口；资源使用位置分析、退休上限和细分拓扑摘要均未进入最终设计。保留的 run/generation/位置校验仅用于保证原子替换发生在已检查的执行边界。

当前实现可以工作，但明显超过需求所需复杂度。阻塞点不是功能错误，而是把一次 Editor 驱动的调试替换扩展成了内容版本管理、响应丢失对账和细粒度差异解释系统。继续按现设计实现 P5/P6 会进一步固化不必要状态。

## 必须收缩

### P1 — 删除显式版本协议

证据：`DebugContentVersions`、manifest `Revision`、inspect `LatestRevision`/`RunRevision`、install/activate request 的 revision，以及 Editor 的 revision 目录反查共同形成了一套跨进程版本状态机。

影响：Editor 和客户端必须持续对账两个字符串，激活、重开、超时和连续构建都有额外分支，但产品行为只需要“当前运行内容”和“最近候选”。

建议：客户端内部只持有 `_run` 与 `_latest` 两个候选对象引用；`content.activate` 始终激活最新候选。Editor 自己持有已应用 build 和 pending build。协议不传 revision，inspect 不回传 revision。

### P1 — 删除响应丢失后的版本对账

证据：Editor service 在激活超时后主动 inspect `RunRevision`，`ExternalGameDebugClient.RefreshAsync` 主要为版本确认和重开确认新增。

影响：为了调试工具的极端响应丢失路径，引入主动轮询、版本身份和额外错误恢复状态。

建议：命令成功才提交源码映射；超时保持旧映射并提示重新连接或重试。不要为该路径维护分布式提交协议。

### P1 — 删除独立拓扑摘要语义

证据：`628803ca` 新增规范化 Graph 拓扑摘要、专门测试与 lesson；分类器仍只需要知道“Graph 是否有任何不按 Entry 处理的变化”。

影响：需要持续定义哪些 Graph 字段属于拓扑，并承担漏字段风险。

建议：恢复简单 Graph/非 Entry 内容摘要；一旦变化统一归入“不可直接热重载，重新开始游戏”。不细分拓扑类型。

### P1 — 删除未被 UI 使用的分类原因

证据：`DebugContentImpactResult.Reasons` 为每个差异构造英文原因数组，但 Editor 只读取 `Impact` 并显示固定本地化文本。

影响：分类器长度和测试面扩大，且英文内部原因与实际用户文案形成第二套提示来源。

建议：分类器直接返回 `DebugContentImpact`。

## 应当收缩

### P2 — 删除环检测专用状态

证据：Runtime/Snapshot 增加 `RepeatedGroupVisit`，Editor 又将其升级为 Restart；需求已明确首期不考虑环。

建议：只保留自然经过的 Group ID 序列。若以后支持环，再以独立 feature 设计。

### P2 — 删除重复的入口缓存布尔值

证据：`CanReturnGroupStart` 与已有 `CachedGroups.Contains(currentGroup)` 表达同一事实。

建议：Editor 直接使用已有缓存 Group 集合，或返回当前入口缓存一个最小事实，二者只保留一个。

### P2 — 删除连续请求序号

证据：Editor service 使用 `_request`/`Interlocked` 丢弃迟到结果，但所有入口最终都经过同一个 Panel `RunAsync`，`Busy` 已串行化用户命令。

建议：保留构建完成后的 author signature 一次校验即可；不要同时维护 Busy、request generation 和 author signature 三重防线。

### P2 — 删除 Notice 状态副本

证据：`DebugContentNotice` 与 `DebugContentImpact` 一一映射，service 同时发 `Changed` 和 `Notice`，Panel 再映射成本地化 key。

建议：service 只暴露当前 pending impact/操作结果；Panel 在命令完成后显示对应 Toast。

### P2 — 精简菜单

证据：顶部 Debug 菜单同时常驻“应用内容修改”“返回并应用”“重新开始游戏”，面板又显示相同操作。

建议：顶部菜单只保留“应用内容修改”。Return/Restart 只在 pending 状态出现一个对应动作，避免三个长期可点击入口。

### P2 — 删除退休版本上限计划

证据：P5 计划增加退休版本数量上限、容量错误和 Editor 降级提示。

影响：新增容量状态与错误协议；调试 Run 本身生命周期短，旧 scope 已计划在 Run 结束统一释放。

建议：当前 Run 用过的 scope 统一保留到 Run 结束；未激活候选被下一候选替换时立即释放。不设上限和专用错误。

### P2 — 不做资源使用位置清单

证据：P5 计划把普通资源引用映射回作者位置并参与三级分类。

影响：需要跨 Entry、动态引用、预载和会话对象建立完整依赖图，远超当前需求。

建议：普通资源安装后只影响后续新 Acquire；已持有对象自然继续。明确的会话级文件变化统一要求重新开始游戏，不分析资源历史位置。

## 应保留的最小实现

1. Snapshot 保存自然经过的 Group ID 序列。
2. inspect 回传当前 Group、编译位置、活动 primitive ID 和 Group 序列。
3. manifest 只保留项目/契约身份、author signature、Group/Entry span 与内容摘要，以及一个非 Entry 内容摘要。
4. Editor service 保存 `appliedBuild` 与 `pendingBuild`，构建后返回 `Direct`、`ReturnGroupStart` 或 `RestartGame`。
5. 客户端 service 保存 `runContent` 与 `latestContent` 两个对象引用，负责安装、激活、为新 Run 选择 latest 及释放 scope。
6. Runtime 保留单一原子 Graph 替换和现有 SeekGroupStart。
7. 协议只保留 install、activate latest、activate latest and seek group start；继续使用已有 run/generation/位置前置条件。

预计收缩后可删除显式版本 DTO/字段、主动刷新对账、原因数组、环标志、入口缓存重复字段、请求序号、Notice 枚举、两个常驻菜单项和 P5 的容量/资源依赖分析，整体状态源仍只有 Editor/客户端各一个服务。
