# 玩家界面主题

此可选项目集中提供 Classic、Minimal、Paper 三套布局，以及独立的 Ocean、Rose、Mono 配色。布局和配色可以任意组合。Core、Runtime 和 GameView 不依赖此项目。

## 启动配置

```csharp
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.Themes;

IGameLayoutTheme theme = new PaperTheme(GamePalette.Ocean);
services.AddAvaloniaGameViewPages(theme: theme);
```

GameShell 自动从 DI 获取主题并在自己的视觉根加载样式。首次语言选择等位于 GameShell 外的玩家界面，应使用同一个主题对象：

```csharp
playerRoot.Classes.Add("galnet-player");
playerRoot.Styles.Add(theme.CreateStyles());
```

每次 CreateStyles 创建独立资源实例；不要把同一个 Styles 实例挂到多个根。官方主题在玩家根使用 Fluent 基础控件模板，并把 Fluent 颜色映射到所选配色；宿主或编辑器可以继续使用 Semi。截图弹窗会显式引入同一套资源。

配置在启动期固定，没有运行期主题或配色切换 API。主题配置对象通过代码使用，不能直接写成 `<themes:PaperTheme/>` 样式元素。资源字典和 ControlTheme 仍可在 XAML 中开发。

## 内置示例

| 布局 | 默认配色 | 页面与控件布局 |
| --- | --- | --- |
| Classic | Ocean | 顶部页头与右侧返回、左侧标题菜单、底部宽对白 |
| Minimal | Mono | 页面侧栏与底部返回、分栏标题、右下对白 |
| Paper | Rose | 居中页面标题与左侧返回、纸张纹理、右侧对白栏、衬线字体 |

每套布局在 Layouts 目录提供自己的标题、设置、存档、关于、加载、Gallery 入口/CG/音频/视频列表、图片/视频详情及诊断页 XAML，并注册对应 View；只复用共享 ViewModel 和命令，不复制业务与媒体生命周期。GamePage 使用主题的 ControlTheme。页面容器无外框，主题内的返回区域保持一致。Paper 的标题纹理由项目内手绘矢量实现，固定棕色属于装饰美术；文字和交互控件跟随配色。

设置和 Gallery 的列表/媒体详情采用各主题自己的页头或侧栏。图片保留比例并限定在与返回区域分离的媒体区。宿主可以覆盖 PageMaxWidth、PageHeaderMinHeight、BackMinWidth、BackMinHeight 尺寸资源；宿主使用设计画布缩放时，点击区域也随画布缩放。

对白正文/人物名字号为 Classic、Paper 的 22/26 和 Minimal 的 20/24，名字比正文大 4。通过 DialogueFontSize、SpeakerFontSize 独立调整；中性回退为 18/22。

## 只替换一个页面

```csharp
services.AddAvaloniaGameViewPages(
    theme: new ClassicTheme(GamePalette.Rose),
    configureViews: views => views.Register<TitlePageViewModel, MyTitlePage>());
```

顺序为共享 GamePage 宿主 → 主题页面 → 宿主 Gallery 注册 → 宿主 View 覆盖 → 完整性校验。最后一种普通页面覆盖优先级最高；映射在组合完成后不可变。普通 View 类型自动补充 Scoped DI 注册，可在构造函数注入宿主服务。新增 ViewModel 及其服务仍由开发者注册。页面使用共享 ViewModel 的命令和本地化绑定，不必复制业务逻辑或实现一个新主题。

GamePageViewModel 的自定义 View 必须继承 GamePage；运行 presenter 和页面工厂使用同一 Scoped 实例。仅改变游戏布局通常只需替换 GamePage 的 ControlTheme。

## 自定义控件与游戏布局

在玩家根的 Resources 放置资源可覆盖主题默认值：

```xml
<Grid.Resources>
  <Thickness x:Key="GalNet.Theme.PageMargin">60</Thickness>
  <SolidColorBrush x:Key="GalNet.Brush.Surface" Color="#242A36" />
</Grid.Resources>
```

控件模板参考 Controls/Styles/Generic.axaml 和本项目 Layouts 下的 Resources.axaml。GamePage 必须保留四个 ContentPresenter，并呈现运行实例拥有的内容：

```xml
<ControlTheme x:Key="{x:Type page:GamePage}" TargetType="page:GamePage">
  <Setter Property="Template">
    <ControlTemplate>
      <Grid>
        <ContentPresenter Name="PART_Scene" Content="{TemplateBinding Scene}" />
        <ContentPresenter Name="PART_Dialogue" Content="{TemplateBinding Dialogue}" />
        <ContentPresenter Name="PART_Nvl" Content="{TemplateBinding Nvl}" />
        <ContentPresenter Name="PART_Choice" Content="{TemplateBinding Choice}" />
      </Grid>
    </ControlTemplate>
  </Setter>
</ControlTheme>
```

实际布局应设置每个承载点的尺寸和位置；这是最小契约示例。内容不能重复挂载。Themes/Common 的 GameToolbar 可绑定 GamePageViewModel，新增可交互区域设置 `page:GamePage.IsPlayerInteraction="True"`，防止操作同时推进剧情。按钮、选择列表、文本框和 RangeBase 控件已有默认排除。

DialoguePresenter 保留 PART_Typewriter，NvlPresenter 保留 PART_Lines 和 PART_ScrollViewer；这些内部部件承担运行状态，不能在播放过程中任意重建。GamePage 在首条对白、NVL 和选项前检查布局承载点，缺失时明确报错。

## 自定义配色和主题

复制 Palettes 中一个完整字典到自己的程序集：

```csharp
var palette = new GamePalette(new Uri("avares://MyGame.View/Palettes/Night.axaml"));
services.AddAvaloniaGameViewPages(theme: new MinimalTheme(palette));
```

字典须提供 GamePalette.RequiredBrushes 列出的 21 个 `GalNet.Brush.*` 语义画刷。官方 Fluent 桥接还需要相应 `GalNet.Palette.*Color` 颜色资源；参考内置字典保留完整 Color/Brush 配对。基础 Colors 是原始色表，Palette 负责语义映射，布局资源使用 DynamicResource 引用画刷。内置布局不重新定义公共语义颜色。

外部主题实现 IGameLayoutTheme：CreateStyles 加载自己的样式与资源，RegisterViews 提供全部内置页面以及 ScreenshotDialogViewModel 的映射。GameLayoutTheme 基类用于本程序集内置目录布局；外部主题不必继承它。主题参数必需；GameView 没有默认普通页面或样式回退，最终映射缺失会在注册时列出并拒绝初始化。外部主题可以先调用某个内置主题的 RegisterViews，再覆盖部分映射，以复用页面。

## 验证与范围

GameThemeTests 覆盖九种组合的页面加载、Skia headless 渲染、配色隔离、宿主覆盖、对白、NVL、选择输入及场景截图。设置 `GALNET_THEME_CAPTURE_DIR` 可导出检查图片。

主题覆盖共享玩家页面、Sample 启动与语言选择、截图弹窗；调试窗口、调试确认/通知、编辑器自身 UI 和系统文件选择器由宿主负责。headless 页面渲染不会验证实际 LibVLC 播放或系统对话框。Sample 使用 1920×1080 设计画布缩放游戏界面；自定义宿主应按自己的画布尺寸检查布局，Paper 的侧栏适用于宽屏布局。

## 截图布局与共享行为

三套布局分别提供 ScreenshotDialog 派生 View，共享基类在 GameView；主题只维护布局。XAML 加载后调用 InitializeDialog，需提供具名控件 Preview(Image)、DirectoryBox/TextBox、FileNameBox/TextBox、IncludeUiBox(CheckBox)、BrowseButton/CancelButton/SaveButton(Button)、ErrorBlock(TextBlock)。缺少控件会明确报错。宿主可注册 `views.Register<ScreenshotDialogViewModel, MyScreenshotDialog>()` 覆盖单个对话框。

对话框每次通过 DI 构造独立实例，并由截图服务释放；普通页面保持 Scoped。通用玩家样式与工具栏位于 Common，GameView 的 Page 目录仅保留 GameShell 导航宿主、GamePage 行为控件和 ScreenshotDialog 行为基类。