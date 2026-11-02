# 页面与主题扩展

## 替换服务与页面行为

Sample 的全部应用页面及其 VM 直接位于应用项目，日常定制直接修改对应 XAML 和 VM。新增页面只需项目 VM、View、集中注册及导航调用，不需要派生默认标题 VM 或注册别名。例如在 Sample 的组合入口注册：

```csharp
SampleCustomization.ConfigureServices(services,
    configureViews: views =>
    {
        views.Register<MyNotebookViewModel, MyNotebookView>();
    });
```

在项目的标题 VM 增加命令，调用 `IGameNavigationService.Navigate<MyNotebookViewModel>()`，然后在 `Views/TitlePage.axaml` 增加绑定该命令的按钮；返回使用现有导航历史。`MyNotebookViewModel` 继承通用 `PageViewModelBase` 即可。所有 Sample 应用页面使用项目自己的实际类型映射；一条映射自动注册页面 VM 和 View 的 Scoped 服务。更大幅度的定制可以替换导航/页面工厂，或通过 `IPlayerApplication` 提供自己的根表面。页面的 XAML 和业务服务不需要字段生成器。

## 页面与主题边界

存档页可直接编辑已有有效存档的名称，Enter 或失焦保存，Escape 放弃。名称保存在原存档 JSON 的 `Description` 字段，最多 120 个字符；清空名称恢复默认槽位名称。重命名不更新存档时间、截图和剧情快照，覆盖保存会保留名称。自定义会话实现可选 `ISaveSlotNamingSession`，自定义存储实现 `ISaveSlotNamingService`；没有该能力的宿主仍可使用只读卡片。

自定义存储可额外实现 `ISaveSlotInfoSource`，避免保存或刷新单个槽位时遍历全部存档。批量更新 `SaveSlots` 时应合并集合通知；单槽保存使用 Replace，以复用其他卡片及缩略图。默认存档缩略图为不超过 480×270 的场景图。

截图弹窗支持可选 `CapturePreviewPngAsync` 小图回调，默认 GameShell 使用不超过 720×405 的预览，包含界面/仅场景分别缓存；`CapturePngAsync` 始终负责正式导出。自定义截图服务保留两者的区别，不能把预览缓存当成正式截图保存。有关控件和场景捕获的边界见 [共享页面规范](../../spec/presentation/game-view.md#截图对话框契约)。

音量与静音共用一行：在音量 SettingItem 上设置 `InlineToggle` 指向同分类的 BoolSetting，并仍将两项放在 `Items` 中，以保留应用、恢复与持久化队列。界面通过 `DisplayItems` 合并显示；修改外观无需改变原设置 ID。

`SampleCustomization.PlayerPalette` 同时供页面与桌面窗口使用，项目 `CreateStyles()` 默认使用该配色。窗口装饰使用独立 CaptionButton/TitleBar 资源；仅修改内容区域的 Button 样式不能覆盖原生关闭按钮。退出确认沿用此配色，在窗口内显示遮罩并暂时禁用背景内容。

退出确认的布局在 `Views/PlayerView.axaml`：使用紧凑桌面确认框比例，默认较小档为 18 设计单位标题、14 正文、靠右的内容宽度按钮，面板/按钮圆角分别为 6/4。字号跟随项目语义资源，按钮尺寸在此弹窗内定义；定制时可独立调整，取消默认焦点、Escape 取消和背景输入屏蔽由内置 `PlayerSurface` 维护。`SampleWindowThemeTests` 支持通过 `GALNET_THEME_CAPTURE_DIR` 输出中英文及最小窗口尺寸的截图。

主页静态标题显式指定 `GalNet.Brush.TextPrimary`，避免退出确认禁用背景时继承容器的半透明禁用文字色。定制主页时保留这个前景色绑定；验证弹窗截图应挂载真实主页，再检查背景标题与取消后的恢复。

- `GamePage` 是稳定的游戏页面宿主；其模板必须保留 `PART_Scene`、`PART_Dialogue`、`PART_Nvl` 和 `PART_Choice` 四个 ContentPresenter。
- 对话与 NVL 的内部部件分别需要 `PART_Typewriter`、`PART_Lines` 和 `PART_ScrollViewer`。不要在剧情播放中重建这些部件。
- Sample 页面及业务命令在项目源码中；通过组合内置呈现、存档行和 Gallery 状态复用机制，不复制 presenter、媒体或游戏会话逻辑。应用布局与尺寸在项目 Styles/ 和 Views/ 中维护。
- Palette 使用完整的语义资源字典。宿主若覆写资源，以玩家根的资源为准；不要跨多个根复用同一 `Styles` 实例。

## 定制字号档位

在 Sample 的 `PlayerTypography.cs` 修改 `Presets`。每档是一套字号，字段依次为页面标题、分组标题、正文、强调、操作、辅助、对白正文；不是统一缩放系数。默认较小档为 `new(64, 34, 18, 26, 18, 12, 22)`，保留桌面现有层级。历史、弹窗等细分资源按其语义角色保留原有比例，映射同样位于 Apply 方法，可按项目独立调整。

例如在创建服务/视图前配置：

```csharp
PlayerTypography.Presets[FontSizePreset.Larger] =
    PlayerTypography.Presets[FontSizePreset.Larger] with { Dialogue = 28, Emphasis = 30 };
PlayerTypography.MobileDefault = FontSizePreset.Larger;
PlayerTypography.DesktopDefault = FontSizePreset.Smaller;
```

`FontSizePreset` 位于 `GalNet.Avalonia.GameHost.Settings`。这里是项目源码配置，不是 Editor 的项目配置字段；玩家设置只选择档位。应用通过局部 DynamicResource 更新字号，不改写剧情内容或重新挂载 presenter。新增页面优先使用现有 `GalNet.Theme.*` 字号资源。截图弹窗通过 DI 读取当前玩家档位，独立创建时使用平台默认。

剧情局部强调可以写 `<size=1.25em>大一点</size>`、`<size=+4>加四个单位</size>` 或 `<size=-2>小声</size>`；嵌套相对于父级字号。优先使用相对值，裸数字绝对字号不会跟随玩家偏好。完整语义与限制见[游戏页面规范](../../spec/presentation/game-view.md#玩家字号与富文本字号)。字号资源只表达玩家档位，普通页面在容器内整体缩放，不能再次按窗口比例折算字号；真机检查时应同时看最终缩放和长文本布局。

## 定制自适应布局

所有平台使用实际内容视口，无需平台标记。`PlayerView.AttachResponsiveShell` 把同一 GameShell 放入一个 Viewbox；普通页面按 1600×900 参考尺寸统一有界缩放，保持实际视口纵横比，游戏与场景鉴赏使用实际视口。Detach 时解除可见性绑定并移除借用的 shell，不应额外创建一套导航或 presenter。

`PlayerLayout.UiScale(Size, bool, double)` 定义普通页面整体缩放，默认限制在 0.65–1.25，移动端下限 0.7、低 DPI 增加 8%；系统 DPI 已由 Avalonia 处理。`Styles/Dimensions.axaml` 统一固定布局尺寸，`PlayerTypography` 统一字号，页面标题、返回和分类直接复用 Heading、Action 和 GroupHeadingSize；不要在测量回调或嵌套样式重复覆写。设置页的页头与正文共用最大800的居中区域，标题与面板左边界对齐。`GamePage.axaml` 和 code-behind 定义独立工具栏的测量/收纳与对白净空。设置 `SettingRow`/`InputBindingRow` 仅移动原有子控件；新增编辑器避免按断点重建控件，否则会丢失草稿或录入状态。存档与图片/视频/场景鉴赏共用 `AdaptiveCardGrid` 和 `CardColumns`/`CardHeight`，三类鉴赏共用 `GalleryCardTemplate`；网格只负责排版，原命令和资源归属不变。

自定义游戏模板时保留内层固定画布及所需 PART_*，历史覆盖层位于工具栏之上；独立工具栏继续设置 GamePage.IsPlayerInteraction。只捕获场景与含 UI 截图的范围不同，见[自适应页面规范](../../spec/presentation/game-view.md#自适应页面与工具栏)。平台安全区交给原 TopLevel/InsetsManager，不叠加另一份刘海边距。

### 定制页面的手柄导航

页面的按钮数量和卡片列数可以变化，默认按当前可见/可用控件与实际位置计算方向目标，不需要维护索引表。在剧情工具栏容器上设置 `input:PlayerNavigation.IsToolbar="True"`（`xmlns:input="using:GalNet.Avalonia.GameView.Input"`），菜单键即可进入。`IsDefault="True"` 指定默认项，`IsTarget="False"` 排除复合控件的内部按钮或仅供键鼠使用的编辑器。危险确认应将取消设为默认。

宿主在 GameShell 外显示模态时，显示后调用 `shell.SetNavigationScope(dialog, cancelAction)`，关闭/释放时在 finally 调用 `shell.SetNavigationScope(null)`；这不替代底层输入阻断。自定义截图服务使用请求中的 `SetNavigationScope` 回调交接。控件确认沿用原生命令/Click 语义；自定义控件应保留键盘可聚焦和激活能力。完整规则见[玩家界面规范](../../spec/presentation/game-view.md#手柄焦点导航)。
