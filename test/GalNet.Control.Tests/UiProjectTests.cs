using GalNet.Control.UI;

namespace GalNet.Control.Tests;

public sealed class UiProjectTests
{
    [Test]
    public async Task SaveAndLoad_PreservesInstancesAndPalette()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FileUiProjectProvider(root, UiProjectDefaults.Create());
            provider.Current.Widgets["button.new"] = new() { Id = "button.new", TemplateId = "builtin.button", ColorOverrides = { ["background"] = "accent" } };
            provider.Current.Screens["title.alt"] = new() { Id = "title.alt", TemplateId = "builtin.title" };
            provider.Current.DefaultViews["title"] = "title.alt";
            await provider.SaveAsync();

            var loaded = new FileUiProjectProvider(root);
            Assert.That(loaded.Current.Widgets["button.new"].ColorOverrides["background"], Is.EqualTo("accent"));
            Assert.That(loaded.Current.Screens["title.alt"].TemplateId, Is.EqualTo("builtin.title"));
            Assert.That(Directory.Exists(Path.Combine(root, "UI", "WidgetInstance")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(root, "UI", "ScreenInstance")), Is.True);
            Assert.That(loaded.Validate(), Is.Empty);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void Validate_ReportsMissingScreenReference()
    {
        var project = UiProjectDefaults.Create();
        project.DefaultViews["title"] = "missing";
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FileUiProjectProvider(root, project);
            Assert.That(provider.Validate().Single().Message, Does.Contain("does not exist"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void Palette_NotifiesOpenConsumers_WhenSharedProjectChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FileUiProjectProvider(root, UiProjectDefaults.Create());
            var palette = new UiColorPalette(provider);
            string? changed = null;
            palette.ColorChanged += key => changed = key;

            provider.Current.Colors["accent"] = "#FF123456";
            provider.NotifyChanged();

            Assert.That(changed, Is.EqualTo("*"));
            Assert.That(palette.Resolve("accent"), Is.EqualTo("#FF123456"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
