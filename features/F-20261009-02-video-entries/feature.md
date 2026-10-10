---
id: F-20261009-02-video-entries
title: 剧情视频播放与分段跳过 entry
type: feature
status: validation-blocked
created: 2026-10-09
---

# 剧情视频播放与分段跳过 entry

## 用户原始目标

“设计视频相关的 entry，大概就开始播放、结束播放，然后开始播放有个列表参数，如果阻塞，那么根据列表的时间戳每次左键跳转到下一个，如果下一个不存在或超过视频时长就结束播放，这么设计如何。”

## 已确认需求与授权

- 用户确认按当前播放位置寻找严格更大的下一时间戳。
- 用户确认视频采用真正场景图层的方向，并授权开始实现；提供 `GameTestCase/Assets/niko.mp4` 用于验证。

## 实施范围与默认决定

- `video.play` / `video.stop` 具有实际 Runtime 行为，按 `handleId` 管理独立视频图层。
- `video.play` 接收 `asset`、`blocking`、`skipPoints` 和现有 Layer 展示参数。
- 默认 `handleId=video`、`blocking=true`、`skipPoints=[]`；时间单位秒，要求非负、有限、严格递增。
- 左键从实际位置与上次请求 seek 位置的较大值向后寻找下一个点。无下一个点、目标达到/超过时长时结束；加载未就绪时不执行 seek。
- 不同 handle 可同时播放，同 handle 替换；自然结束、stop、取消、失败或隐藏/替换源均释放播放资源并移除对应视频源，不保留末帧。
- 非阻塞视频不消费推进输入，保持普通图层动画、排序、变换、透明度与效果语义。
- 阻塞播放沿用此前稳定存档；非阻塞视频保存图层定义，读档从零播放，不保存实时游标。
- 上述默认值和结束策略为授权后的实现决定，用户尚未逐项确认；后续可按反馈修订。

## 非目标

- 暂停/恢复/循环、保留末帧、字幕编辑、视频编码、Gallery 改造。
- 视频原声整合 AudioRuntime 轨道、零拷贝解码与所有平台的原生媒体实现。

## 验收标准

- 视频帧进入场景图层合成，参与排序、变换、透明度和纹理效果。
- 分段跳转保持阻塞；自然结束或停止后继续剧情。
- 多 handle 与替换生命周期互不误清理，旧播放完成不能移除新图层。
- 加载期间隐藏/替换会取消加载，不会在稍后重新覆盖画面。
- 存档和恢复保持上述稳定语义。
- 测试视频实际解码并输出可检查的场景帧；Raw 示例可编译，Headless 可运行至结尾。

## 约束

Core/Runtime 不依赖 Avalonia、LibVLC 或路径；平台从 `IAssetManager` 获取媒体。参数使用 JSON 类型。视频属于现有 Layer 类型，平台解码资源与 playback identity 不写入存档。

## 相关链接

- [提交前复查](review.md)
- [实现总结与既有架构门禁阻塞](summary.md)
- [设计](design.md)
- [阶段计划](phase-plan.md)
- [Entry 格式与推荐 ID](../../docs/spec/entry-types.md)
- [Runtime](../../docs/spec/runtime.md)
- [展示边界](../../docs/spec/control.md)
- [渲染效果长期计划](../../docs/design/render-effects-pipeline-plan.md)
