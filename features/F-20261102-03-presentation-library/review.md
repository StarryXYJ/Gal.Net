# 实现审阅

本轮由实现 agent 对实际 diff 自审，未启动独立子 agent，也不把自审表述为独立审核。

## 已修正的问题

- 上层 SceneLayerHost 默认黑底会遮住整个 UI：增加透明 CPU 合成和独立 GPU surface，不清共享目标；空覆盖面不提交绘制。
- shader 与 Layer 的八位颜色顺序不同：ShapeMask 默认使用六位不透明黑色，文档明确 RGBA/ARGB，闭眼像素测试覆盖。
- EffectInstance 默认 progress=0 会污染项目 shader 恢复：非空 program 不保存虚构 progress；真实动画值仍保留，状态往返验证。
- 强调逐项动画不能用一个播放身份统一停止：改为单 Timeline，增加 Replace 首键从当前呈现值读取的 startFromCurrent，保留作者键与逻辑末值。
- 新的强调参与数组、视差数组与音频自动化引用原先未参与复制重映射：扩展已有 identity walker，增加行为测试。
- 集合元素的 CLR 大写 wire 名导致 Parallax 本地化检查不完整：DTO 显式 JsonPropertyName 与作者 JSON 对齐，结构解析测试及最终完整项目校验通过。
- Bitcrusher 省略 mix 的 DSP 默认与 catalog 不一致：修正并对六种新效果验证空参数与显式 catalog 默认的实际信号一致。
- Headless 音频循环不能提前执行末帧停止：初始化只提交首帧；显式停止执行完整终态，有专门回归。

## 剩余验证边界

没有把 CPU contact sheet、Headless 集成或编译成功当作原生 GPU/跨平台真机与主观听感验收。设备、GPU 性能和声音艺术调校未测，详见 summary。音频自动化当前通过 Timeline 数据创作，未增加专门的音频曲线 GUI。
