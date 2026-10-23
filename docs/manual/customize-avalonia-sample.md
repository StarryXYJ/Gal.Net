# 定制 Avalonia Sample

`GalNet.Sample.Avalonia` 是项目级组合根示例：保留启动参数、项目选择、本地化和调试策略；通用会话、页面与展示组合应优先复用 `Avalonia.GameHost`、`Avalonia.GameView` 和 Themes，而不是复制其生命周期代码。

外部 Editor 调试的启动配置、运行变量面板、路线定位和可选宿主接入见[对接外部游戏调试](external-game-debugging.md)。

只需用两个字符串调用项目逻辑时，可用内置 `custom.invoke`，在运行组合中注册 action，无需为每个 action 新建项目契约；见[通用 action 注册与模式语义](custom-actions.md)。

## 推荐改造顺序

1. **建立项目契约。** 复制或引用 `GalNet.Sample.Contracts`，在 `ProjectSchemas.Modules` 中列出项目允许的 `IEntryModule`。构建后用 `scripts/build-project-contracts.ps1` 部署到内容项目的 `Contracts/`。CLI/Editor 与玩家必须消费同一契约集合。
2. **配置运行组合。** 在 Sample 的 `SampleCustomization` 中配置 catalog、执行模块、资源/玩家存储、音频 host 和项目启动策略。把项目专属 primitive 注册在这里；不要修改 `App`、Session 或关闭链来接入一条业务能力。
3. **定制外观。** 通过 `AddAvaloniaGameViewPages` 选择 `ClassicTheme`、`MinimalTheme` 或 `PaperTheme`，也可在 `configureViews` 覆盖单个页面。运行期主题切换不受支持。
4. **扩展项目功能。** schema/DTO 与 composite 放在契约项目；执行模块放在 Avalonia 或项目执行程序集。参数 DTO 用 `GenerateFieldSchema`/字段特性生成 descriptor，平台控件仅在页面/设置工厂中处理。

## 页面与主题边界

- `GamePage` 是稳定的游戏页面宿主；其模板必须保留 `PART_Scene`、`PART_Dialogue`、`PART_Nvl` 和 `PART_Choice` 四个 ContentPresenter。
- 对话与 NVL 的内部部件分别需要 `PART_Typewriter`、`PART_Lines` 和 `PART_ScrollViewer`。不要在剧情播放中重建这些部件。
- 主题页面复用共享 ViewModel 和命令；只替换布局/样式，不复制 presenter、媒体或游戏会话逻辑。
- Palette 使用完整的语义资源字典。宿主若覆写资源，以玩家根的资源为准；不要跨多个根复用同一 `Styles` 实例。

## 上线前检查

- Project `Contracts/` 是与当前 GalNet API 匹配的可信构建产物；重新构建游戏或 CLI 不会自动更新该目录。
- 自定义 entry 已在 CLI 校验/编译、Avalonia 运行和保存恢复路径中测试；Headless 缺少平台实现时应清晰报错。
- 主题的普通页面映射完整，`GamePage` 布局可在目标分辨率和输入方式下工作。
- 退出、切换项目和读档均由会话链路释放旧的 presentation、媒体与资源。

主题的可复制示例在[Themes README](../../src/Presentation/GalNet.Avalonia.Themes/README.md)；运行与页面的精确约束见[Avalonia 游戏页面规范](../spec/control.md)。

## 注册 DTO 参数控件

共享 UI 构建位于 GalNet.Avalonia.Controls 项目，命名空间为 GalNet.Game.Controls.Forms。Sample 的 SampleCustomization.CreateFieldEditors 是组合入口：先 CreateDefault，再 RegisterDto<T>、RegisterMember<T> 或 RegisterDefault<T>；内置已有类型用 ReplaceDefault<T>，最后 Freeze。每个工厂返回新的 FieldEditorLease，不能重用已挂载 Control。

~~~csharp
var editors = EditorRegistry.CreateDefault();
editors.RegisterDto<MyOptions>(context =>
{
    var panel = new StackPanel();
    panel.Children.Add(new TextBlock { Text = "Custom settings" });
    panel.Children.Add(context.CreateDefaultForm());
    return new FieldEditorLease(panel);
});
return editors.Freeze();
~~~

MyOptions 代表项目自己的 schema DTO 类型；真实业务提交仍由宿主 binding 负责。只要替换一个字段，使用 RegisterMember<MyOptions>(x => x.SomeProperty, factory)，当前只支持直接的生成 DTO 属性，字段 JsonPropertyName 会自动解析。在根工厂中调用 CreateDefaultForm("已由自定义控件负责的字段ID") 可以保留剩余字段，排除参数使用 schema 的字段 ID。

多项 DTO 用各自 DtoBinding 或作者文档 binding 创建 DtoEditorItem，再传给 DtoPanel.ItemsSource；每项 Id 唯一，同一 Type 可出现多次。只展示一个 DTO 时也可使用同一分派逻辑的 DtoEditorHost。IFieldDto 的 CLR 对象仍由宿主管理，Editor 直接绑定作者 JSON；只为查找控件不需要创建完整 DTO。原子多字段 patch 必须提供 DtoEditorItem 的 commit 委托；普通单字段 DTO/设置绑定继续沿用现有提交路径。

普通生成 DTO 的嵌套对象和常用列表/一维数组会自动获得可折叠表单，不需要逐类型注册；内部字段继续使用相同 registry。展开创建子控件，折叠释放；未知类型、复杂 converter、过深或过长的结构使用明确 JSON 回退。专业时间轴、曲线等仍通过成员或整 DTO 覆盖接入，不为所有 JsonElement 注册同一个专属控件。

共享默认样式中的 FieldEditorActions 提供显式默认值/null/缺省/恢复草稿。缺省提交需要宿主保存 presence，普通 CLR DtoBinding 没有这种能力时返回 dto.presence，不能把删除字段等同于 CLR 赋 null；Sample 的设置应用/保存规则保持由原宿主管理。

具体生命周期、冻结、错误及当前支持范围见[Schema 字段表单](../spec/control.md#schema-字段表单)。
