---
feature: F-20261009-01-track-seffects
status: confirmed
created: 2026-10-09
---

# 设计

`SEffectCatalog` 位于 Core，只保存稳定 ID、支持 scope 和参数边界。平台 host 通过 `IAudioSEffectProcessor` 注册实现，registry 同时构造 catalog；Core/Runtime 不认识 LibVLC、频段或设备。

当前只实现 `Track` scope。Runtime 在 `audio.seffect.apply` 时查询 host catalog 并校验 JSON 参数，失败即诊断并不保存 attachment；stop 保持幂等。

Avalonia 的 processor 聚合一个 voice 的所有 active attachment：`gain` 乘入 output gain，低/高通向 LibVLC equalizer 频段贡献负 dB，贡献相加后被 LibVLC 的 [-20, 20] dB 范围限制。LibVLC 可在播放中应用 equalizer，但它不是参数化双二阶滤波器，因此效果是频段近似；descriptor 不声明 resonance。

注册只在 host 创建期间进行，避免播放时并发改变 capability；未来平台可提供自己的 registry 和同 ID 的更精确 processor。作者 ID 不允许提交任意 DSP 文本。
