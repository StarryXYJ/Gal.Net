---
feature: F-20260929-02-particle-system-v2
updated: 2026-09-29
---

# 设计

## 参考模型

- Unity Particle System 把 Main、Emission、Shape、Velocity/Force/Noise over Lifetime、Color/Size/Rotation over Lifetime、Collision、Texture Sheet、Trails 与 Renderer 分成可独立启用的模块。
- Godot ParticleProcessMaterial 使用“出生时从 Min/Max 取确定性随机值，再乘生命周期 Curve”的组合；发射节点另管 lifetime、one-shot、preprocess、local coordinates 与 draw order。
- After Effects Particle Playground 区分 Cannon/Grid/Exploder 等生成器、出生属性、出生后 Gravity/Repel/Wall/Property Mapper 与粒子素材映射。

三者的共同点不是具体参数名，而是将发射时机、出生位置、初始状态、生命周期运动和渲染表现解耦。GalNet 因而采用 Main / Emission / Shape / Initial / Motion / Lifetime / Renderer 的模块边界。

## 快速迭代策略

不保留 v1 JSON 迁移层。Sample、测试和正式文档与实现同步更新，避免在尚未发布的 API 上积累双重语义。

- `particle.play`：持续 rate emitter，具有 `instanceId`，写入 Runtime scene state，可动画 `emissionRate`，由 `particle.stop` 结束。
- `particle.burst`：一次性 count 命令，无 `instanceId`、无 stop、无 Runtime scene state；展示端在粒子耗尽后自动释放。
- `duration` 不属于粒子定义。有限持续效果由普通流程或 animation plan 表达 `play → stop`。
- 重复 burst 由 animation plan 在多个帧触发，循环 plan 会在每轮重新触发事件；跳过时不补发未来 burst。

这样把持久状态与瞬时视觉明确分开：读档只重建仍在持续播放的 emitter，一次 burst 的稳定终态就是“已经结束”。

## 模块路线

```text
ParticleEmitterDefinition v2
├─ Main: seed / maxParticles / simulationSpace
├─ Emission: particle.play(rate) / particle.burst(count)
├─ Shape: point / box / circle / line / mask
├─ Initial: lifetime / velocity / size / rotation / color ranges
├─ Motion: gravity / drag / noise / radial / orbit / attractor
├─ Lifetime: size / color-opacity / velocity / rotation curves
└─ Renderer: texture / blend / flipbook / alignment / trail
```

## Phase 1 决策

首阶段实现两个原语与共享 Shape。它们能覆盖爆炸、火花、喷泉、区域飘落和路径发射，又不要求保存单颗粒子。

- 持续 emitter 由 play/stop 明确管理；有限时长由上层编排，不在 renderer 内维护第二套计时器。
- burst 在命令到达时一次发射，遵守 `maxParticles`，耗尽后自动释放。
- Point 使用固定坐标；Box 在中心矩形内均匀采样；Circle 按面积均匀采样；Line 在线段上均匀采样。
- 所有采样共享 emitter 的 seed RNG，故相同 definition 从头重放时结果一致。
- stop 后不再持续发射，已有粒子自然耗尽。

## 后续阶段

1. 引入通用 `FloatRange` / `ColorRange` 和 Initial 模块。
2. 增加 drag、angular velocity、opacity/rotation/velocity lifetime curves。
3. 增加 blend mode 与 flipbook；保持 Skia atlas 批量提交。
4. 再评估 collision、attractor/vortex、trail、mask emission、sub-emitter 与 GPU simulation。
