---
id: F-20260929-15-replay-save-format
title: 基于原语追帧的稳定点存档（暂缓）
type: feature
status: deferred
created: 2026-09-29
updated: 2026-09-29
---

# 基于原语追帧的稳定点存档

## 2026-09-29 范围决策

用户决定不继续推进追帧存档。现有“最近无阻塞稳定状态 + Save scope 变量 + 完整 `SceneState`”快照继续作为正式存档模型，本 feature 不产生设计、Phase Plan 或业务实现。

后续另行规划“按 graph node 记录完整通过状态，并从已解锁 group 快速进入”的进度/入口能力。该能力不以原语 Replay、内容哈希或 replay-save 格式为前置条件。

## 原始目标

将正式存档从保存完整 `SceneState` 转为保存最近一次无阻塞稳定状态的位置、分支轨迹和存档变量。读档时由原语自身定义 Replay 语义，静默、快速地重新执行剧情路径以重建画面。所有已注册原语都必须支持 Replay；不接受“不支持追帧”的运行时降级。

存档失败或内容不兼容时，宿主弹窗只提供“删除”和“确认（保留存档）”：删除移除存档并刷新列表；确认关闭提示且不进入游戏。

## 当前事实与问题

- 当前 `GameEngine.CreateSaveData()` 返回最近无阻塞稳定快照，`GameSnapshot` 格式包含 `NodeId`、`EntryIndex`、Save scope 变量和完整 `SceneState`。
- Runtime 在读取完整快照后由宿主重放 `SceneState` 到平台展示端；这会随 effect、动画、粒子和未来媒体状态扩张存档格式与恢复契约。
- 引擎已在稳定边界避免保存阻塞原语的中途状态，但尚未记录跨节点的有序分支轨迹，也没有统一的原语 Replay 协议。
- Graph 内节点 ID 被视为唯一；节点持有其 galgroup 资源引用。存档应以 `GraphId` 与节点 ID 定位内容，不重复保存资源 ID。

## 范围

- 定义 replay-save 数据：格式版本、`GraphId`、场景缓存锚点、有序局部执行轨迹、最终 `NodeId + EntryIndex`、Save scope 变量和发布内容哈希；不保存用户态 `SceneState`。
- 在最近无阻塞稳定状态持续维护从最近场景缓存锚点到当前位置的局部轨迹；轨迹保留重复执行，并以出口和 group 内条件 entry 的结果消除歧义。
- 定义并接入原语 Replay 协议。每个已注册原语必须有确定的 Replay 行为：立即重建其场景结果、按轨迹选择，或作为明确无副作用的完成；不存在“不支持”的运行时分支。
- 追帧先恢复 Save scope 变量，再只执行原语的 Replay 行为；不再次执行存档变量写入、玩家进度写入、外部回调或媒体实际播放。
- 追帧失败、轨迹无法匹配或内容版本不兼容时，把可判定失败传递给宿主；Sample/Avalonia 存档 UI 提供“删除/确认（保留）”处理。
- 为未来多 graph 预留 `GraphId`，但本期继续支持单 graph 默认值，不实现章节选择 UI 或多 graph 运行编排。
- 编译期为每个可达 graph group node 生成其“进入 group 前”的场景基线缓存；缓存索引以 `(GraphId, NodeId)` 定位，场景值按规范化哈希去重。复用同一 galgroup 资源的多个节点默认拥有独立索引，不按资源 ID 盲目共享上下文。
- 补充 Runtime、存储、Sample host 的自动化测试和格式兼容测试。

## 非目标

- 不实现多 graph 的内容选择、章节菜单或跨 graph 跳转。
- 不保证精确恢复动画帧、粒子年龄、媒体播放时间或 Task 状态；这些由原语的 Replay 语义决定。
- 不允许通过缺失 Replay 实现静默回落到旧的完整 `SceneState` 恢复。
- 不在本 feature 中改变 Sample Debug 菜单、日志面板或进程重启调试行为。

## 验收标准

- 正常存档不再序列化 `SceneState`，但在同一内容版本下读档后可通过原语 Replay 恢复等价的可见场景与后续剧情逻辑。
- Save scope 变量在读档前恢复；追帧期间变量原语不会二次写入或污染持久状态。
- 场景缓存锚点加局部轨迹可重放重复访问的节点、分支与选择；最终定位精确到 graph node 和 group entry，且不需要从 graph 根节点重演整段历史。
- 每个 Runtime 可执行原语都拥有可测试的 Replay 策略；遗漏实现必须在注册、编译或测试门禁中失败。
- 发布内容哈希、轨迹或资源解析失败会得到可识别的存档加载失败；Sample UI 可删除该存档或保留并退出提示。
- 现有单 graph 存档使用默认 `GraphId`；格式为将来多 graph 解析预留字段。

## 约束与默认

- `GraphId` 在当前单 graph 内容中使用稳定默认值；真正的多 graph catalog 留给后续 feature。
- 轨迹必须保留重复执行，并覆盖缓存锚点之后的跨节点出口和 group 内条件 entry 的执行结果；最终位置另存 `(NodeId, EntryIndex)`。仅记录节点序列无法在跳过变量写入时重建历史条件结果。
- 节点到 galgroup 资源的映射继续由 graph 清单负责；存档不冗余资源 ID。
- 发布内容哈希由编译器对可运行内容清单生成：graph、已编译 galgroup、资源元数据及其稳定路径/内容标识共同参与；不兼容不尝试猜测路径。
- v1 为每个可达 graph group node 输出缓存，以避免运行时搜索最近 anchor；场景缓存值去重，避免同一基线随节点数线性膨胀。存档轨迹只覆盖缓存锚点之后的局部段，不再随完整游戏流程线性增长。
- Runtime/Core 继续与 Avalonia 和具体文件存储解耦；弹窗及“删除/保留”交互属于宿主 UI。
- 不考虑向后兼容：旧 `GameSnapshot` 格式直接判为不可加载，不提供迁移或旧读取器。

## 开放问题

1. 编译器如何证明每个 graph node 的进入场景基线在所有前驱路径下等价；不能证明时是构建失败，还是要求作者插入明确的场景同步节点？
2. group 内条件 entry 的 trace 编码：记录每次条件求值结果、实际执行的 entry ID，还是记录完整的 replay 指令流？
3. 非 group node（例如等待选项的 branch）达到稳定状态时，缓存锚点选择为前一个 group 起点还是也为此类 node 生成空/控制状态缓存？

## 相关链接

- [Runtime 规范](../../docs/spec/runtime.md)
- [架构规范](../../docs/spec/architecture.md)
- [原语模块 Runtime 设计](../../docs/design/primitive-module-runtime-design.md)
- [编译内容管线](../F-20260929-01-compiled-content-pipeline/feature.md)
- [GameSnapshot](../../src/Shared/GalNet.Core/Runtime/GameSnapshot.cs)
- [GameEngine](../../src/Shared/GalNet.Runtime/Engine/GameEngine.cs)
