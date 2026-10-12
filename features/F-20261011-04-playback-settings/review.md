# 播放与设置 feature 审核

日期：2026-10-12。结论：**pass-with-follow-up**（自动化与代码审核通过，桌面完整验收待补）。

审核基线为 `43dd62f`（相对其父提交），包含本轮格式修正和 `BuiltinAutoPlaybackTests`。未纳入用户原有 `App.axaml.cs` 改动，不扩展 Editor UI。审核依据为 feature/design/phase-plan、实际 diff、实现与测试；不是另一个独立 agent 的审核。

## 重点检查

- Core/Runtime 不反向依赖 Avalonia；播放状态以 Engine enum 为唯一来源，自动等待使用实例 identity 集合，不模拟点击。
- 阅读资格在本次标记已读之前捕获，稳定身份使用 group + compiled entry ID；同名条目允许跨 group，旧格式拒绝且不自动迁移。
- 自动计时等待有效边界及所有动态自动阻塞，设置变更/取消/读档/停止使旧计时失效；Choice 不自动选择。
- 追帧实际执行条件与变量，目标语音前先停止并同步最终展示；已启动和存档恢复的展示任务分别排空，最终恢复使用隔离 run scope。
- 类型化设置的范围验证、Step 非校验、Choice 默认回退、availability、保存失败重试及自定义控件 detach 生命周期有对应测试。
- 三布局×三配色测试检查真实模板背景和重复点击取消，而非只检查 ViewModel 值。

本轮没有确认新的 Blocker/P1 业务缺陷。格式门禁发现的换行及导入顺序问题已定向修正；没有对整个工作树做格式化。

## 验证

本轮顺序执行 Core 70、Runtime 71、Builtins 60、Storage 14、Editor.Shared 18、Presentation 131、Integration 77，共 441 项通过；Architecture 26 项通过。Integration 新增 4 项测试使用真实内置 Dialogue/Audio 原语和 AudioRuntime、可控展示端，覆盖两种完成顺序及音频失败，确认间隔不提前开始。

Sample 在 Integration 构建中成功编译；仍有非阻断 analyzer/XAML constructor warning，不将其表述为零 warning。原生视频 4 项、音频 lifetime 2 项在先前 Phase 4 已实际通过，本轮常规回归不包含原生 opt-in，不重复声称原生后端再次验收。

格式验证限定本 feature 文件：whitespace、IDE0005；`git diff --check`。具体最终输出见 phase-plan。

## 后续验收（P2，非已确认代码缺陷）

完整桌面 Sample 链路仍缺人工证据：自动文本+可听语音；快速/追帧最终媒体组合；追帧取消后手动推进；读档/退出时迟到回调；物理窗口/无边框全屏切换及音量听感。分层自动化和孤立原生测试降低风险，但不替代这些组合验收。

保留 Phase 4/5 为 in-progress 和 feature implementing，不以生成总结作为完成依据。当前工具不能原生控制桌面窗口，因此没有伪造截图或操作结果。Editor 预览完整设置与追帧协调、快捷键编辑、独占全屏、音频存档恢复仍不属于本次交付范围。
