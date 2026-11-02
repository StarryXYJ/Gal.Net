# 演出库扩展与通用文本卡实现总结

2026-11-02。已完成本批实现、案例、自动化验证和文档同步。翻页、描边及需求文档列出的高级相机/粒子能力保持延期。原生设备、GPU 和主观听感验证边界见下文。

## 实现结果

- 默认项目视觉库从 8 个扩展到 16 个 shader，新增位移/波纹/热浪、缩放/旋转径向模糊、胶片、故障/色差/扫描线、像素化、聚光、形状遮罩和情绪调色。另提供 5 个天气纹理用途，源资源由项目拥有，旧项目显式安装，不覆盖作者文件。
- ShapeMask 提供矩形、圆角矩形、椭圆、眼睑、图片和黑边模式，以及羽化、反向、位置、大小、旋转和颜色。裁切目标与独立覆盖使用同一 shader；默认场景内，可选择覆盖对白和文本卡。透明覆盖面不清除已绘制页面，确认弹窗始终在最上层。
- 新增 text.card 原语、ITextCardPresenter/TextCardHost：九宫格位置、App 样式名、时长覆写和可等待；同位置替换、不同位置独立，旧取消/退场不能删除新卡。Sample 的音乐提示复用相同服务，保留真实 Started、循环/恢复抑制及本地化。
- 新增六种 SEffect：Delay、立体声平衡、峰值 EQ、软失真、Bitcrusher、左右联动 Compressor。仍复用现有 PCM 有序链和参数快照，音频回调不新增分配或 I/O。
- Timeline 增加 audioTracks 与 audio.play/stop、audio.seffect.apply/update/stop、audio.mix.set 事件；音频参数由同一帧回调驱动，跳过不补发瞬时声音。新增 audio.duck，实际源声音开始后压低指定 Track/Bus，暂停/结束后恢复，与玩家及剧情音量相乘，规则入档、包络不入档。
- 新增 preset.mask/flash/idle/weather/mood/heartbeat/parallax/emphasis、dialogue.emphasis，以及 transition.flash/zoom/pixelate/glitch。说话人图层显式指定，单 Timeline 同步变化并可与原文本/语音组合，StartFromCurrent 保证连续切换衔接，EnsureOnly 不重建自己的已有效果。
- 编辑器接入资源填充、shader 参数表单、目标分析、四语言条目/音效名称。强调参与者、视差和音频参数轨道引用进入既有索引/复制重映射；Parallax DTO wire 名与作者 JSON 对齐。

## GameTestCase

新增 [presentation-library.rawgalgroup](../../GameTestCase/Graph/groups/presentation-library.rawgalgroup)，接在 ending 后，两条现有路线均可到达。

- 139 条作者 Entry，编译为 143 条原语。
- 54 条说明分别提供 en-US 与 zh-CN 版本。
- 展示全部新 shader、单人物裁切、场景/对白覆盖遮罩、九宫格/替换文本卡、呼吸/摇摆、人物强调、五种天气、四种情绪、心跳、视差、四种转场、六种新音效、音频参数渐变/帧事件和 duck。
- 完整项目校验 `success=true, complete=true`，零诊断；单组编译和目录导出成功。
- 原始项目 Contracts 已部署目标 Sample 的契约；Project.Contracts、Contracts.Builtins、Core 三个 DLL 与桌面 Sample 输出逐一比对 SHA256 相同。

最终验证导出位于 `output/presentation-library/content-final`，不提交 Git。素材/剧情仍以 GameTestCase 源文件为准。

## 验证

使用本机固定 SDK、已有 NuGet 缓存和项目默认 bin/obj；额外报告统一在 output/presentation-library。

| 检查 | 结果 |
|---|---|
| Primitives.Builtins 全量 | 450 通过 |
| Editor.Shared 最终全量 | 621 通过 |
| Presentation 全量 | 250 通过；全量中的 7 项 Explicit 未自动执行 |
| Media.Audio 全量 | 45 通过；2 项原生设备 Explicit 未执行 |
| Architecture 全量 | 56 通过 |
| 曲名迁移后的三套配色集成 | 3 通过 |
| 新 shader CPU contact sheet | 单独显式执行 1 项通过，已目视检查 preview.png |
| 编辑器作者相关定向检查 | 156 通过（包含在上述全量中，不重复计数） |
| Editor / Headless CLI / CrossPlatform Desktop 构建 | 成功 |
| GameTestCase 完整校验、单组编译、目录导出 | 成功，零诊断 |
| 文档导航/链接/片段检查 | 72 页、310 个仓库引用通过 |
| git diff --check | 无空白错误；其他工作区文件的换行提示保留 |

全量 Presentation 的 Explicit 计数中包含预览测试；本轮单独执行了新库预览，未把 Explicit 的 notExecuted=0 当作全部已运行。最终绿色全量已覆盖此前因新增条目数量导致的固定计数断言更新。

针对性验证覆盖：shader 中性恒等/透明像素、遮罩羽化与端点、静态 shader progress 的快照往返、强调当前值衔接、文本卡替换/取消/完整等待、DSP 尾音/频率/声道/压缩/极值/无分配/默认值、duck 音量叠加与暂停恢复、音频共用帧时钟/跳过/Headless 循环清理，以及整组案例的 CatchUp 状态执行与存档。

## 设计落实与边界

- 文本服务由应用表面提供，游戏会话借用并在取消/重置时清理，未另建通知调度框架；样式与时序由 App 定制。
- 音频使用独立 audioTracks DTO，经既有 beforeFrame 回调进入 AudioRuntime；没有为每个音频参数增加 Scene 对象。一次性音频自动化读档保留已提交值，不恢复精确时钟；循环沿用原 Timeline 定义恢复。当前通过 Timeline 数据创作，未增加专用音频曲线 GUI。
- 渲染路径及默认宿主已接线；CPU 像素、Headless 和构建证据不替代原生 GPU、移动端设备、长时间性能或主观声音调校。本轮未执行这些专项验收，也未宣称它们通过。
- 几何遮罩与色彩规则复用现有约定：shader 八位颜色是 RGBA，Layer/Particle 八位颜色是 ARGB；默认不透明遮挡使用六位颜色。
- 自审发现并修正的问题见 [review.md](review.md)；未使用独立审阅 agent。
- 工具策略拒绝递归删除旧的自生成验证目录，因此保留旧 output/presentation-library/content，最终验证另用 content-final。未删除或改写用户的既有/并行工作区改动，未创建隔离环境或复制 SDK/NuGet 缓存；Git 提交按用户后续明确指令单独执行。

## 正式文档与经验

- [默认视觉与预设](../../docs/manual/editor/default-visuals.md)
- [音频创作](../../docs/manual/editor/audio.md)
- [Sample 定制](../../docs/manual/extensions/customize-sample.md)
- [运行时契约](../../docs/spec/runtime/entries.md)
- [媒体契约](../../docs/spec/presentation/media.md)
- [作者目标分析](../../docs/spec/editor/scene-targets.md)
- [透明覆盖合成](../../.agents/lessons/L-20261102-01-transparent-ui-scene-overlays.md)、[颜色顺序](../../.agents/lessons/L-20261102-02-shader-color-alpha-order.md)、[shader 静态初值](../../.agents/lessons/L-20261102-03-shader-static-progress-snapshot.md)
