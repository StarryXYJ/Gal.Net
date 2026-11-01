# Sample 模板交付与接线精简总结

P1–P3 范围内完成，2026-11-01。唯一 Sample 继续拥有页面、VM 和应用选择；内置层集中播放协议、呈现状态、媒体、取消及生命周期机制。

- 启动：GameLaunchFlow 统一新游戏、继续、读档的普通/准备后启动适配，项目 VM 保留业务和导航；既有 RunAsync 兼容。
- 视觉：SampleCustomization 选择加载停留和公告时长，PlayerSurface 保留取消、确认抑制和清理机制。
- 布局：普通游戏和场景重播使用相同页面映射；普通游戏保留 scoped 页面工厂扩展，重播新建独立 state/VM，不污染玩家进度。
- 交付：export-template.py 按清单原样复制同一应用及必要附件。交付根切换包依赖并明确版本，不改工程 XML、不复制引擎源码。按钮、VM、新页面和配色可直接编辑项目文件。
- 依赖：移除未使用的 LibVLCSharp.Avalonia 和 Markdown 中央版本残留，保留实际媒体核心；更新锁图与包消费者门禁，无包版本升级。

验证包括六个包消费者、仓库外定制 15 项、最终源码布局 14 项、实际音视频/重播 8 项及取消释放 1 项；Windows 原生会话、两种自包含发行与两个真实程序启动退出通过。完整 Release build 0 错误，回归 2560 passed / 3 个既有 Editor 相关失败，23 未执行，不能称整套 CI 绿色。原始路径、失败修正和完整计数见 [execution.md](execution.md)。

Android 真机依用户决定延期；本轮未新增 Linux 原生、macOS/iOS、Browser、真实手柄证明。缓存切换发现 Browser workload 包身份差异，保留原 hash，后续专查；默认包 native 支持也未扩大。独立模板不附游戏内容、重命名器或自动升级合并器。

后续可分别处理 Editor 对 GameView 的单点目录依赖、Engine 内调试核心的公开包归属、平台后端拆分；这些会影响其他模块或公开交付契约，未混入本轮。依据见 [依赖分析](dependency-review.md)。

使用方式见 [Sample README](../../src/Samples/GalNet.Sample.CrossPlatform/README.md)、[定制手册](../../docs/manual/extensions/customize-sample.md) 和 [页面规范](../../docs/spec/presentation/game-view.md)。
