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

## 兼容策略

`ParticleEmitterDefinition` 暂时保留 v1 平铺字段，避免破坏已有内容、动画属性和 snapshot。v2 模块作为可选数据追加：

- 未声明 `emission` 时，连续发射率继续读取 `emissionRate`。
- 未声明 `shape` 时，继续使用历史的屏幕顶部随机 X 发射。
- 声明模块后，渲染器读取模块的有效值；`emissionRate` 动画仍控制运行时连续发射率。
- snapshot 继续保存完整 emitter definition，因此 v2 模块自然参与存档往返；活粒子仍不进入 Runtime 状态。

这是一段有意保留的迁移期。等编辑器和内容全部输出 v2 后，再决定是否废弃平铺字段，不能在读取路径中静默改变旧内容视觉结果。

## 模块路线

```text
ParticleEmitterDefinition v2
├─ Main: duration / looping / seed / maxParticles / simulationSpace
├─ Emission: rateOverTime / bursts[]
├─ Shape: point / box / circle / line / mask
├─ Initial: lifetime / velocity / size / rotation / color ranges
├─ Motion: gravity / drag / noise / radial / orbit / attractor
├─ Lifetime: size / color-opacity / velocity / rotation curves
└─ Renderer: texture / blend / flipbook / alignment / trail
```

## Phase 1 决策

首阶段只实现 Emission + Shape。它们能覆盖爆炸、火花、喷泉、区域飘落和路径发射，又不要求改变现有 atlas batch 或 Runtime 动画协议。

- burst 以 emitter 启动后的秒数触发，按时间排序，每项只触发一次。
- Point 使用固定坐标；Box 在中心矩形内均匀采样；Circle 按面积均匀采样；Line 在线段上均匀采样。
- 所有采样共享 emitter 的 seed RNG，故相同 definition 从头重放时结果一致。
- stop 后不再触发连续发射或未来 burst，已有粒子自然耗尽。

## 后续阶段

1. 引入通用 `FloatRange` / `ColorRange` 和 Initial 模块。
2. 增加 drag、angular velocity、opacity/rotation/velocity lifetime curves。
3. 增加 blend mode 与 flipbook；保持 Skia atlas 批量提交。
4. 再评估 collision、attractor/vortex、trail、mask emission、sub-emitter 与 GPU simulation。

