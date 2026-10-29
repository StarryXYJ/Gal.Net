# L-20261029-04：缩小场景预览时也要限制效果处理像素

- 状态：confirmed
- 来源：2026-10-29 Sample 保存与截图卡顿跟进（独立修复）
- 关联：docs/spec/control.md，SceneLayerHost，SceneRenderPipeline

## 现象与根因

只把 RenderTargetBitmap 或 Image 缩小，仍可能在 UI 线程内运行完整逻辑分辨率的 CPU 效果链。场景自定义绘制操作内部自行分配纹理，外层缩放不约束它的像素预算。

## 正确做法

捕获入口分别传入逻辑尺寸和像素尺寸；图层原生大小、X/Y、粒子坐标继续用原逻辑空间，绘制画布缩放到输出像素空间。层效果与场景效果的中间纹理都使用预览像素尺寸。正式截图保留原分辨率，PNG 编码在渲染后移到后台。同一截图弹窗的两种界面包含状态可以分别缓存。

## 验证与禁止做法

SceneLayerHostTests.Bounded_snapshot_preserves_native_texture_coordinates_and_bounds_effect_work 验证非零位置的 Native 图片和效果源纹理尺寸；ScreenshotEntryTests.BoundedCaptureIncludesControlsPreservesSceneAndLeavesFullExportUnchanged 验证界面叠层、后台编码和后续完整导出。禁止仅凭小图片的输出尺寸宣称渲染计算已缩小；禁止把低分辨率预览用于正式导出。
