# 设计

现状：眼睑开口是抛物线，在 progress 接近 1 时角落仍然遮挡；progress>=1 又强制完全透明，产生跳变。线性关键帧还在闭合/保持边界突然改变速度。

修正：开口在垂直和水平方向扩大，向外遮挡预留 2 个 shader 像素；使用 smoothstep 在开口后段淡出残余遮挡，并在闭合初段连续恢复羽化。眼睑预设缺省羽化 0.025（作者显式设置可覆写），遮罩预设用零切线 CubicHermite，使闭合、停留、睁开速度连续。复用既有 Shader/Timeline，无新服务。

演示按文本与图集、媒体、音频、路线分支、Shader、遮罩、人物演出、天气、转场、结束组织。旧/新同类案例合并，增加双语章标题及操作前引导，保留原作者 Entry/资源/文本 key。每个视觉章节显式布景和清理，音频集中起停，不依赖路线进入视觉库。

风险：移动后的 Group 影响旧存档；CPU 像素验证不能替代原生 GPU 和主观视觉验收。

验证发现的必要修正：EditorDocumentRepository 以默认大小写敏感设置反序列化 Graph，导致嵌套 AssetMeta 的 camelCase 已知字段误入 ExtensionData；保存基线以 Web 设置重读时又转成已知字段，刚打开即 dirty。仓库读取改用显式大小写不敏感选项，与实际 Graph/metadata 的 camelCase 输入保持一致；未知扩展字段继续保留。通过实际 GameTestCase 打开/保存/重开回归验证。

## P3 天气验收跟进

粒子 Shape 是左上角原点的绝对坐标；旧天气用 x=0/y=-height/2，造成半屏覆盖与慢速粒子数秒后才进入视口。改为中心(width/2,height/2)的画面范围发射，配短时间淡入，让新一轮立即在全画面开始。雨滴用确定性白色渐变线条生成 4×64 透明纹理（替换原 8×32），缩小宽度并降低 alpha；花瓣两个颜色端点均为粉色系。

particle.stop 新增 clear=false，保留默认自然排空；clear=true 立即释放对应呈现实例。天气 Timeline 收尾使用 clear=true，不等待寿命；按用户要求移除 GameTestCase 旧雪花/火花段，仅保留新天气预设。IParticlePresenter 增加默认实现的 clear 重载以保持旧宿主源码兼容；默认实现回退原 stop，Avalonia 实现精确清除，其他宿主要支持立即清除需覆写重载。旧发射器的延迟 drain 不得误删新实例。

目视预览发现旧 lesson 将粒子颜色误写为 ARGB，实际 ParticleEmitter 使用 RGBA；不修改既有解析契约，改正预设的 RGBA 参数和手册/lesson，并增加真实花瓣像素 R>B>G 与雨滴 alpha 回归。

## P4 自然接续
按用户新决定，雨/雪/花瓣在 y=-16、height=1 的全宽发射带生成；Dust/Fireflies 仍在画面内浮动。天气收尾改为 clear=false，仅停止发射；不同 playbackHandleId 对应独立 emitter，旧轮自然 drain/Drained 释放不阻塞新轮。保留手动 clear=true 能力。默认重用同一 playbackHandleId 仍属于替换而不是重叠，演示各轮已有独立 ID。

## P5 实际宿主更新与连续性

根因：SceneLayerHost 使用 Any(Update)，第一个活跃 renderable 短路，后续 emitter 未更新；直接调用各 emitter 的旧测试未覆盖宿主路径。改为逐一 Update 并聚合活跃结果。先用真实 Host.Render 回归复现。

持续发射：天气编译将实际 rate 限制到 maxParticles/(maxLife+0.1)，避免预算小于完整生命周期占用而按批次饱和；通用 emitter 不积压满载期间错过的发射配额，避免容量空出时补发旧配额。保留作者 maxParticles 上限。

雨默认加 250 px/s 的水平速度，方向按实际速度对齐纹理。增加通用 initial.alignToVelocity=false，使用 atan2(vy,vx)-90° 叠加作者旋转；径向雨沿用已有 circle/radialVelocity 原语，新案例独立持有 ID，自然排空。Timeline stop 必须传递 clear 参数，修正先前旁路未转发问题。
