# 视频图层实现总结

完成日期：2026-10-10。视频功能已实现并通过针对性验证；整体 feature 保留 `validation-blocked`，原因是既有架构依赖门禁失败。

2026-10-10 用户反馈测试通过并授权整理、提交；[提交前复查](review.md) 结论为 `pass-with-follow-up`，未发现阻止视频功能提交的问题。保留既有门禁失败及首期限制。

## 结果

`video.play` 创建真正的场景视频 Layer，LibVLC 解码帧经过现有 Skia 排序、变换、透明度、Layer effect 与 ScenePost 管线。阻塞播放的每次左键推进寻找当前实际位置之后的下一时间戳；连续点击同时考虑已请求但尚未生效的 seek。无下一点或目标达到/超过已知时长时结束，视频自然结束也继续剧情。

`video.stop` 按 handle 停止播放。不同 handle 独立，同 handle 替换；隐藏/替换图层、播放失败、取消和会话销毁均清理。待加载源提前登记，隐藏或替换会取消资源加载，旧加载不能晚发布。播放 identity 防止旧完成误删新图层，帧租约防止渲染线程访问已释放纹理。LibVLC callback 只完成信号，停止和释放发生在回调线程外。

Layer 保存视频源标记和展示定义；非阻塞视频读档从零重放，阻塞视频仍使用此前稳定快照。播放器、播放 identity、当前帧与游标不写入存档。恢复入口也重放静态图层，使恢复的视频、普通图层和效果使用同一场景状态。

## 修改模块与样例

- Core/Runtime：Layer 视频标记及快照复制。
- Presentation.Abstractions/Builtins：`IVideoPresenter`、播放请求、seek cursor、entry 参数、primitive 生命周期与恢复。
- Avalonia.Rendering/GameView：动态帧源、帧租约、解码宿主与 Layer 替换/隐藏释放。
- Sample：视频宿主组合和异步销毁。
- `GameTestCase`：`niko.mp4.meta`，intro 的阻塞分段播放、非阻塞变换播放和显式停止。用户提供的视频文件未修改。

## 验证证据

| 验证 | 结果 |
| --- | --- |
| Core tests | 57 通过 |
| Runtime tests | 35 通过 |
| Builtins tests | 51 通过 |
| Presentation tests | 77 通过 |
| Integration tests | 34 通过 |
| Native video opt-in tests | 4 通过 |
| Architecture tests | 24 通过、1 既有失败 |
| Avalonia Sample build | 成功，0 errors |
| Editor CLI 编译 Raw 示例 | 成功，4 groups |
| Headless 示例（提供推进和选择输入） | 运行至 smoke test complete，exit 0 |
| 修改文件 whitespace / IDE0005 | verify-no-changes 通过 |

共 258 项功能/运行时测试通过；另有 24 项架构断言通过。Native smoke 使用用户的 `niko.mp4`（54.869 秒），解码出 1280×738 帧，经场景管线输出 [video-smoke.png](../../artifacts/video-smoke.png)。已检查图像包含视频内容。Native tests 还验证连续 seek 后结束、跳至尾部后自然结束，以及资源加载期间 hide/replace 立即取消。

Native 测试通过设置 `GALNET_VIDEO_ASSETS`、`GALNET_VIDEO_LIBVLC` 后运行 `dotnet test test/GalNet.Presentation.Tests/GalNet.Presentation.Tests.csproj --filter FullyQualifiedName~VideoNativeSmokeTests`。测试不启动游戏窗口；原生日志仍有 vout “Failed to set on top” 及快速 seek/关闭期间 decoder 警告，所有解码、结束与清理断言通过。

## 既有阻塞与限制

架构失败为 `GalNet.Primitives.Builtins_UsesOnlyAllowedProjectDependencies`：项目直接引用 `GalNet.Runtime`，白名单不允许该引用。`git show HEAD:src/Shared/GalNet.Primitives.Builtins/GalNet.Primitives.Builtins.csproj` 已包含此引用，现有音频模块使用 `AudioRuntime`；本 feature 没有改 csproj 或白名单，未通过放宽测试掩盖问题。正式收尾需另行协调音频结构与白名单。

当前使用 CPU 解码帧上传，未承诺零拷贝或多个高清视频的性能。真实视频验证在 Windows 完成，未进行其他平台及人工窗口观看验收。视频原声直接由 LibVLC 输出，尚未接入 AudioRuntime 轨道。首期不提供暂停、循环或保留末帧。

## 文档与经验

- [Entry 参数与推进语义](../../docs/spec/entry-types.md)
- [Runtime 与稳定存档](../../docs/spec/runtime.md)
- [平台视频与渲染边界](../../docs/spec/control.md)
- [视频加载与帧生命周期 lesson](../../.agents/lessons/L-20261010-01-video-layer-lifetime.md)

计划偏差：补充加载期取消的真实宿主回归测试；整体架构门禁的既有失败保留为外部收尾阻塞。没有改变用户已有的 ADR 删除或音频行为。
