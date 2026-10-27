# Editor 创作工作区

## 编写与参数

Group 行内默认展示常用字段。单击选择条目后，Inspector 显示完整参数和执行条件；箭头独立控制行内展开；编辑器设置可指定新添加条目的参数是否默认展开。搜索框可以按正文、类型、条件或显示序号查找。锁定 Inspector 可持续查看指定条目；辅助面板不会清空它。

人物输入按姓名或 ID 搜索后，可直接按 Enter 采用高亮候选，也可用方向键进入列表选择。输入法组合期间的 Enter 保留给输入法。

未完成输入允许暂时离开或关闭面板，在同一项目中重新打开仍可继续。Escape 取消当前输入；Problems 可以定位或取消已经关闭面板的草稿。Save All 会尝试提交输入，冲突、无效值及输入法尚未完成的组合文字需要先处理。待处理输入不会跨应用重启保留。

## 检查与导航

点击状态栏的检查状态或从视图菜单打开 Problems，使用“检查当前内容”检查未保存的作者版本。检查结果与保存状态分开；检查不完整或内容后来改变时会明确提示。点击具体问题或变量引用，打开对应 Group/条目及参数；目标已经移除时不会跳到占用旧行号的另一个条目。

Graph 顶部可以搜索节点名称/ID、适配全图、定位选择和缩放；小地图按需开启。资源工具栏提供导入、新建、类型筛选和列表视图，搜索范围为整个项目。

## 布局

角色表和变量表的新增入口位于列表右上角“＋”，应用、还原和删除使用图标按钮，悬停可查看说明。变量的玩家/存档子页分别记住选中项和未应用草稿；搜索过滤或关闭重开后可继续编辑。角色草稿同样随项目会话保留。两类面板的输入仍需点击应用，Save All 保存已应用的声明。

资源 Inspector 上方编辑属性，下方为可折叠、可通过分隔条调整空间的预览；图片、音频和图片变体使用同一底部区域。资源 ID 与路径可选中复制，变体 JSON 修复区位于属性区域，预览不再夹在配置字段之间。

Group 工具栏的添加和追加按钮会打开可搜索的条目类型列表，选择后按批量数量插入，按钮名称保持不变。Timeline 的添加事件按钮使用同样的选择方式；选择单个事件后在 Inspector 修改参数，点击应用提交或取消放弃草稿。前后关键帧按钮也会停在事件所在帧。

窗口菜单中手动选择剧情编写、动画编辑或游戏调试布局；工具打开不会替你切换预设。已有保存布局继续优先恢复。最大化面板可以再恢复到原布局。Timeline 左侧树与画布之间的分隔条可以拖动；对象轨道与时间轴事件之间的水平分隔条可调整上下区域比例。底部保留显示完整时长，右侧显示当前帧和缩放滑条。

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

Common 只影响行内展示；AdvancedValue 仍在 Inspector 编辑。按现有项目契约流程重建 DLL，使 Editor 和游戏加载相同契约。手写 schema 可设置 `new EditorHints(IsCommon: true)`；自定义 DTO 编辑工厂读取 `DtoEditorContext.DisplayMode`，默认表单会自动筛选。嵌套对象继续使用自身编辑器，不自动铺开所有成员。完整契约见 [共享表单](../spec/control.md) 和 [Group 编辑](../spec/editor-groups.md)。

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
