# 实现总结

2026-10-11。

## 结果

新增可选 GalNet.Avalonia.Themes 项目，提供 Classic、Minimal、Paper 三种布局与 Ocean、Rose、Mono 三种独立配色。Sample 默认 Classic + Ocean，开发者在 App.PlayerTheme 选择启动配置，GameShell 自动加载相同主题。标题、加载、设置、存读档、鉴赏、关于、对白/NVL/选择/工具栏、截图弹窗和启动/语言选择界面接入语义资源。

页面映射按默认→主题→宿主 Gallery→宿主普通 View 覆盖组成不可变注册表，最终 View 类型自动注册到 Scoped DI；替换单页无需新主题。GamePage 从 UserControl 迁移为稳定 TemplatedControl，保留 scene/presenter 状态与输入接线，用四个必需内容承载点开放布局。派生 GamePage 与 Runtime 使用同一实例，错误类型和模板明确诊断。

Controls 的基础模板使用 ControlTheme 与中性回退资源。官方布局在玩家根内加载 Fluent 并桥接 Palette，隔离 Sample Semi/Editor；截图弹窗显式加载样式并释放预览图片/订阅。布局不覆盖公共配色键，宿主根 Resources 可局部覆盖。

## 验证

- Presentation：92 通过；包含 15 个新主题测试用例，九种组合均通过真实对白 presenter、等待推进、工具栏输入隔离、选项、NVL 重挂状态和场景 PNG 验证。
- Integration：62 通过，覆盖 Sample 会话、存读档和本地化回归。
- Architecture：25 通过，Themes 仅依赖 Controls/GameView；Shared 无反向依赖。
- Sample 构建、Editor 独立输出目录构建通过。现有 AVLN3001 构造函数加载提示和其他模块 CA1716/CA1861 警告仍存在，无新增编译错误。
- 变更 C# 文件的 dotnet format whitespace 与 IDE0005 检查通过，git diff --check 通过。
- 九种组合所有共享页在 1280×720 headless Skia 加载并渲染，检查标题/游戏/设置/存档/选项/NVL/截图代表画面；修正白色外边距、设置 Slider 压缩与配色、存档卡过度拉伸问题。
- 渲染图片位于忽略目录 artifacts/game-ui-themes；设置 GALNET_THEME_CAPTURE_DIR 可重新导出。

## 偏差与边界

本次影响公开模板契约和多页面，按普通分阶段流程执行，没有使用快速流程压缩。普通页面共享默认 View 和尺寸资源，只为三套标题提供专用 View；不复制每个页面。

仅代码启动配置，不提供直接主题 XAML 元素、运行期换主题/配色、玩家变量绑定或主题 ID 继承管理。Paper 标题固定棕色矢量纹理属于原创装饰美术，文本和交互跟随 Palette。

真实 LibVLC 媒体 opt-in 测试未启用，未验证原生播放、系统对话框、全部 DPI/字体环境。headless 视频页测试加载空播放状态，不声称验证播放后端。Sample 按 1920×1080 画布缩放，外部宿主需验证自己的尺寸；Paper 适合宽屏布局。调试确认/通知和窗口装饰由宿主负责。

默认 Editor 输出被已有进程锁定，独立 OutDir 构建通过；没有停止用户进程。原有 Sample csproj LanguageSelectionView 的 DependentUpon 修改保留。未创建 commit/PR。

## 文档

- [Themes 配置与扩展](../../src/Presentation/GalNet.Avalonia.Themes/README.md)
- [当前游戏页面规范](../../docs/spec/control.md)
- [开发者手册](../../docs/development.md)
- [长期设计与落地差异](../../docs/design/game-ui-themes.md)
- [ADR 0003](../../docs/adr/0003-startup-game-layout-themes.md)

lesson：Headless session 的 async Dispatch 应 await using 清理，避免同步 Dispose 自等待；已记录在 .agents/lessons/L-20261011-01-headless-session-cleanup.md。
