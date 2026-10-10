# 动态视频图层的加载与帧生命周期

状态：confirmed
来源 Feature：F-20261009-02-video-entries

## 现象与根因

异步加载完成后才登记视频图层，会使加载期间的 hide/replace 找不到待播放源；加载完成后视频可能重新覆盖后来的画面。动态纹理还会被解码线程替换，直接释放旧帧可能与渲染线程读取相竞争。

## 发现与证据

代码审查确认加载后登记缺少取消路径；新增真实宿主测试使用延迟 AssetManager，在 GetFileAsync 未结束时 hide 或替换为颜色图层，证明修改后立即取消加载且不会晚发布。帧租约测试验证当前帧在发布新帧前保持可读，真实 niko.mp4 解码验证帧进入同一场景管线。

## 正确做法

先登记可释放的待播放 FrameSource；hide/replace 释放源并取消尚未完成的加载。每次播放独立 identity，旧完成只能清理自己的源。帧使用租约同步发布/读取，未被读取的 source bitmap 同样必须释放。native 完成/失败事件只发信号，Stop/Dispose 在回调之外执行；会话销毁等待所有播放器释放后再释放 LibVLC。

## 适用范围与禁止做法

适用于异步动态 Layer 源。不要在加载后无条件发布图层，不要按 handle 无条件清理旧播放，不要跨渲染线程直接释放仍可能读取的帧，不要在 LibVLC callback 内 Stop/Dispose。

关联：features/F-20261009-02-video-entries/design.md；VideoNativeSmokeTests；VideoLayerTests。
