using GalNet.Editor.Shared.UI;
using Avalonia.Controls;
using Avalonia.Media;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;
using GalNet.Control.ViewModels;
using GalNet.Control.Widget;
using GalNet.Core.Services;
using GalNet.Core.Settings;

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
    public void Palette_RaisesIndexerChange_WhenSharedProjectChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FileUiProjectProvider(root, UiProjectDefaults.Create());
            var palette = new ProjectColorPalette(provider);
            string? changed = null;
            palette.PropertyChanged += (_, args) => changed = args.PropertyName;

            provider.Current.Colors["accent"] = "#FF123456";
            provider.NotifyChanged();

            Assert.That(changed, Is.EqualTo("Item[]"));
            Assert.That(palette["accent"].ToString(), Does.Contain("123456"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void ExistingUiProject_MergesNewDefaultInstances()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var uiDirectory = Path.Combine(root, "UI");
            Directory.CreateDirectory(Path.Combine(uiDirectory, "ScreenInstance"));
            File.WriteAllText(Path.Combine(uiDirectory, "ui.json"), "{\"Version\":1,\"Colors\":{\"PrimaryColor\":\"#FF000000\"},\"DefaultViews\":{\"title\":\"title.custom\"}}");
            File.WriteAllText(Path.Combine(uiDirectory, "ScreenInstance", "title.custom.json"), "{\"Id\":\"title.custom\",\"TemplateId\":\"builtin.title\",\"Configuration\":{\"ShowGallery\":false}}");

            var provider = new FileUiProjectProvider(root);

            Assert.Multiple(() =>
            {
                Assert.That(provider.Current.DefaultViews["title"], Is.EqualTo("title.custom"));
                Assert.That(provider.Current.Screens, Does.ContainKey("title.default"));
                Assert.That(provider.Current.Screens, Does.ContainKey("title.custom"));
                Assert.That(provider.Current.Widgets, Does.ContainKey("button.title"));
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void WidgetHost_RebuildsWithFreshPresentation()
    {
        var factory = new CountingWidgetFactory();
        var host = new WidgetHostViewModel(factory, new WidgetBuildContext(null!, new TestPalette(), null!, null!), "button.title", "button");

        host.Rebuild();
        var first = host.View;
        host.Rebuild();

        Assert.That(factory.BuildCount, Is.EqualTo(3));
        Assert.That(host.View, Is.Not.SameAs(first));
        Assert.That(host.Widget, Is.EqualTo(3));
    }

    [Test]
    public void WidgetFactory_RejectsWrongCategory()
    {
        var registry = new TemplateRegistry([new TestWidgetTemplate()], []);
        var factory = new WidgetFactory(registry);
        var context = new WidgetBuildContext(null!, new TestPalette(), new TestWidgetProvider(), null!);
        Assert.That(() => factory.Build("button.test", context, "slider"), Throws.InvalidOperationException.With.Message.Contains("category 'slider'"));
    }

    [Test]
    public void TitleMenu_UsesFixedHostsAndHonoursShowGallery()
    {
        var options = new GameFlowOptions { GameContentProvider = null!, Widgets = null!, Screens = null!, Palette = new TestPalette() };
        var viewModel = new GameStartViewModel(null!, null, options);

        var factory = new ButtonWidgetFactory();
        var context = new WidgetBuildContext(null!, new TestPalette(), null!, null!);
        WidgetHostViewModel Host() => new(factory, context, "button.title", "button");
        viewModel.SetHosts(Host(), Host(), Host(), Host(), Host(), Host(), showGallery: false);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowGallery, Is.False);
            Assert.That(viewModel.ContinueHost, Is.Not.Null);
            Assert.That(viewModel.NewGameHost, Is.Not.Null);
            Assert.That(viewModel.LoadHost, Is.Not.Null);
            Assert.That(viewModel.GalleryHost, Is.Not.Null);
            Assert.That(viewModel.SettingsHost, Is.Not.Null);
            Assert.That(viewModel.QuitHost, Is.Not.Null);
        });
    }

    [Test]
    public void ToolkitWidgetVm_RaisesNotificationsAndRelayCommandSelectsChoice()
    {
        var slider = new SliderWidgetViewModel();
        var changes = new List<string?>();
        slider.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        slider.Value = 42;
        Assert.That(changes, Does.Contain(nameof(SliderWidgetViewModel.DisplayValue)));

        var choice = new ChoicePanelWidgetViewModel();
        choice.SetChoices(["A", "B"]);
        var selected = -1;
        choice.ChoiceSelected += value => selected = value;
        ((GalNet.Core.Widget.IChoicePanel)choice).SelectCommand.Execute("B");
        Assert.That(selected, Is.EqualTo(1));
    }

    [Test]
    public void Settings_UsesEightFixedHostsAndSynchronizesValues()
    {
        var factory = new SettingsWidgetFactory();
        var context = new WidgetBuildContext(null!, new TestPalette(), null!, null!);
        WidgetHostViewModel Host(string id, string category) => new(factory, context, id, category);
        var settings = new TestSettings { BgmVolume = .5f, Fullscreen = false };
        var vm = new SettingsViewModel(settings, null!);
        vm.SetHosts(
            Host("slider.bgm", "slider"), Host("slider.sfx", "slider"), Host("slider.voice", "slider"),
            Host("slider.text", "slider"), Host("slider.auto", "slider"), Host("slider.quick", "slider"),
            Host("toggle.fullscreen", "toggle"), Host("button.back", "button"));

        vm.BgmVolumeHost.RequireWidget<GalNet.Core.Widget.ISliderWidget>().Value = .75;
        vm.FullscreenHost.RequireWidget<GalNet.Core.Widget.IToggleWidget>().IsChecked = true;
        Assert.Multiple(() =>
        {
            Assert.That(settings.BgmVolume, Is.EqualTo(.75f));
            Assert.That(settings.Fullscreen, Is.True);
            Assert.That(new[] { vm.BgmVolumeHost, vm.SfxVolumeHost, vm.VoiceVolumeHost, vm.TextSpeedHost,
                vm.AutoDelayHost, vm.QuickDelayHost, vm.FullscreenHost, vm.BackButtonHost }.Distinct().Count(), Is.EqualTo(8));
        });
    }

    private sealed class CountingWidgetFactory : IWidgetFactory
    {
        public int BuildCount { get; private set; }
        public WidgetPresentation Build(string instanceId, WidgetBuildContext context, string? expectedCategory = null) =>
            new(new ContentControl(), ++BuildCount);
    }

    private sealed class ButtonWidgetFactory : IWidgetFactory
    {
        public WidgetPresentation Build(string instanceId, WidgetBuildContext context, string? expectedCategory = null) =>
            new(new ContentControl(), new ButtonWidgetViewModel());
    }

    private sealed class SettingsWidgetFactory : IWidgetFactory
    {
        public WidgetPresentation Build(string instanceId, WidgetBuildContext context, string? expectedCategory = null) =>
            new(new ContentControl(), expectedCategory switch
            {
                "slider" => new SliderWidgetViewModel(),
                "toggle" => new ToggleWidgetViewModel(),
                "button" => new ButtonWidgetViewModel(),
                _ => throw new InvalidOperationException()
            });
    }

    private sealed class TestWidgetProvider : IWidgetInstanceProvider
    {
        public bool TryGetWidget(string id, out GalNet.Core.UI.WidgetInstanceDefinition? instance)
        {
            instance = new() { Id = id, TemplateId = "test.button" };
            return true;
        }
    }

    private sealed class TestWidgetTemplate : IWidgetTemplate
    {
        public string Id => "test.button";
        public string Category => "button";
        public IReadOnlyCollection<string> ColorKeys => [];
        public void Validate(GalNet.Core.UI.WidgetInstanceDefinition instance, ICollection<GalNet.Core.UI.UiValidationError> errors) { }
        public WidgetPresentation Build(GalNet.Core.UI.WidgetInstanceDefinition instance, WidgetBuildContext context) =>
            new(new ContentControl(), new ButtonWidgetViewModel());
    }

    private sealed class TestSettings : ISettingsService
    {
        private GameSettings _settings = new();
        public float BgmVolume { get => _settings.BgmVolume; set { _settings.BgmVolume = value; Changed?.Invoke(); } }
        public float SfxVolume { get => _settings.SfxVolume; set => _settings.SfxVolume = value; }
        public float VoiceVolume { get => _settings.VoiceVolume; set => _settings.VoiceVolume = value; }
        public float TextSpeed { get => _settings.TextSpeed; set => _settings.TextSpeed = value; }
        public bool Fullscreen { get => _settings.Fullscreen; set => _settings.Fullscreen = value; }
        public GameSettings GetSnapshot() => new() { BgmVolume = BgmVolume, SfxVolume = SfxVolume, VoiceVolume = VoiceVolume, TextSpeed = TextSpeed, Fullscreen = Fullscreen, AutoAdvanceInterval = _settings.AutoAdvanceInterval, QuickAdvanceInterval = _settings.QuickAdvanceInterval };
        public void ApplySnapshot(GameSettings settings) => _settings = settings;
        public Task LoadAsync(string path) => Task.CompletedTask;
        public Task SaveAsync(string path) => Task.CompletedTask;
        public event Action? Changed;
    }

    private sealed class TestPalette : IColorPalette
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public IBrush this[string key] => Brushes.Transparent;
    }
}
