using GalNet.Core.Widget;

namespace GalNet.Core.Screen;

/// <summary>
/// 页面模板 —— 类别下的 XAML 布局定义。
/// </summary>
public abstract class ScreenTemplate
{
    /// <summary>模板唯一 ID</summary>
    public string Id { get; }

    /// <summary>所属 ScreenCategory.Name</summary>
    public string Category { get; }

    protected ScreenTemplate(string id, string category)
    {
        Id = id;
        Category = category;
    }

    /// <summary>创建默认配置</summary>
    public abstract ScreenConfig CreateDefaultConfig();

    /// <summary>
    /// 从配置创建页面控件。
    /// resolveWidget: 通过 WidgetInstance ID 解析对应的 Control。
    /// </summary>
    public abstract object CreateView(ScreenConfig config, Func<string, object> resolveWidget);
}
