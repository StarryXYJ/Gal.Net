---
feature: F-20260929-02-particle-system-v2
updated: 2026-09-29
---

# 实施计划

## Phase 1 — 兼容模型、Emission 与 Shape

**状态：verified**

- 扩展 Core definition，解析 v1 平铺 JSON 与 v2 模块 JSON。
- 在 Avalonia CPU simulation 中实现 bursts 与 Point/Box/Circle/Line 采样。
- 固化 stop、maxParticles、seed 确定性和 emitter 级重放边界。
- 添加 Core 解析、snapshot 往返与渲染模拟测试。

退出条件：旧内容视觉起点不变，v2 模块测试通过，受影响项目可构建。

**验证（2026-09-29）：** 新增解析、JSON 往返、burst 时序/上限与 Point 渲染测试 5/5 通过；粒子 Runtime、Presenter 与 snapshot 定向回归 19/19 通过；Headless Sample 构建 0 warning / 0 error；全量 `GeneralTest` 245/246，通过项包含全部新增测试，唯一失败仍为未触及的 `EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 换行/缩进差异。

## Phase 2 — Initial、Motion 与 Lifetime

**状态：planned**

- 增加可版本化随机范围类型。
- 支持 lifetime、velocity、size、rotation 初始范围。
- 支持 drag、rotation、opacity 与更多 lifetime curves。

退出条件：常见雪、雨、火花、烟雾、爆炸无需自定义 shader 即可表达。

## Phase 3 — Renderer 与高级模块

**状态：planned**

- 增加 blend mode、flipbook 与 alignment。
- 按真实需求评估 trail、collision、mask、sub-emitter 与 GPU simulation。
- 配套编辑器 schema、预览与性能预算。

退出条件：Renderer 扩展不破坏统一 scene composition 和 effect stage 顺序。
