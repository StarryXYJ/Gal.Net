---
feature: F-20261009-01-track-seffects
status: completed
completed: 2026-10-09
---

# 实现总结

## 交付

- Core 增加不可变 `SEffectCatalog`，校验 stable ID、scope、必填数值和参数范围。
- `IAudioPlaybackHost` 暴露其注册 catalog；`AudioRuntime` 拒绝未知或无效轨道 effect，避免旧实现的 host 静默忽略。
- Avalonia host 增加构造期 processor registry。默认处理器为 `seffect.gain`、`seffect.lowPass` 和 `seffect.highPass`，也可注入平台/项目专用 processor。
- 低/高通将各 attachment 的衰减叠加到 LibVLC equalizer 的频段；移除 attachment 后重新聚合。淡化同时保持 effect gain。
- `GameTestCase` BGM 增加低通、高通的 apply/stop 可听示例。

## 验证

- `dotnet test test/GalNet.Core.Tests/GalNet.Core.Tests.csproj --no-restore`：57 passed。
- `dotnet test test/GalNet.Runtime.Tests/GalNet.Runtime.Tests.csproj --no-restore`：35 passed。
- `dotnet test test/GalNet.Presentation.Tests/GalNet.Presentation.Tests.csproj --no-restore`：73 passed。
- `dotnet run --project src/Editor/GalNet.Editor.Headless/GalNet.Editor.Headless.csproj --no-restore -- build GameTestCase --output artifacts/audio-seffects-build`：成功，4 个 group。

## 边界

- LibVLC 的 equalizer 是有限频段近似，不是带 resonance 的逐样本低/高通。
- registry 在 host 创建时固定；它支持按平台组合注册，不支持剧情运行中加载任意 DLL/DSP。
- 仅 track scope；item scope、参数动画、编辑器动态表单、pan/pitch/spatial、bus/master DSP 和存档恢复仍未实现。

正式事实已同步至 [音频系统设计](../../docs/design/audio-system-design.md)、[Runtime 规范](../../docs/spec/runtime.md) 与 [Entry 类型](../../docs/spec/entry-types.md)。
