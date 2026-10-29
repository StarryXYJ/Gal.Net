# Shader 采样需要像素与视觉双重验证

状态：confirmed
来源：features/F-20261026-03-default-visual-library

## 现象与复现

DepthBlur 的稀疏 5×5 网格采样在教室背景预览中出现重复图像条纹。SpeedLines 的定向线宽改细后，在 128×128、density=64 时所有像素 alpha=0，尽管 shader 编译完全成功。

## 已验证的原因

固定网格间隔过大使边缘与纹理产生可见重复采样。定向线在该分辨率下的 lane 坐标采样点固定落在细线范围之外，导致整条线消失；只检查编译或单个高分辨率截图不能发现这个问题。

## 做法

- 在低分辨率上验证动态程序图层存在非零与零 alpha，并比较两个逻辑时间的像素差异。
- 线宽过细时，按 density / 画布短边估计像素覆盖范围，为线边界提供抗锯齿宽度。
- 大半径模糊避免稀疏规则网格；本例改为中心加 48 个黄金角圆盘采样，消除了网格重影，但不据此承诺任意半径的性能。
- 生成真实项目素材的 CPU contact sheet 并查看；仍须区分 CPU 验证与真实 GPU 验收。

证据：DefaultVisualRenderingTests 的 128×128 动态像素断言，以及显式 WriteDefaultVisualPreview 产生的 artifacts/visual-library-preview.png。修正后相关 27 项效果测试与 257 项展示层普通测试通过。
