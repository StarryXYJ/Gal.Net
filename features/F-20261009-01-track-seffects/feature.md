---
id: F-20261009-01-track-seffects
title: 可注册的轨道 SEffect
type: feature
status: completed
created: 2026-10-09
---

# 可注册的轨道 SEffect

## 目标

为已交付的音频轨道扩展可注册的 SEffect descriptor 与 Avalonia processor registry，并交付实际可听的 `seffect.lowPass`、`seffect.highPass`。未知或不受宿主支持的 effect 必须在 Runtime 诊断并保持干声，而不是被静默忽略。

## 范围

- Core 定义可校验的 `SEffectDefinition` / `SEffectCatalog`。
- `IAudioPlaybackHost` 暴露自己已注册的 catalog；Runtime 在附着前验证 ID、scope 和参数。
- Avalonia host 以 processor registry 注册 `gain`、低通和高通；低/高通使用 LibVLC equalizer 的频段近似。
- 加入 catalog、Runtime 拒绝与 processor 聚合测试。

## 非目标

- item scope、参数动画、编辑器参数表单、bus/master DSP、任意插件 DLL 或 VST/AU。
- 逐样本的滤波、resonance、pan、pitch、spatial、存档恢复与音频后端替换。

## 验收

- 宿主可按构造期注册 processor；catalog 由注册项动态形成，重复 ID 失败。
- 已注册的低/高通能在播放中应用与移除；多个 effect 的 gain/EQ 贡献重新聚合。
- 不支持或参数非法的 effect 不进入轨道状态，且产生诊断。
