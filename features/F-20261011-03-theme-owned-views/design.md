# 设计

当前 GameView 普通页面与主题页面重复，缺失主题映射会悄悄回退。改为必需的启动主题，注册顺序为共享 GamePage 宿主 → 主题 → Gallery 扩展 → 宿主 View 覆盖 → 完整性校验。GameView 不反向依赖 Themes。

Gallery 类型与 View 映射分离：内置 cg/video/audio 只注册对应 VM，主题提供 View。扩展的 Add/Replace 仍可同时指定自定义 View。

通用玩家样式、工具栏移动到 Themes/Common，布局主题保留各自 GamePage 模板。GameShell 只提供导航容器，主题决定其背景。

ScreenshotDialog 成为无布局的共享行为基类，主题派生 View 提供约定的具名控件并在加载 XAML 后初始化行为。截图 VM 也参与 View 注册，宿主可按相同机制覆盖它；服务每次创建并释放对话框，避免复用已释放的预览。截图捕获、文件选择、保存和 Bitmap 生命周期仍归 GameView。

公共 API 不再允许省略主题；外部主题需提供完整页面集与截图 View。注册器验证 GamePage/ScreenshotDialog 基类契约并列出缺失映射。Sample 保持 Classic，Editor 显式 Classic。删除旧布局不会删除共享 VM、呈现器、命令和生命周期。
