---
id: F-20260929-02-particle-system-v2
title: 模块化 2D 粒子系统 v2
type: feature
status: implementation
created: 2026-09-29
updated: 2026-09-29
---

# 模块化 2D 粒子系统 v2

## 原始目标

参考 Unity、Godot 与 After Effects 的 2D 粒子设计，在不破坏现有 `particle.play` 内容、存档和重放语义的前提下，为 GalNet 建立可逐步扩展的模块化粒子定义，并开始实现第一批高价值效果。

## 范围

- 保留 v1 平铺 JSON 和现有 C# 构造调用。
- 引入带版本号的 Emission 与 Shape 模块。
- 支持按时间触发的 burst。
- 支持 Point、Box、Circle、Line 四种发射形状。
- 保持 emitter 级存档：只保存定义、动画值和排序，读档后确定性重启 emitter，不保存单颗粒子。
- 为解析兼容、burst 时序、上限和形状渲染添加测试。

## 非目标

- 本阶段不实现碰撞、子发射器、轨迹、mask emission、GPU simulation。
- 本阶段不保存单颗粒子的年龄、位置或随机数状态。
- 本阶段不一次性实现所有随机范围、生命周期曲线、flipbook 和混合模式。

## 验收标准

- 旧 JSON 未声明模块时保持原有“屏幕顶部随机 X”行为。
- v2 JSON 可声明 `emission.rateOverTime`、`emission.bursts` 和 `shape`。
- burst 在跨过指定时间时只触发一次，并遵守 `maxParticles`。
- 四种 Shape 的采样由 emitter seed 驱动，重放结果可确定复现。
- Runtime snapshot 往返后保留 v2 模块定义。
- 受影响测试与构建通过，已知无关失败单独记录。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [渲染/粒子计划](../../docs/design/render-effects-pipeline-plan.md)
- [Runtime 规范](../../docs/spec/runtime.md)

