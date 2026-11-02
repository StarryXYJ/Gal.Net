---
status: confirmed
date: 2026-11-02
feature: F-20261102-06-player-layout-polish
---

# 透明点击层仍可能被按钮样式绘制为不透明

SaveSlotCard 的 Preview.Source 有有效图片、尺寸正常，但真实存档图片不可见。此前测试只验证了 Source、Bounds、重命名和点击，没有校验实际像素。

复现用纯 CornflowerBlue 图片，Mono/Ocean/Rose 三种配色的截图采样分别得到白色/深色面色，均等于 PART_Activate 的实际 Background。按钮虽然在模板中声明 Transparent，应用按钮样式仍使点击层绘制了面色，并在图片之后覆盖它。

修正将激活按钮放在底层，预览 Image 在其上绘制并设置 IsHitTestVisible=false；标题编辑区、hover/focus 高亮继续在更上层。这样既不遮图片，也不阻断保存/读取点击或重命名。

后续预览验证应同时覆盖实际已知颜色像素与点击行为，不能用绑定成功或控件尺寸正常代替可见性。证据：ModalAndSaveVisualTests；output/player-layout-polish/modal-preview/before.trx 的四项失败和 after.trx 的修复通过。
