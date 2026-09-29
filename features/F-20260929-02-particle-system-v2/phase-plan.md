---
feature: F-20260929-02-particle-system-v2
updated: 2026-09-29
---

# 实施计划

## Phase 1 — Play/Burst 原语与 Shape

**状态：verified**

- `particle.play` 只承载 rate，`particle.burst` 只承载一次 count；不保留旧 JSON 兼容层。
- 在 Avalonia CPU simulation 中实现一次 burst 与 Point/Box/Circle/Line 采样。
- animation plan 支持 burst 帧事件和循环重触发，跳过不补发瞬时事件。
- 固化 stop、maxParticles、seed 确定性和 emitter 级重放边界。
- 更新 Sample，并添加 Core 解析、snapshot、Primitive、timeline 与渲染模拟测试。

退出条件：两个原语的 Runtime/Presenter/存档边界清晰，timeline 重复 burst 可验证，Sample 使用新 API，受影响项目可构建。

验证证据（2026-09-29）：

- `GalNet.Primitives.Builtins`、`GalNet.Avalonia.GameView`、`GeneralTest` 构建通过。
- 粒子、Runtime snapshot、entry catalog 与 primitive/timeline 定向测试 40/40 通过，其中包含循环 plan 每轮重触发 burst。
- GeneralTest 全量 249/250 通过；唯一失败为既有的 `LastDockLayout_RoundTripsAsAString` CRLF/缩进差异，与本 feature 无关。
- `GalNet.Sample.Headless` 构建通过，0 warning / 0 error。

## Phase 1.5 — 粒子 Flipbook

**状态：verified**

- 在共享 emitter definition 中加入 sprite-sheet 网格与播放配置，不新增粒子原语。
- 每颗粒子独立支持固定 `framesPerSecond` 和 `cyclesOverLifetime` 两种推进方式。
- 支持 loop、非 loop 与 seed 驱动的随机起始帧，并保持单次 atlas batch。
- 为 play/burst 共用定义、snapshot 往返、帧计算和 source rect 添加测试与 Sample。

退出条件：两种播放模式、循环边界、随机起始帧和 atlas source rect 均有自动化证据，存档与发射原语语义不变。

验证证据（2026-09-29）：

- `GalNet.Core` 与包含 Rendering/GameView 的 `GeneralTest` 构建通过。
- 粒子 definition、snapshot、primitive 与 renderer 定向测试 27/27 通过；覆盖固定 FPS、生命周期循环、loop/clamp、随机起始帧确定性和实际 atlas 像素取帧。
- GeneralTest 全量 253/254 通过；唯一失败仍为既有的 `LastDockLayout_RoundTripsAsAString` CRLF/缩进差异。
- `GalNet.Sample.Headless` 构建通过，0 warning / 0 error；Sample burst 使用 2x2 flipbook 素材与生命周期选帧。

## Phase 2 — Initial、Motion 与 Lifetime

**状态：planned**

- 增加可版本化随机范围类型。
- 支持 lifetime、velocity、size、rotation 初始范围。
- 支持 drag、rotation、opacity 与更多 lifetime curves。

退出条件：常见雪、雨、火花、烟雾、爆炸无需自定义 shader 即可表达。

## Phase 3 — Renderer 与高级模块

**状态：planned**

- 增加 blend mode 与 alignment。
- 按真实需求评估 trail、collision、mask、sub-emitter 与 GPU simulation。
- 配套编辑器 schema、预览与性能预算。

退出条件：Renderer 扩展不破坏统一 scene composition 和 effect stage 顺序。
