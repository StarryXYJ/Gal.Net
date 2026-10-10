# 视频图层设计

用户已授权实现。视频采用真正的场景 Layer：解码帧进入 Skia 场景合成，因此排序、变换、透明度、Layer effect 和 ScenePost 沿用现有管线。独立原生 VideoView 无法参与同一纹理链，不采用该方案。

`video.play` 接收 `asset`、`handleId`、`blocking`（默认 true）、`skipPoints`（秒，严格递增非负有限数组），以及现有图层的 `transform`、`z`、`opacity`、`displayMode`。不同 handle 可独立播放；同 handle 替换。`video.stop(handleId)` 停止并移除对应视频。结束、失败和取消均清理；不保留末帧。视频原声由视频后端直接播放，首期不加入 AudioRuntime 轨道。

平台无关 `IVideoPresenter` 提供播放到结束的 Task、分段推进和按 handle 停止。每次播放携带独立 playback ID，防止旧播放的异步结束清理新图层。Builtin instance 先登记 Runtime Layer，再启动 presenter；分段跳转不完成阻塞实例。无 presenter 的无界面宿主安全完成；实际播放失败产生诊断。

Avalonia 宿主通过 AssetManager 获取视频字节并实体化到独立临时目录。LibVLC 使用格式和像素回调，按媒体实际尺寸生成帧。发布帧与渲染读取通过租约同步，避免 GPU/截图读取已释放像素；回调不操作 UI，也不在 LibVLC 事件线程停止或销毁播放器。UI 帧失效通知合并，防止逐帧消息积压。

左键从实际播放位置与最后请求跳转位置的较大值寻找下一个点，避免连续点击在 seek 尚未生效时重复跳到同一点。没有下一个，或目标达到/超过已知时长时停止。加载未就绪时不执行 seek；无法 seek 的后端必须诊断并退出，不能永久阻塞。

Layer 仅持久化视频源标记和资源/图层定义，不保存解码器或游标。阻塞期间继续返回之前稳定快照；非阻塞视频随稳定场景保存，读档从零恢复。`layer.hide`、`layer.show` 和 `layer.replace` 对视频源执行释放；旧播放不得移除替换后的图层。

兼容性：保留现有 `asset` 参数；新增 `handleId` 默认 `video`，旧单视频脚本仍可编译。停止默认同一 handle。新增视频状态缺省 false，不影响旧快照。

验证覆盖推进边界、自然结束、失败/取消、连续 seek、多个 handle、替换竞争、动态图层排序/透明度/效果、快照与恢复，以及 `Assets/niko.mp4` 的真实解码 smoke。性能限制：首期 CPU 解码回调上传纹理，不承诺零拷贝或任意数量高清视频。
