# 播放控制与分类设置实现总结

状态：主要功能已实现并提交；自动化审核通过，桌面组合验收未完成。feature 仍为 implementing。主实现提交：`43dd62f`。

## 实现结果

- Engine 提供互斥 Quick/Auto、动态自动阻塞和整数毫秒间隔。Quick 全文呈现并快速完成可跳过阻塞；Auto 等所有声明参与的有限呈现结束后计时，不需音文配对。
- 带主题确认的 Skip 沿真实路线追帧，在未读阅读边界或 Choice 停止；取消保留已执行逻辑，不回滚变量。
- ADV/NVL 成功呈现后记录已读，group + 稳定编译 ID 消除位置变化与跨 group 同名冲突。compiled v3 / progress v2 拒绝旧格式，raw v2 / snapshot v3 不变。
- 分类设置采用只读结构和可观察类型化项：Int/Double（Slider/NumericUpDown）、Bool、String、Choice、Action、自定义编辑控件租约；动态 availability 和原因 key、默认回退、应用/保存队列及保存重试。
- Sample 接通游戏、画面、音频设置；快速默认 50ms、自动 2000ms。窗口/无边框全屏及预设尺寸由纯能力端口提供，不宣称独占全屏。音量/静音实际作用于当前与后续播放。
- 三布局×三配色工具栏与设置模板统一；自动/快速只在选中时显示背景、可再次点击取消，其他工具按钮悬浮淡色、按下加深；可按类型覆盖设置模板。

## 关键边界与计划调整

`PrimitiveInstance.PresentationCompletion` 区分逻辑完成与异步展示清理；Sample 跟踪恢复实例，并在 Engine scoped runtime/token 下重建。文本/NVL 使用会话内投影，不把历史塞入 snapshot，也不把 BGM 追帧投影扩大为音频存档恢复。

复用 `SampleGamePresentation` 作为追帧端口，确认状态复用现有游戏 VM，不新增通用 Runner 或独立确认导航页。用户移除 Editor 后续实现范围；保留已有的最小播放绑定及 Shared 作者文档 ID 修正，不扩展 Editor 设置。未采用 AutoSettingUI 或新 UI 依赖。

本地旧样例 progress version 0 已显式移至 `progress.legacy-20261012.json.bak`，没有加入运行时自动迁移/清空。用户设置和存档保留。用户原有 `App.axaml.cs` 配色修改未提交。

## 验证与限制

最新回归：Core 70、Runtime 71、Builtins 60、Storage 14、Editor.Shared 18、Presentation 131、Integration 77，共 441 项通过；Architecture 26 项通过。新增 4 项真实内置文本/音频联动测试覆盖完成顺序与失败。Sample 构建通过。

先前验证：GameTestCase 作者 validate、4 group 重编译和 Headless 路线到终态通过；真实 LibVLC Video 4 项、Audio lifetime 2 项通过。未将 isolated 后端测试当作完整桌面验收。编译仍有非阻断 warning；格式检查仅针对 feature 文件，避免改动用户文件。

[审核记录](review.md) 为 pass-with-follow-up。剩余桌面人工验收包括媒体组合、追帧取消/读档/退出、物理全屏切换和音量听感；Phase 4/5 未据此标为 verified。快捷键、音频存档恢复和独占全屏不在范围。

## 文档与经验

已同步 [Runtime](../../docs/spec/runtime.md)、[游戏页面](../../docs/spec/control.md)、[i18n](../../docs/spec/i18n.md) 和 [Themes README](../../src/Presentation/GalNet.Avalonia.Themes/README.md)。正式文档保留当前能力与边界，未加入未实现方案。

经验见 [agent-lessons.md](agent-lessons.md)：run scope、作者校验作用域、显式文本 delay、Fluent 模板状态覆盖。`.agents` 只读，尚未晋升或更新长期索引，临时记录保留原始证据。
