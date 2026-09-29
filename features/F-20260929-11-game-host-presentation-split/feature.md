---
id: F-20260929-11-game-host-presentation-split
title: 游戏宿主与 Avalonia 展示职责拆分
type: refactor
status: implementing
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# 游戏宿主与 Avalonia 展示职责拆分

## 目标

完成维护性路线图 Phase 4b，缩小 `SampleGameSessionService` 与 `AvaloniaGamePageView` 的职责面，使资源生命周期、存档会话、运行编排、持久场景重放以及各类 presenter 可独立验证，同时保持现有 Sample、Editor Preview 和 Runtime 行为。

## 范围

- 固定会话停止、prepared run、读档重放、资源释放和 UI 调度语义。
- 将 Avalonia 对话/选择、图层、动画和粒子展示拆成独立 presenter，并共享 UI dispatcher adapter。
- 从 Sample session 抽取资源 scope、存档会话、持久场景 restorer 和 runner 协作。
- 补充 `AssetManager` 并发 acquire、取消和释放边界测试；仅在一致性边界明确时调整实现。
- 保持 `CompositeGameView` 只负责 entry module 冻结、primitive 查找与单次 dispatch。

## 非目标

- 不重做 Sample 页面交互、导航、存档格式或 Runtime 状态模型。
- 不修改粒子、动画和音频的产品语义，不把 Sample 的文件系统与媒体策略下沉到 Runtime。
- 不因文件较大而拆散 `AssetManager` 的缓存、引用计数和释放原子性。
- 不在本 feature 中移动项目目录或修改程序集/命名空间布局。

## 验收标准

- `AvaloniaGamePageView` 不再直接实现全部细分 presenter；各 presenter 有独立职责和测试。
- `SampleGameSessionService` 主要保留页面可见命令、生命周期串行化与协作者编排。
- 读档后 effect/particle/animation 展示重放、停止和资源释放语义保持不变。
- `CompositeGameView` 的 primitive dispatch 契约和 public API 不发生行为回归。
- Presentation、Integration、Assets、Runtime/Builtins 测试与 Sample/solution Release 构建通过。
- 每个实现阶段完成后创建独立 Git 提交。

## 约束与假设

- 用户已授权按维护性路线图持续实现，并要求每阶段提交。
- 当前粒子、动画与音频 feature 可能仍会触碰相邻代码；拆分优先机械迁移和适配，不改变其协议。
- Sample 和 Editor Preview 都必须迁移到同一组 Avalonia presenter 组合方式。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [路线图 Phase 4b](../F-20260929-03-maintainability-roadmap/phase-plan.md)

