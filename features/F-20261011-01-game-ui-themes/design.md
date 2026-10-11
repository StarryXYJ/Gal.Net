# 实施设计

依据 [长期设计](../../docs/design/game-ui-themes.md)，采用启动期固定配置。

IGameLayoutTheme 位于 GameView 的组合命名空间，提供 Avalonia 样式入口和页面注册方法。AddAvaloniaGameViewPages 保留旧调用兼容性，增加主题参数；主题先注册普通页与 Gallery 覆盖，宿主回调最后覆盖。最终 View 类型自动补充 Scoped DI 注册，避免开发者覆盖映射却遗漏 View 注册。

Themes 独立项目包含色表、完整 Palette、共享样式、Classic/Minimal/Paper 标题页面与 GamePage ControlTheme。通过同一个布局主题对象提供资源与页面注册，Sample 在 App 初始化选择主题，在 DI 注册前使用同一对象。资源可应用于游戏根以支持 Editor 隔离。

GamePage 迁移为 TemplatedControl，拥有稳定 Scene、Dialogue、Nvl、Choice 内容；模板用 ContentPresenter 安排布局，工具栏是主题模板里的共享命令绑定。必需承载点在 OnApplyTemplate 验证。GameShell 的导航、截图及会话生命周期保留。

所有默认玩家页移除固定语义色，引用统一资源，布局参数按需提取，基础资源保证未引用 Themes 的嵌入宿主仍能使用默认页面。每套布局通过模板与主题专用资源调整控件和页面；普通页面可复用基础页面，不机械复制业务逻辑。

不支持运行中换完整模板。输入排除由交互标记与常见交互控件识别保障，避免自定义工具栏操作触发剧情推进。

验证采用 Presentation 层注册与 headless Avalonia 模板/页面测试、Sample/Desktop 构建、Architecture 门禁及格式检查。新 UI 资源须渲染验证；若环境无法显示原生 UI，在 summary 说明验证范围。
