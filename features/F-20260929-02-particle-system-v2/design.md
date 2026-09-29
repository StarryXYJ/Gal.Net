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

## Phase 1.5 决策：粒子 Flipbook

Flipbook 是粒子 Renderer 配置，不新增原语；`particle.play` 与 `particle.burst` 共用 `parameters.flipbook`。每颗粒子根据自身年龄独立选择 sprite-sheet source rect，继续通过同一个 atlas batch 提交。

- `columns`、`rows`、`frameCount` 描述 row-major sprite sheet，帧下标从 0 开始。
- `framesPerSecond > 0` 时使用固定速率：`floor(age * framesPerSecond)`。
- 否则使用 `cyclesOverLifetime`：`floor(normalizedAge * cyclesOverLifetime * frameCount)`。
- `loop = true` 时按 `frameCount` 取模；否则钳制到最后一帧。
- `randomStartFrame = true` 时，出生时使用 emitter seed RNG 生成确定性的起始帧。
- 发射 `rate` 与 flipbook 播放速率没有共享语义；后者显式命名为 `framesPerSecond`，避免歧义。
- snapshot 只保存完整 emitter definition；不保存单颗粒子的年龄、当前帧或随机状态，读档仍从 seed 与第 0 时刻重建。

## Phase 2 决策：Initial、Motion 与 Lifetime

Phase 2 将出生随机量、出生后运动和归一化生命周期表现拆开；authoring JSON 直接采用嵌套模块，不保留 Phase 1 的平铺参数兼容层。

- `initial` 的 lifetime、velocityX/Y、size、rotation、angularVelocity、color 均使用 min/max 范围；每颗粒子出生时由 emitter seed 确定性采样一次。
- flipbook 的 `framesPerSecond` / `cyclesOverLifetime` 与 `startFrame` 同样按粒子采样范围；固定 FPS 存在且大于 0 时优先，否则使用生命周期循环次数。
- `motion.drag` 使用与帧率无关的指数阻尼；gravity、noise 与 attractor 作为加速度，radialVelocity 在出生时沿发射中心向外叠加，orbitDegreesPerSecond 绕发射中心旋转位置。
- `lifetime.size`、`opacity`、`velocity`、`rotationDegrees` 与 `color` 都按归一化年龄在线性插值。velocity 是位移倍率，不反复乘入速度，避免指数累积；rotation 是叠加到出生角度和 angular velocity 的角度值。
- opacity 曲线显式表达淡入淡出；缺省仍为从 1 淡出到 0。初始颜色与生命周期颜色逐通道相乘。
- emitter 动画继续修改 range 中心并保留半宽；存档仍只保存 emitter 定义和动画值，不保存单颗粒子。

## 后续阶段

1. 增加 blend mode 与 alignment；保持 Skia atlas 批量提交。
2. 再按真实效果需求评估 collision、trail、mask emission、sub-emitter 与 GPU simulation。
