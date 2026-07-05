using GalNet.Control.Widget;
using GalNet.Core.Widget;

namespace GalNet.Control.Tests;

internal sealed class BindingHelperTests
{
    [Test]
    public void ApplyConfig_MapsMatchingProperties()
    {
        var config = new TestWidgetConfig { Message = "hello world" };
        var control = new TestControl();

        BindingHelper.ApplyConfig(control, config);

        Assert.That(control.Message, Is.EqualTo("hello world"));
    }

    [Test]
    public void ApplyConfig_SkipsMissingControlProperty()
    {
        var config = new ConfigWithExtraProps { Name = "test", Ignored = "extra" };
        var control = new SimpleControl();

        // 不应抛出异常，ExtraProp 不存在于 SimpleControl 上应静默跳过
        BindingHelper.ApplyConfig(control, config);

        Assert.That(control.Name, Is.EqualTo("test"));
    }

    [Test]
    public void ApplyConfig_SkipsTypeMismatch()
    {
        var config = new ConfigWithExtraProps { Name = "test", Count = 42 };
        var control = new SimpleControl(); // Count 在 control 上是 string 类型

        BindingHelper.ApplyConfig(control, config);

        Assert.That(control.Name, Is.EqualTo("test"));
        Assert.That(control.Count, Is.Null); // 类型不匹配，跳过
    }

    [Test]
    public void ApplyConfig_NullControl_Throws()
    {
        Assert.That(() => BindingHelper.ApplyConfig(null!, new TestWidgetConfig()), Throws.ArgumentNullException);
    }

    [Test]
    public void ApplyConfig_NullConfig_Throws()
    {
        Assert.That(() => BindingHelper.ApplyConfig(new object(), null!), Throws.ArgumentNullException);
    }
}

// ── 测试辅助类型 ──────────────────────────────────────────────

file sealed class ConfigWithExtraProps : WidgetConfig
{
    public string Name { get; set; } = "";
    public string Ignored { get; set; } = "";
    public int Count { get; set; }
}

file sealed class SimpleControl
{
    public string Name { get; set; } = "";
    public string? Count { get; set; }
}
