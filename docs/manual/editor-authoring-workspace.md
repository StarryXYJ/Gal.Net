# Editor 创作工作区

## 编写与参数

Group 行内默认展示常用字段。单击选择条目后，Inspector 显示完整参数和执行条件；箭头独立控制行内展开。搜索框可以按正文、类型、条件或显示序号查找。锁定 Inspector 可持续查看指定条目；辅助面板不会清空它。

人物输入按姓名或 ID 搜索后，可直接按 Enter 采用高亮候选，也可用方向键进入列表选择。输入法组合期间的 Enter 保留给输入法。

未完成输入允许暂时离开或关闭面板，在同一项目中重新打开仍可继续。Escape 取消当前输入；Problems 可以定位或取消已经关闭面板的草稿。Save All 会尝试提交输入，冲突、无效值及输入法尚未完成的组合文字需要先处理。待处理输入不会跨应用重启保留。

## 检查与导航

点击状态栏的检查状态或从视图菜单打开 Problems，使用“检查当前内容”检查未保存的作者版本。检查结果与保存状态分开；检查不完整或内容后来改变时会明确提示。点击具体问题或变量引用，打开对应 Group/条目及参数；目标已经移除时不会跳到占用旧行号的另一个条目。

Graph 顶部可以搜索节点名称/ID、适配全图、定位选择和缩放；小地图按需开启。资源工具栏提供导入、新建、类型筛选和列表视图，搜索范围为整个项目。

## 布局

角色表和变量表的新增入口位于列表右上角“＋”，应用、还原和删除使用图标按钮，悬停可查看说明。变量的玩家/存档子页分别记住选中项和未应用草稿；搜索过滤或关闭重开后可继续编辑。角色草稿同样随项目会话保留。两类面板的输入仍需点击应用，Save All 保存已应用的声明。

资源 Inspector 上方编辑属性，下方为可折叠、可通过分隔条调整空间的预览；图片、音频和图片变体使用同一底部区域。资源 ID 与路径可选中复制，变体 JSON 修复区位于属性区域，预览不再夹在配置字段之间。

窗口菜单中手动选择剧情编写、动画编辑或游戏调试布局；工具打开不会替你切换预设。已有保存布局继续优先恢复。最大化面板可以再恢复到原布局。Timeline 左侧树与画布之间的分隔条可以拖动，底部可显示完整时长或定位播放头。

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
