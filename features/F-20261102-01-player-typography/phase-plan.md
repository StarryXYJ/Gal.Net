# 实施阶段

## P1 富文本字号（verified）

扩展 Core token/parser 与 Avalonia run 渲染。退出条件：比例、偏移、绝对值、嵌套、非法输入和原有标签/打字指令通过；基础字号变化不重新播放且测量更新。

证据：Core 全部 156 passed；Presentation 全部 230 passed，另 6 项 Explicit 未执行。集成测试验证有字体后端的真实测量增长及打字进行中的任务/run 身份不变。

## P2 玩家字号档位（verified）

设置服务保存四档选择，Sample 提供语义字号与平台默认配置，接入现有页面/控件资源。退出条件：桌面较小档保留原设计值；平台默认、保存恢复、非法旧值回退、当前视图资源更新及标题/ADV/NVL/历史继承验证通过。

证据：5 项 PlayerTypographyTests 通过，包含平台默认两例、持久化/回退/中英文标签、真实标题/ADV/NVL/历史与截图资源、独立角色配置和无效值、打字进行中的切换。最终相关集成 93 passed，包括原有窗口/模板/设置及 GameTheme/ScreenshotDialogLayout 回归。平台默认通过显式平台分类测试，不是 Android/iOS 原生运行。

## P3 文档收尾（verified）

运行相关 Core/Presentation/Integration 验证，更新当前 spec/manual 和 summary，记录真机未验证与移动布局后续。退出条件：实现和证据一致，不隐藏失败或扩大已验收范围。

当前规范、扩展手册、玩家操作说明与 agent 索引已同步。证据和限制见 summary；未进行全解决方案 CI 或真机验收。
