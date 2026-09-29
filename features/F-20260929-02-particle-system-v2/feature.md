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

参考 Unity、Godot 与 After Effects 的 2D 粒子设计，为 GalNet 建立可逐步扩展的模块化粒子定义，并把持续发射与一次性爆发拆成语义明确的原语。项目仍处于快速迭代期，本 feature 不承担旧粒子 JSON 兼容。

## 范围

- `particle.play` 只创建按 rate 持续发射、可由 `particle.stop` 停止的持久 emitter。
- `particle.burst` 只触发一次指定数量的瞬时粒子，不创建 Runtime 持久状态。
- 持续时间由流程或 animation plan 编排 `play` / `stop`，不是粒子参数或可动画属性。
- animation plan 可在指定帧触发 `particle.burst`，循环 plan 自然形成连续 burst。
- 支持 Point、Box、Circle、Line 四种发射形状。
- 保持 emitter 级存档：只保存定义、动画值和排序，读档后确定性重启 emitter，不保存单颗粒子。
- 为解析兼容、burst 时序、上限和形状渲染添加测试。

## 非目标

- 本阶段不实现碰撞、子发射器、轨迹、mask emission、GPU simulation。
- 本阶段不保存单颗粒子的年龄、位置或随机数状态；burst 中途存档后按一次性非阻塞视觉的最终状态处理，不恢复。
- 本阶段不一次性实现所有随机范围、生命周期曲线、flipbook 和混合模式。

## 验收标准

- `particle.play` 使用 `rate` 并进入 active emitter snapshot；`particle.stop` 移除它。
- `particle.burst` 使用 `count`，只调用展示端一次且不进入 snapshot。
- animation plan 的 burst 事件可随循环重复；跳过 plan 不补发尚未发生的 burst。
- 四种 Shape 的采样由 emitter seed 驱动，重放结果可确定复现。
- Runtime snapshot 往返后保留持续 emitter 定义；burst 不参与往返。
- 受影响测试与构建通过，已知无关失败单独记录。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [渲染/粒子计划](../../docs/design/render-effects-pipeline-plan.md)
- [Runtime 规范](../../docs/spec/runtime.md)
