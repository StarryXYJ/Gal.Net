# 实现总结

## 结果

- 默认与 GameTestCase 的 ShapeMask 同步修复：眼睑开口在横纵方向扩大，遮挡边缘预留两纹理像素；progress 0.65–1 用 smoothstep 淡出剩余角落，闭合端连续收束羽化，取消全开阈值切换。
- Blink/Open/Close 默认 feather=0.025，保留作者覆写；遮罩预设轨道使用零切线 CubicHermite，闭合/停留/睁开平滑衔接。
- GameTestCase 在 P2 保留原 282 个作者案例，调整为文本与图集/提示卡、视频、音频、shader/景深、遮罩、人物与情绪、粒子天气、分支转场、结束。包含两个 Gallery 重播组，P2 共 15 个 Group、371 个作者条目（P3 后调整见下文）。两条分支均可体验全库。
- 增加双语章节和操作前引导、短动作标签/观察停顿、原声音频对比与明确语音停止；两种覆盖范围相邻，慢眨眼时长 120 帧，分离 Close/Open 之间有停留。视觉章节显式清理与布景。
- 验证中修正 EditorDocumentRepository 对嵌套 AssetMeta 大小写不一致导致的打开即 dirty；未知扩展字段保持。实际项目打开/修改/保存/重开及资源字节保留回归通过。

## 验证证据

证据统一保存在 `output/presentation-polish/`（不提交）：

- Builtins：450 项通过，含预设缺省羽化、插值、全展示快进清理/快照。
- Presentation：255 项通过，普通全量有 8 项 Explicit 未自动执行，其中眼睑预览另显式运行 1 项通过，其余 7 项未执行。新增矩形/奇数尺寸、羽化、闭合全边缘、非整数视口/缩略输出像素及端点连续性检查；已目视查看 `eyelids.png`。
- Editor.Shared：621 项通过。
- 项目集成：13 项通过，覆盖中英文 × 两条路线、存档恢复、导出包内资源/图集、编辑器保存重开。
- Headless 构建、完整项目校验（complete=true、零诊断）、目录导出成功；Core/Builtins/Project.Contracts 三个 DLL 与本地部署和 Sample 输出 SHA256 一致；导出位置 `output/presentation-polish/content-final`。
- 文档：72 页、310 个仓库引用通过。全量文本/NVL/卡片 key 的双语解析、作者 Entry 保留及两份 shader 字节相同均已检查。

## 调整与中间失败

- 原存档用例在 intro 中寻找音频检查点，现改为 audio 章并断言该章显式布景的图集值；旧 Group 数与默认 shader 数断言更新为 15/16。
- 初次编辑器回归暴露 metadata 的读取/基线不一致，已修复并记录 lesson；不清 dirty 绕过。
- 新增音轨停止的 clearQueue 最初误用 JSON boolean，作者值要求字符串 `"true"`；已修正后完成项目复测。
- 集成测试旧 testhost 曾短暂锁住 DLL，按约定告知并请求决定；进程自行退出后继续，未结束进程、复制缓存或创建隔离环境。用户随后授权确认归属后可结束残留进程，但本次无需执行。

## 边界

本轮验证为 CPU shader 像素、Headless 和自动化集成；未专项验证原生 GPU、移动设备、主观听感与持续动画视觉。旧 GameTestCase 存档/阅读进度因 Group 归属变化不承诺续播。移动端 feature 的改动保持独立。本轮未自动提交 Git。

当前行为见 [默认视觉库手册](../../docs/manual/editor/default-visuals.md)、[Entry 规范](../../docs/spec/runtime/entries.md) 和 [演示项目说明](../../GameTestCase/README.md)。

## P3 用户验收跟进：天气

- 修正左上角原点与居中坐标混用造成的半屏、屏外发射问题。五种天气在画面范围生成，短淡入后即可见；真实作者预设在 0.25 秒内覆盖左右两半及下半部的像素检查通过。
- particle.stop 增加 clear=false 默认保持自然排空；clear=true 立即清掉对应发射器和粒子。天气 Timeline 的 OnCompletion 使用 clear=true，所以无需等待上一轮寿命。旧宿主可保留默认接口重载回退，新 Avalonia 宿主实现立即释放。
- 雨滴改为程序生成的 4×64 白色透明渐隐纹理，宽约 1.12–2.8 逻辑像素、长约 18–45，颜色 alpha 48–120；样例雨速率为 100。花瓣使用 RGBA #ED4786CC 至 #FFB1D1F2，两个端点均为粉色系。
- 按用户最新要求，移除独立旧雪花/火花段及无用旧雪花收尾，天气章只保留五种新预设；当前共 15 个 Group、366 个作者条目。P2 的全保留是当时结果，已由此项明确调整。
- 目视预览暴露原 lesson/手册将 Particle 误归到 ARGB：真实解析为 RGBA，误传会变黄绿色。已修正参数、lesson、手册，并以实际渲染像素断言粉色 R>B>G 和雨滴半透明，保留现有解析契约。

本阶段证据在 `output/weather-polish/`：Builtins 452、Presentation 262、完整项目集成 13、Editor.Shared 视觉模板与创作 9 项通过，另显式天气预览 1 项通过；普通呈现全量有 9 个 Explicit 未自动运行，其中天气预览另行执行，余 8 项未运行。Headless 构建、完整项目校验 complete=true/零诊断和目录导出通过；Core/Builtins/Project.Contracts 与本地部署及 Sample 输出 hash 一致。最终导出 `output/weather-polish/content`，预览 `weather.png` 已目视检查。新测试初次缺少必填 particleTexture 的夹具已补齐后通过。原生 GPU/移动端持续运动仍未专项验证。本轮仍未提交 Git。

## P4 最新决定：自然接续（替代 P3 天气强制清除）

雨/雪/花瓣改从画面上方 16 像素、全宽 1 像素高的发射带落入；浮游天气仍在画面内生成。天气 Timeline 收尾显式 clear=false，仅停止旧轮发射，已有粒子按原寿命曲线淡出，Drained 后异步安全移除并释放。旧轮停止的 Task 不等其排空，新轮使用独立 playbackHandleId 立即开始。通用 clear=true 能力保留为手动选择。

新增实际呈现回归验证旧轮仍可见且停发、新轮已发射、旧轮排空只移除自己；更新实际作者预设的全宽入场/浮游覆盖像素测试，验证 0.75 秒内进入画面。Builtins 452、Presentation 263、项目集成 13、显式预览 1 项通过；CPU 预览已再次查看。Headless 构建、完整项目零诊断校验/目录导出通过，三个契约 DLL hash 与本地部署及 Sample 一致。最终内容为 `output/weather-polish/content-drain`，证据文件均带 `-drain`。设备验证边界与未提交状态不变。

## P5 最新修正：宿主实际更新、连续发射与径向雨

用户实测证明 P4 单体 presenter/粒子测试不足以验收真实帧链路。SceneLayerHost 的 Any(Update) 在首个活跃对象处短路，导致后续发射器完全不更新；新的实际 Host.Render 测试先复现 [1,0,0]，修正为无条件逐个 Update 后为 [1,1,1]。此前只改停发和坐标未解决串行现象，本阶段补齐根因修复和 lesson。

- 天气在数量预算内按最大寿命限制持续速率，保留作者 MaxParticles 上限，避免先占满再集中空出的周期。通用发射器仅保留发射小数配额，满载丢弃的整数配额不补发。三种落下天气均进行 60 秒、每半秒一次的顶部新粒子实际像素采样，持续可见；另有预算腾空不补发爆发的回归。
- 默认斜雨水平附加 250 px/s，发射带向上风侧扩展；initial.alignToVelocity（默认 false）使雨丝纵轴跟随当前速度，同时保留作者旋转。增加实际作者径向雨原语案例：circle + radialVelocity + alignToVelocity，放在斜雨之后、雪之前，独立持有 ID 并自然排空。当前 15 个 Group、369 个作者条目。
- 补齐 Timeline particle.stop 的 clear 参数转发，按 JSON boolean 读取；原先旁路调用旧重载会忽略 clear。既有 Timeline 粒子调度测试追加 clear=true 的实际转发断言。

证据在 output/weather-polish：Builtins 452、Presentation 普通全量 269、后加预算回归单独 1 项、完整项目集成 13、显式天气预览 1 项全部通过（共 736 项不同测试）。预览已目视检查斜雨/径向雨/雪/花瓣。Host.Render 回归需运行在既有 Headless 平台上；DrawingGroup 不支持末尾自定义 Skia 操作，测试只容许该特定 NotSupportedException 并检查其之前的实际宿主更新。未宣称原生 GPU 验收。

Headless 构建、完整项目 complete=true/零诊断校验、目录导出通过；三份契约与 Sample 输出 hash 一致。最新最终内容为 `output/weather-polish/content-p5`，对应日志均带 p5。本轮仍未提交 Git，既有其他工作区修改保留。

## 后续外观微调
用户要求雨稍粗、萤火虫稍黄：斜雨尺寸范围调整为 0.044–0.11，径向雨同步增加约 10%；Fireflies 的 RGBA 调为 #FFF0A088–#FFF7C0FF，保持原 alpha 范围和运动参数。5 项天气编译、11 项实际天气渲染回归通过；Sample Desktop/Headless 构建和完整项目零诊断校验通过，本地项目契约已同步。本次微调尚未提交 Git。
