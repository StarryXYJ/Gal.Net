# Track / Bus 音频混音后端

状态：implemented，音频范围验证通过；全仓验收保留一项非音频 Timeline UI 失败，见 [总结](summary.md)。2026-10-27 用户明确授权开始实现；此前讨论确认实例拥有自身播放和收尾配置，Track 管理顺序/随机、循环队列和轨道声音，Bus 汇总 Track 并执行共享 SEffect。允许重构 Entry 和原语。跨平台契约，首期 Windows。

目标：替换 LibVLC EQ 近似；真实低通、高通和混响；Track/Bus 有序效果链；无播放重启的效果更新；实例淡入淡出；已有取消、迟到事件、恢复保护保持有效。

非目标：公开 Voice/Send 对象、任意循环路由、VST、精确游标/混响缓冲存档、音视频时钟统一、其他系统的设备验收。

验收：PCM 频响、混响尾音和 Bus 汇总测试；播放队列/随机/循环与结束策略、恢复和取消回归；Windows 原生解码与设备播放验证；编辑器/示例配置和正式文档迁移。

见 [设计](design.md)、[阶段](phase-plan.md)。

2026-10-27 创作交互补充：用户确认效果类型使用 ComboBox、实例身份类似 Layer 自动生成并隐藏，停止时按 Track/Bus 选择已有实例，复用现有选择和分析机制。状态：implemented，音频范围验证通过；类型/参数表单、自动身份、已有实例与全部停止、参数 patch 更新已交付。见 [创作设计](authoring-design.md) 和 [创作实现记录](authoring-summary.md)。全仓 Timeline 失败继续保留。
