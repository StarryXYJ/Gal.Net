using GalNet.Control.Widget;
using GalNet.Core.Widget;

namespace GalNet.Control.Tests;

public sealed class WidgetFactoryTests
{
    [Test]
    public void CreateInstance_FromTemplateId_ReturnsInstance()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var instance = factory.CreateInstance("test-default");

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.TemplateId, Is.EqualTo("test-default"));
        Assert.That(instance.Config, Is.InstanceOf<TestWidgetConfig>());
    }

    [Test]
    public void CreateInstance_WithConfiguration_AppliesConfig()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var instance = factory.CreateInstance("test-default", cfg =>
        {
            ((TestWidgetConfig)cfg).Message = "configured";
        });

        Assert.That(((TestWidgetConfig)instance.Config!).Message, Is.EqualTo("configured"));
    }

    [Test]
    public void CreateInstance_UnknownTemplate_Throws()
    {
        var registry = new WidgetTemplateRegistry();
        var factory = new WidgetFactory(registry);
        Assert.That(() => factory.CreateInstance("nonexistent"), Throws.Exception);
    }

    [Test]
    public void CreateView_ReturnsViewFromTemplate()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var instance = factory.CreateInstance("test-default");
        var view = factory.CreateView(instance);

        Assert.That(view, Is.InstanceOf<TestControl>());
    }

    [Test]
    public void CreateView_AppliesBinding()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var instance = factory.CreateInstance("test-default", cfg =>
        {
            ((TestWidgetConfig)cfg).Message = "bound-value";
        });
        var view = factory.CreateView(instance);

        var control = (TestControl)view;
        Assert.That(control.Message, Is.EqualTo("bound-value"));
    }

    [Test]
    public void GetOrCreateView_CachesView()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var instance = factory.CreateInstance("test-default");
        var view1 = factory.GetOrCreateView(instance);
        var view2 = factory.GetOrCreateView(instance);

        Assert.That(view2, Is.SameAs(view1));
    }

    [Test]
    public void ReleaseView_RemovesCache()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var instance = factory.CreateInstance("test-default");
        factory.GetOrCreateView(instance);
        Assert.That(factory.CachedCount, Is.EqualTo(1));

        factory.ReleaseView(instance.Id);
        Assert.That(factory.CachedCount, Is.EqualTo(0));

        // 释放后再次获取应该重新创建
        var view = factory.GetOrCreateView(instance);
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void ClearCache_EmptiesAll()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var inst1 = factory.CreateInstance("test-default");
        var inst2 = factory.CreateInstance("test-default");
        factory.GetOrCreateView(inst1);
        factory.GetOrCreateView(inst2);

        Assert.That(factory.CachedCount, Is.EqualTo(2));
        factory.ClearCache();
        Assert.That(factory.CachedCount, Is.EqualTo(0));
    }

    [Test]
    public void CreateInstance_UniqueIds()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();
        var factory = new WidgetFactory(registry);

        var inst1 = factory.CreateInstance("test-default");
        var inst2 = factory.CreateInstance("test-default");

        Assert.That(inst1.Id, Is.Not.EqualTo(inst2.Id));
    }
}
