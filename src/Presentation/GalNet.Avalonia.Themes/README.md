# 配色与字体

本项目只提供 `GamePalette` 语义配色、颜色资源和 `GameTypography` 字体回退。页面、菜单、字体/尺寸选择和应用样式位于唯一的 CrossPlatform Sample 应用项目；这里不注册页面，也不提供整套布局主题。

内置配色为 `GamePalette.Mono`、`Ocean`、`Rose`。`CreateResources()` 创建独立资源字典并验证 21 个必需语义画刷；`CreateFluentPalette()` 只将语义颜色映射到 Avalonia Fluent 的颜色资源，不决定页面和控件布局。每个根创建自己的样式/资源实例，避免多个窗口相互影响。

```csharp
public static GamePalette PlayerPalette { get; } = GamePalette.Mono;
// SampleCustomization.CreateStyles() 加载项目 Styles/Player.axaml 和 Dimensions.axaml。
```

修改一页直接编辑 Sample 的 `Views/<Page>.axaml` 与对应 `ViewModels/`。修改所有页面的尺寸和默认按钮样式编辑项目 `Styles/`；覆盖单个按钮的 Avalonia ControlTheme、样式或 XAML 不要求实现新的注册协议。新增一种完整页面基础以后由新的模板源码表达，目前只维护一个模板。

`GalNet.Brush.*` 和 `GalNet.Palette.*Color` 是语义颜色；`GalNet.Theme.*` 仅是现有 Avalonia 资源键，应用尺寸由 Sample 赋值。控件库提供必要中性默认样式，`GameView/Styles/Controls.axaml` 提供游戏承载点和跳过确认的控件默认样式。

GamePage 保留 Scene、Dialogue、Nvl、Choice 承载点；具体菜单、工具栏和布局由项目 XAML 决定。运行状态通过 `GamePage.Presentation` 指定，独立于页面 DataContext。详见 [页面规范](../../../docs/spec/presentation/game-view.md) 和 [Sample 定制](../../../docs/manual/extensions/customize-sample.md)。

`GameTypography.CreateFontManagerOptions()` 统一平台字体回退，中文优先 Noto/思源黑体，未命中时由系统匹配。Sample 显式选择已打包的 Inter；Inter 不包含中文，实际中文显示仍需要系统或项目提供 CJK 字体。
