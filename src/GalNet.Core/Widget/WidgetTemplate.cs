namespace GalNet.Core.Widget;

/// <summary>
/// 控件模板 —— 类别下的具体样式定义（XAML + 参数定义）。
/// UI 层通过继承此类并提供 CreateView 实现来创建具体控件。
/// </summary>
public abstract class WidgetTemplate
{
    /// <summary>模板唯一 ID</summary>
    public string Id { get; }

    /// <summary>所属 WidgetCategory</summary>
    public string Category { get; }

    protected WidgetTemplate(string id, string category)
    {
        Id = id;
        Category = category;
    }

    /// <summary>创建默认配置</summary>
    public abstract WidgetConfig CreateDefaultConfig();

    /// <summary>从配置创建控件实例（由 UI 层实现）</summary>
    public abstract object CreateView(WidgetConfig config);
}
