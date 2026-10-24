# Timeline 交互简化

日期：2026-10-18。状态：verified，用户明确授权实施。

这是 P6e2 的用户反馈修订，替代旧时间设置组合，不引入兼容路径。已有轨道持续显示绝对值/叠加偏移模式，切换只改变该轨道 BlendMode，不换算关键帧数值和切线；一次项目历史，旧行提交仍须通过版本检查。

顶部只保留当前帧、结束帧和 FPS。结束帧/FPS 输入 Enter 或失焦确认，Escape 恢复未提交草稿；各自提交，不要求另一输入有效。FPS 保持帧号与切线，改变播放速度；结束帧下限为最后一个关键帧/事件，不提供裁剪。删除尾部内容后可继续缩短。移除保秒重定时、裁剪、应用按钮和相关生产路径。

关键帧拖过结尾允许到 int 帧范围内，松手时原子移动并延长结束帧，Undo 一起恢复。负帧、0 帧首键、碰撞和过期手势的既有保护保留。标尺在结尾之外仍显示刻度，拖动预览不写内容；不引入自动播放/游戏预览或整体时间缩放工具。

验证覆盖真实模式下拉框、时间输入焦点/键盘、独立提交、帧保留、下限钳制、跨结尾拖动与撤销/取消/溢出、未知 JSON/同帧事件/保存重开，以及受影响回归、构建与格式门禁。

## 实施与验证过程

原重定时/裁剪实现和对应命令已删除；TimelineDocument 只修改实际变化的帧率/结束帧，确认省略的默认值不会补写字段。结束帧下限由轨道键及事件帧计算；拖动的整帧目的地通过非负/int 边界与首键/碰撞校验后，在同一 JSON patch 中移动并延长。

轨道模式以明确选择事件提交，无损更新原 JSON 的 blendMode；已有属性范围不接受绝对值时保持实际模式，界面显式恢复下拉框选项。首次 13 项聚焦和六项目 901 项回归通过后，审查补充了这个拒绝回显边界及真实控件断言，再次 13 项聚焦通过。该补充不复用之前的源码指纹作为最终快照证明，最终门禁单独记录。

真实控件覆盖：轨道模式键盘切换、四语选项、结束帧文本 Enter/取消、FPS 失焦提交、一次多轨越界拖动及 Undo、越界预览取消和帧溢出拒绝。服务路径另验证两输入独立拒绝/提交、结束帧钳制且不删事件、FPS 保留键/事件/切线、重复确认不加历史、旧行失效和省略默认字段无写入。Semi 截图已检查，顶部只显示结束帧/FPS，已有轨道右侧持续显示模式。

## 最终结果

最终解决方案构建成功（增量构建 0 警告、0 错误）；最终同源码 Editor 169、Architecture 45、Presentation 243，共 457 项通过、0 失败。Presentation TRX 另记录 6 项 NotExecuted，缺少原生 LibVLC/视频环境；本次不修改媒体功能。whitespace/IDE0005 verify 和源码 SHA-256 一致性通过。四语各 678 keys；文档链接和 diff 检查通过。

完整六项目初次验证为 901 项通过、0 失败（86 警告、0 构建错误）；随后仅补充 Editor 模式拒绝回显与控件断言，最终快照证据以上方 457 项为准，不把此前指纹当作最终源码。此修订退出条件满足，P6e 整体仍在实施，下一步 P6e3 曲线/切线。

证据目录：`C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/timeline-validation`。最终脚本 `validate-timeline-simple-final.ps1`，结果 `timeline-simple-final-GalNet*.log/trx`，构建 `timeline-simple-final-solution-build.log`，格式 `timeline-simple-final-format-verify.log`，指纹 `timeline-simple-final-source-sha256.json`，截图 `timeline-simple-panel.png`。初次完整验证 `validate-timeline-simple.ps1` / `timeline-simple-GalNet*`，补充聚焦 `timeline-simple-mode.*`。
