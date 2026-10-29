# 阶段计划

1. P1 verified：契约、Bus 配置、Track 策略、实例淡出、目标效果状态与原语迁移。307 项原语/契约回归，以及新增恢复、随机和循环测试通过。
2. P2 verified：统一 PCM mixer、真实滤波、混响、有序平滑效果链。频响、顺序、汇总、尾音、连续性、零分配、效果重挂/换类型测试通过。
3. P3 verified：miniaudio Windows 原生解码/输出、有界流缓冲、取消和释放；默认宿主接入。Unicode WAV/Vorbis 解码、真实 Windows 设备与播放中 Track 效果/Bus 混响、文件租约和取消验证通过。
4. P4 in-progress（全仓门禁保留）：编辑器/配置/示例/文档迁移已完成，音频相关验证通过；独立输出的解决方案构建、普通发布与原生 DLL 分发检查通过。当前工作区的一项非音频 Timeline UI 用例仍失败，未伪装为全仓通过。见 [总结](summary.md)。

## 音频创作交互（音频范围 verified）

见 [补充设计](authoring-design.md)。在保留 P1–P4 证据的基础上，拟依次推进共享音频身份/复制语义、剧情位置音频事实与查询、创建/停止选择与参数表单，最后接入后续位置参数更新。各步骤依赖与验收见该设计的“交付与验收边界”；实际证据见 [创作实现记录](authoring-summary.md)。

2026-10-27 用户授权提交基线并开始实现；基线提交 `f3c19655`。A1 身份/复制、A2 分域分析、A3 创建/停止表单及 A4 参数更新均已实现并通过对应验证。

- A1 verified：新建/复制身份、目标限定重映射与引用索引，新增纯分析/身份回归覆盖。
- A2 verified：复用位置、Graph、条件/循环和前缀缓存，独立音频 payload/完整性；Editor.Shared 全套 456 项通过。
- A3 verified：类型 ComboBox、元数据参数字段、显式 all 与既有实例选择；音频及共享选择器挂载回归 11 项通过。
- A4 verified：update patch 合并、原子验证和不复活缺失实例；Media.Audio 22 项通过，Primitives.Builtins 321 项通过。

整体 feature 不宣称全仓关闭：完整 Editor 在断言修正前为 329 通过/2 失败，范围文案断言已修正并在最终音频回归通过，既有 TimelineEventQueryPickerKeepsPositionAndPropertyCreationUsesFrameZeroBaseline 的 Flyout 空引用保留。
