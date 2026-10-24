# Editor 创作工作区

## 编写与参数

Group 行内默认展示常用字段。单击选择条目后，Inspector 显示完整参数和执行条件；箭头独立控制行内展开。搜索框可以按正文、类型、条件或显示序号查找。锁定 Inspector 可持续查看指定条目；辅助面板不会清空它。

未完成输入允许暂时离开或关闭面板，在同一项目中重新打开仍可继续。Escape 取消当前输入；Problems 可以定位或取消已经关闭面板的草稿。Save All 会尝试提交输入，冲突、无效值及输入法尚未完成的组合文字需要先处理。待处理输入不会跨应用重启保留。

## 检查与导航

点击状态栏的检查状态或从视图菜单打开 Problems，使用“检查当前内容”检查未保存的作者版本。检查结果与保存状态分开；检查不完整或内容后来改变时会明确提示。点击具体问题或变量引用，打开对应 Group/条目及参数；目标已经移除时不会跳到占用旧行号的另一个条目。

Graph 顶部可以搜索节点名称/ID、适配全图、定位选择和缩放；小地图按需开启。资源工具栏提供导入、新建、类型筛选和列表视图，搜索范围为整个项目。

## 布局

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
