# DTO 字段提示与参数控件

## 注册 DTO 参数控件

共享 UI 构建位于 GalNet.Avalonia.Controls 项目，命名空间为 GalNet.Game.Controls.Forms。共享玩家的 SamplePlayerServices.CreateFieldEditors 是组合入口，桌面 SampleCustomization 转发到它：先 CreateDefault，再 RegisterDto<T>、RegisterMember<T> 或 RegisterDefault<T>；内置已有类型用 ReplaceDefault<T>，最后 Freeze。每个工厂返回新的 FieldEditorLease，不能重用已挂载 Control。

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

具体生命周期、冻结、错误及当前支持范围见[Schema 字段表单](../../spec/presentation/game-view.md#schema-字段表单)。

## 为项目 DTO 标记常用字段

```csharp
[GenerateFieldSchema]
public partial class MyOptions
{
    [Field(0), Common]
    public string? Content { get; set; }

    [Field(1)]
    public float AdvancedValue { get; set; }
}
```

Common 只影响行内展示；AdvancedValue 仍在 Inspector 编辑。按现有项目契约流程重建 DLL，使 Editor 和游戏加载相同契约。手写 schema 可设置 `new EditorHints(IsCommon: true)`；自定义 DTO 编辑工厂读取 `DtoEditorContext.DisplayMode`，默认表单会自动筛选。嵌套对象继续使用自身编辑器，不自动铺开所有成员。完整契约见 [共享表单](../../spec/presentation/game-view.md) 和 [Group 编辑](../../spec/editor/groups.md)。

## 选择数值编辑器

Editor 的普通 `numeric` 字段默认显示上下调整框；需要明确指定时，可使用以下元数据：

```csharp
[Field(0, EditorId = ValueEditorIds.NumericUpDown, Step = .1)]
public float Duration { get; set; }

[Field(1, EditorId = ValueEditorIds.Slider, Minimum = 0, Maximum = 1, Step = .01)]
public float Opacity { get; set; } = 1;

[Field(2, EditorId = ValueEditorIds.NumericText)]
public decimal ExactValue { get; set; }
```

Slider 在 Editor 中带精确输入框；步长只影响调整，输入 `.537` 仍可提交为 `0.537`，不会强制对齐到 `.01`。浮点输入支持 `.3` 和 `-.3`，Enter/失焦确认后规范显示。大整数和 decimal 始终保留无损文本路径；滑块需明确有限范围，不适用时回退并显示原因。宿主可通过 `registry.Replace(ValueEditorIds.Numeric, factory)` 或成员/类型注册覆盖选择，元数据不改变业务合法值。共享设置表单原有默认 numeric 文本路径保留。

## 声明嵌套结构

普通生成 DTO、数组/List 和字符串键字典会使用公共属性遍历与通用表单。需要保留 JsonElement wire 类型的固定对象，可以显式关联结构类型：

```csharp
[GenerateFieldSchema]
public partial class PositionData
{
    [Field(0)] public float X { get; set; } = 0f;
    [Field(1)] public float Y { get; set; } = 0f;
}

[GenerateFieldSchema]
public partial class CustomOptions
{
    [Field(0, StructureType = typeof(PositionData))]
    public JsonElement Position { get; set; }
}
```

元数据关联不改写项目 JSON，也不将普通字符串当 JSON 解析。只有原契约确实支持历史编码时才声明 StructureAllowsJsonString；StructureIgnoresCase 和 StructureUsesFieldIds 同样属于显式兼容规则。专用 EditorId/成员/类型工厂仍可负责布局，复用公共字段描述与属性访问。

手写 schema 可组合 ValueStructure。例如逐个 Map 值声明文本引用，而不是把整个字典标成字符串字段：

```csharp
var textValue = new FieldDescriptor("value", new(typeof(string)),
    semantic: FieldValueSemantic.LocalizedText);
var values = new ValueDescriptor(typeof(JsonElement))
    .WithStructure(ValueStructure.Map(textValue));
```

读取/检查可使用 `using var document = new ValueDocument(field, json);`，然后调用 GetIterator、FindProperty 或 Validate。Patch 只返回候选值和诊断，宿主仍须通过原作者意图提交，复核来源并记录历史。共享 PropertyFieldBinding 为专用控件提供子字段访问；通过相同父 Context 和 registry 继承草稿、宿主能力和数字编辑路径。

通用表单按 Header 分组并缩进，字典支持键/值操作，重复键拒绝提交。字段值草稿由项目会话保留；新增/改键等结构操作的未成功键名仅留在当前控件。八层/200 元素的 UI 回退与引用分析预算分别生效。

应用特效与 SpeedLines/Impact 预设的参数分析复用 shader metadata，shader 表单与扫描共用 ShaderEffectFields；音频效果复用 ISEffectCatalog 中的字段，更新参数按前文挂载的实例解析。Editor 的 ProjectSceneTargets 通过 IEntryParameterMetadataSource 将现有资源查询快照接给 ProjectLocalizationQuery，随资源刷新重新分析，托管 shader 未保存的内容同样生效。独立调用 LocalizationReferenceIndex 可通过 EntryParameterMetadata 提供宿主目录与 shader 声明；未提供时仍能分析内置音效。

Timeline 的轨道、关键帧和事件参数也进入同一遍历；运行 DTO schema 与 editorId/editorTangents 作者 metadata 分别提供结构，事件按项目 catalog/type 选择参数字段。事件中的 shader 资源会进入同一 metadata 快照。已知 shader 允许额外参数，切换后保留的 angle 等旧值属于合法数据，不需要为消除 warning 手工删除；只有声明字段仍按元数据校验。未知效果类型、资源声明不可用，以及其他尚未关联结构的 JSON 仍提示未覆盖，不能仅靠增加 UI 工厂来宣布引用检查完整。
