# 实现与验证

2026-10-27。音频实现已落地，相关验证通过；全仓验收保留一项 Timeline UI 失败，feature 不标为完全关闭。

## 行为

- 作者模型为实例 → Track → Bus。实例保留资源/gain/播放次数/结束处理，新增自身淡出；省略停止淡出时采用实例配置，显式 0 硬切。Track 新增顺序/随机和 None/One/All 循环策略，随机状态持久化。
- Bus 替代标量 Group，真实汇总 Track 后处理一次效果和音量。SEffect 使用 scope（track/bus）/targetId，首次添加决定顺序，更新保留顺序，停止再挂移到链尾。换类型时只保留一个逻辑活动实例，旧处理器平滑退出。
- 默认后端改为 miniaudio 设备/解码 + 托管统一 PCM 混音/DSP；删除 LibVLC 音频 host 与 EQ 模拟，视频/Gallery 保留 LibVLC。真实二阶高低通、gain 和基础房间混响，参数/启停平滑；音源结束后尾音继续，reset/读档清除旧 DSP 缓冲。
- 解码在后台，经每实例约两秒的有界缓冲交给设备；回调无文件 I/O、Runtime 锁或用户事件。后台终态释放原生解码器和租约，单项清理/观察者失败不阻断其余源；异常会汇总而非吞掉。无设备仍保留效果契约，实际播放通过 Failed 降级。
- `audio.mix.set` 保存剧情目标 gain/muted，玩家 Master/Buses/Tracks 混音独立叠加。编辑器目标选择随 scope 使用 Track 或 Bus 声明，已挂载测试覆盖只有 Bus 才存在的 room ID。

## 迁移与构建

`galnet.project` section v2：audioGroups → audioBuses，groupId → busId；效果 trackId → targetId，可显式 scope=bus；旧 attenuationDb → 真正滤波器的 q（不是等价数值映射）。旧 section v1 明确拒绝。音频 tracks/seffects 状态表 v2，新增 channels 表，旧档拒绝。GameTestCase 已同步并加入 Bus 混响示例。

miniaudio 0.11.23 和固定提交 stb_vorbis 的源码、授权、SHA256 随 native/README 保存。Windows x64 开发构建需 MSVC C++ Build Tools；静态 CRT 的 DLL 自动复制到输出/发布，玩家不需要编译器。WAV/MP3/Ogg Vorbis/FLAC 支持，设备的其他平台实现尚未交付。Windows ReadyToRun 发布锁图同步。

## 验证证据

采用 AVALONIA_TELEMETRY_OPTOUT=1、-m:1、UseSharedCompilation=false；解决方案构建禁用 analyzer 执行（现有 nullable/XAML 警告保留）。最终使用 artifacts/audio-isolated 独立输出，避免覆盖正在运行的 Editor.exe/DLL；默认契约从该次构建产物补入对应测试输出。

| 检查 | 结果 |
| --- | --- |
| 新音频 DSP/状态/解码/异常清理 | 20 通过 |
| 显式 Windows 默认设备播放、Bus 混响和播放中滤波启停 | 1 实际执行并通过 |
| Primitives.Builtins | 307 通过 |
| Presentation | 261 通过 |
| Architecture | 52 通过 |
| Editor 音频相关挂载/历史/配置 | 6 通过 |
| Editor.Shared | 448 通过 |
| Storage.FileSystem | 24 通过 |
| Integration | 132 通过 |
| GameHost | 6 通过 |
| 解决方案独立输出构建 | 0 错误 |
| Sample Debug 普通 publish smoke | 成功，包含 galnet_miniaudio.dll（774656 bytes） |
| 新音频文件 whitespace / git diff --check | 通过 |

较早全量 Editor 回归 324/326：TimelinePropertyTree 的文件移动权限错误在单项复核通过；TimelineEventQueryPickerKeepsPositionAndPropertyCreationUsesFrameZeroBaseline 在单项复核仍于 TimelineEventQueryTests.cs:55 空引用失败（读取异步 Flyout 时为空，未在本 feature 继续定位）。该用例不含音频路径，未修改相关 Timeline 实现或隐藏该失败。工作区同时存在其他编辑器改动，均保留。

架构测试依赖清单同时补齐既有 Editor.Shared.Tests → Editor.DefaultContracts 引用，未扩大生产层依赖。普通输出目录的最后覆盖尝试被正在运行的 Editor 锁定；保留进程，独立输出验证成功，不把锁定误报为代码编译失败。

## 边界

混响为基础算法空间，不是卷积 IR。没有公开 Voice/Send、跨 Bus 路由、Sidechain 或 VST；只有 Windows x64 设备验收。没有保存精确游标、DSP 尾音或包络时钟，也未承诺无缝队列/循环或任何负载下的绝对无断音。尚未对混响进行专业主观调音或长时间压力测试。

当前接口与示例见 [音频手册](../../docs/manual/audio-mixing.md)、[Runtime](../../docs/spec/runtime.md)、[架构](../../docs/spec/architecture.md)。
