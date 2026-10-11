# 0003：启动期组合玩家布局主题与独立配色

状态：Accepted，2026-10-11。

## 背景

GalNet 的行为控件应可复用，开发者需要完整玩家界面的主题参考，同时能仅替换某一个页面。GamePage 的 scene/presenter/打字机/NVL 状态已有共享生命周期，复制游戏页面逻辑会破坏状态与输入接线。

## 决定

采用启动期固定的 IGameLayoutTheme：提供局部样式及普通页面注册。页面映射按默认→主题→宿主覆盖组合后固定，最终 View 使用当前游戏 Scope。布局和 Palette 独立，官方示例集中在可选 Themes 项目；Shared 与 GameView 不反向依赖该集合。

普通页面使用共享 ViewModel 配合可替换 View。GamePage 为稳定 TemplatedControl，拥有 Scene/Dialogue/Nvl/Choice 内容，主题以验证过的四个 ContentPresenter 安排布局。自定义 GamePage 类型须继承该稳定宿主，并与 Runtime 使用同一 Scoped 实例。

官方主题把 Fluent 限制在玩家视觉根内，公共色表映射为语义 Color/Brush；外层宿主资源拥有覆盖优先权。截图等独立资源路径显式引入主题。

## 备选与后果

只抽颜色无法改变结构；所有 Page 模板化会增加简单页面的扩展负担；每套主题复制游戏页面会复制生命周期。选定方案集中运行行为，并允许单页覆盖而无需新主题。

运行中切换主题需要独立处理打字机任务、NVL 历史、模板部件和媒体状态，本期不支持。DynamicResource 用于资源引用，不代表完整运行期切换保证。官方布局适合 Sample 的宽屏设计画布；宿主不同尺寸需自行验证。

来源：[feature](../../features/F-20261011-01-game-ui-themes/feature.md)、[设计](../design/game-ui-themes.md)、[当前契约](../spec/control.md)。
