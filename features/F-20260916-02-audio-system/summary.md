---
feature: F-20260916-02-audio-system
status: completed
completed: 2026-09-30
---

# 音频系统首期实现总结

## 交付

- `ProjectSettings.AudioTracks` 定义默认 `bgm`、`sfx`、`vocal` 轨道；编辑器可增删改，并在保存时校验轨道 ID、固定 bus、增益、声部与队列限制。
- `AudioRuntime` 拥有 track queue、voice generation、放置策略、暂停/继续、清队、淡化意图及轨道 SEffect 生命周期；`IAudioPlaybackHost` 是平台端口。
- Avalonia host 以 `IAssetManager` 获取资源并在会话临时目录实体化，按 voice 管理 LibVLC player，支持线性淡化和 `seffect.gain`。
- `dialogue.text` 与 `nvl.append` 保持纯文本；`dialogue.textVoice`、`nvl.appendVoice` 编译为非阻塞的 `audio.play(replaceIfDifferent)` 后接原文本。
- `GameTestCase` 使用 `Assets/心.mp3` 演示 BGM 淡入、`seffect.gain` 启停与中途淡出。

## 验证

- `dotnet test test/GalNet.Runtime.Tests/GalNet.Runtime.Tests.csproj --no-restore`：34 通过。
- `dotnet test test/GalNet.Primitives.Builtins.Tests/GalNet.Primitives.Builtins.Tests.csproj --no-restore`：43 通过。
- `dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-restore`：33 通过。
- `dotnet build src/Editor/GalNet.Editor/GalNet.Editor.csproj --no-restore` 与 Avalonia Sample build 通过。
- Headless 编译 `GameTestCase` 成功，生成四个 `.galgroup`。

## 已知边界

- 轨道选项尚未成为 entry 编辑器的项目上下文下拉；`trackId` 目前是文本字段，运行时会诊断未知轨道。
- 本期只实现 host 可验证的 `seffect.gain`；SEffect catalog、参数编辑器、动画及其他 DSP 后续另立 feature。
- 不实现 bus CRUD、玩家音量设置、音频存档恢复、角色默认 vocal track、seek、自然结尾交叉淡化或无缝循环。

正式长期设计见 [音频系统设计](https://github.com/StarryXYJ/Gal.Net/blob/f17a5934c291d5669ea6615b928321bdf9a2aa02/docs/design/audio-system-design.md)，过程设计见 [design.md](design.md) 与 [phase-plan.md](phase-plan.md)。
