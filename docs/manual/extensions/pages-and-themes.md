# 页面与主题扩展

## 替换服务与页面行为

`AddAvaloniaGameViewPages` 通过 `TryAddScoped` 提供默认服务，项目可以在调用前注册替代实现，或在调用后显式覆盖。标题、设置、游戏页面和 Shell 的默认 VM 可派生；标题命令的处理方法可以重载。注册派生 VM 的别名和实际 View：

```csharp
services.AddScoped<TitlePageViewModel, MyTitleViewModel>();
services.AddScoped<MyNotebookViewModel>();
services.AddAvaloniaGameViewPages(theme,
    configureViews: views =>
    {
        views.Register<MyTitleViewModel, MyTitleView>();
        views.Register<MyNotebookViewModel, MyNotebookView>();
    });
```

`MyTitleViewModel` 派生 `TitlePageViewModel`，自己的按钮命令可调用 `IGameNavigationService.Navigate<MyNotebookViewModel>()`；返回使用现有导航历史。工厂按实际 VM 类型选择 View，因此派生类型也需要映射。更大幅度的定制可以替换 `IGameNavigationService`、`IPageViewFactory`，或由自己的窗口构造 Shell。页面的 XAML 和业务服务不需要字段生成器。

## 页面与主题边界

存档页可直接编辑已有有效存档的名称，Enter 或失焦保存，Escape 放弃。名称保存在原存档 JSON 的 `Description` 字段，最多 120 个字符；清空名称恢复默认槽位名称。重命名不更新存档时间、截图和剧情快照，覆盖保存会保留名称。自定义会话实现可选 `ISaveSlotNamingSession`，自定义存储实现 `ISaveSlotNamingService`；没有该能力的宿主仍可使用只读卡片。

自定义存储可额外实现 `ISaveSlotInfoSource`，避免保存或刷新单个槽位时遍历全部存档。批量更新 `SaveSlots` 时应合并集合通知；单槽保存使用 Replace，以复用其他卡片及缩略图。默认存档缩略图为不超过 480×270 的场景图。

截图弹窗支持可选 `CapturePreviewPngAsync` 小图回调，默认 GameShell 使用不超过 720×405 的预览，包含界面/仅场景分别缓存；`CapturePngAsync` 始终负责正式导出。自定义截图服务保留两者的区别，不能把预览缓存当成正式截图保存。有关控件和场景捕获的边界见 [共享页面规范](../../spec/presentation/game-view.md#截图对话框契约)。

音量与静音共用一行：在音量 SettingItem 上设置 `InlineToggle` 指向同分类的 BoolSetting，并仍将两项放在 `Items` 中，以保留应用、恢复与持久化队列。界面通过 `DisplayItems` 合并显示；修改外观无需改变原设置 ID。

`SampleCustomization.PlayerPalette` 同时供页面与桌面窗口使用，修改配色时保持 `PlayerTheme` 使用同一调色板。窗口装饰使用独立 CaptionButton/TitleBar 资源；仅修改内容区域的 Button 样式不能覆盖原生关闭按钮。退出确认沿用此配色，在窗口内显示遮罩并暂时禁用背景内容。

退出确认的布局在 `Views/MainWindow.axaml`：使用紧凑桌面确认框比例，18px 标题、14px 正文、靠右的内容宽度按钮，面板/按钮圆角分别为 6/4。文字与按钮尺寸只在此弹窗内定义；定制时可独立调整，取消默认焦点、Escape 取消和背景输入屏蔽由代码维护。`SampleWindowThemeTests` 支持通过 `GALNET_THEME_CAPTURE_DIR` 输出中英文及最小窗口尺寸的截图。

主页静态标题显式指定 `GalNet.Brush.TextPrimary`，避免退出确认禁用背景时继承容器的半透明禁用文字色。定制主页时保留这个前景色绑定；验证弹窗截图应挂载真实主页，再检查背景标题与取消后的恢复。

- `GamePage` 是稳定的游戏页面宿主；其模板必须保留 `PART_Scene`、`PART_Dialogue`、`PART_Nvl` 和 `PART_Choice` 四个 ContentPresenter。
- 对话与 NVL 的内部部件分别需要 `PART_Typewriter`、`PART_Lines` 和 `PART_ScrollViewer`。不要在剧情播放中重建这些部件。
- 主题页面复用共享 ViewModel 和命令；只替换布局/样式，不复制 presenter、媒体或游戏会话逻辑。
- Palette 使用完整的语义资源字典。宿主若覆写资源，以玩家根的资源为准；不要跨多个根复用同一 `Styles` 实例。
