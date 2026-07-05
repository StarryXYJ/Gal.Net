using GalNet.Control.Widget;
using GalNet.Core.Widget;

namespace GalNet.Control.Tests;

public sealed class WidgetTemplateRegistryTests
{
    [Test]
    public void Register_Manual_CanRetrieve()
    {
        var registry = new WidgetTemplateRegistry();
        var template = new TestWidgetTemplate("test-1", "TestCategory");

        registry.Register(template);

        Assert.That(registry.Get("test-1"), Is.SameAs(template));
        Assert.That(registry.Contains("test-1"), Is.True);
    }

    [Test]
    public void Register_Generic_CreatesInstance()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register<TestWidgetTemplate>();

        Assert.That(registry.Contains("test-default"), Is.True);
        Assert.That(registry.Get("test-default"), Is.InstanceOf<TestWidgetTemplate>());
    }

    [Test]
    public void Get_UnknownId_ReturnsNull()
    {
        var registry = new WidgetTemplateRegistry();
        Assert.That(registry.Get("nonexistent"), Is.Null);
    }

    [Test]
    public void Contains_UnknownId_ReturnsFalse()
    {
        var registry = new WidgetTemplateRegistry();
        Assert.That(registry.Contains("nonexistent"), Is.False);
    }

    [Test]
    public void Register_DuplicateId_Overwrites()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register(new TestWidgetTemplate("dup", "Cat"));
        registry.Register(new TestWidgetTemplate("dup", "Cat"));

        Assert.That(registry.Contains("dup"), Is.True);
    }

    [Test]
    public void GetAllByCategory_ReturnsMatching()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register(new TestWidgetTemplate("a1", "CatA"));
        registry.Register(new TestWidgetTemplate("b1", "CatB"));
        registry.Register(new TestWidgetTemplate("a2", "CatA"));

        var catA = registry.GetAllByCategory("CatA");
        Assert.That(catA, Has.Count.EqualTo(2));
        Assert.That(catA.Select(t => t.Id), Is.EquivalentTo(new[] { "a1", "a2" }));
    }

    [Test]
    public void IdComparison_IsCaseInsensitive()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register(new TestWidgetTemplate("MyWidget", "Cat"));
        Assert.That(registry.Get("mywidget"), Is.Not.Null);
    }

    [Test]
    public void TemplateIds_ReturnsAll()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register(new TestWidgetTemplate("x", "Cat"));
        registry.Register(new TestWidgetTemplate("y", "Cat"));
        Assert.That(registry.TemplateIds, Is.EquivalentTo(new[] { "x", "y" }));
    }

    [Test]
    public void All_ReturnsAll()
    {
        var registry = new WidgetTemplateRegistry();
        registry.Register(new TestWidgetTemplate("x", "Cat"));
        registry.Register(new TestWidgetTemplate("y", "Cat"));
        Assert.That(registry.All.ToList(), Has.Count.EqualTo(2));
    }

    [Test]
    public void ScanAssembly_FindsSubclasses()
    {
        var registry = new WidgetTemplateRegistry();
        registry.ScanAssembly(typeof(WidgetTemplateRegistryTests).Assembly);

        // TestWidgetTemplate 在同一程序集中，应被扫描到
        Assert.That(registry.Contains("test-default"), Is.True);
    }

    [Test]
    public void Register_Null_Throws()
    {
        var registry = new WidgetTemplateRegistry();
        Assert.That(() => registry.Register(null!), Throws.ArgumentNullException);
    }
}

// ── 测试用 WidgetTemplate ────────────────────────────────────

public sealed class TestWidgetTemplate : WidgetTemplate
{
    public TestWidgetTemplate() : base("test-default", "TestCategory") { }
    public TestWidgetTemplate(string id, string category) : base(id, category) { }

    public override WidgetConfig CreateDefaultConfig() => new TestWidgetConfig();
    public override object CreateView(WidgetConfig config) => new TestControl();
}

public sealed class TestWidgetConfig : WidgetConfig
{
    public string Message { get; set; } = "hello";
}

public sealed class TestControl
{
    public string Message { get; set; } = "";
}
