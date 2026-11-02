---
status: confirmed
feature: F-20261102-03-presentation-library
---

# 复用场景渲染器绘制 UI 遮罩时必须显式透明合成

现象：单独增加位于对白和提示卡上方的 SceneLayerHost，会把整个页面盖成黑色，即便没有遮挡区域。

根因：原 SceneRenderPipeline 的 CPU 纹理与 GPU 目标默认 Clear(Black)，这是普通场景的正确背景；上层覆盖面不能沿用这个假设。GPU 直接清目标还会清除已绘制的下层内容。

修正：SceneRenderOptions.TransparentBackground 让 CPU 初始纹理透明；GPU 在独立透明 surface 合成后 SrcOver 绘入目标，不清目标。普通场景仍默认黑底。空覆盖面不提交绘制操作。

验证：PresentationShaderPixelsTests 比较空场景的普通/透明模式 alpha，前者全 255、后者全 0；羽化遮罩像素与 contact sheet 另验。GPU 实现已接线，但本条不把 CPU 像素证据当成原生 GPU 验收。

适用：在对白、通知或其他 UI 上方复用 scene renderer 的覆盖控件。禁止只靠新建高 Z 容器或 Clear(Transparent) 清共享 GPU 目标实现覆盖。

来源：features/F-20261102-03-presentation-library；docs/spec/runtime/entries.md。
