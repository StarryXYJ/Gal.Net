using GalNet.Control.Abstraction.UI;
using GalNet.Control.ViewModels;
using GalNet.Control.Views;
using GalNet.Core.UI;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls.Documents;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.UI;

/// <summary>Small base class shared by built-in template factories.</summary>
internal static class BuiltInScreen
{
    public static GameFlowOptions Options(ScreenBuildContext context) =>
        context.Session as GameFlowOptions ?? throw new InvalidOperationException("Game flow options are required to build a screen.");
    public static TView Present<TView>(IServiceProvider services, object viewModel, IColorPalette palette)
        where TView : AvaloniaControl
    {
        var view = ActivatorUtilities.CreateInstance<TView>(services);
        view.DataContext = viewModel;
        PaletteScope.SetPalette(view, palette);
        // Foreground is inherited by text controls: this is the screen-wide
        // default, while individual templates only bind when they need a role.
        view.Bind(TextElement.ForegroundProperty, PaletteBinding.Create(view, "FontColor0"));
        return view;
    }
}

public sealed class TitleScreenTemplate(IGameFlowFactory flow) : ScreenTemplate<TitleScreenConfiguration>("builtin.title", "title")
{
    protected override ScreenPresentation Build(ScreenInstanceDefinition instance, TitleScreenConfiguration config, ScreenBuildContext context)
    {
        var vm = flow.CreateStart(context.Navigator, BuiltInScreen.Options(context));
        var widgetId = config.Widgets.TryGetValue("MenuButton", out var configured) ? configured : "button.title";
        var factory = context.Services.GetService(typeof(IWidgetFactory)) as IWidgetFactory
            ?? throw new InvalidOperationException("IWidgetFactory is required to build title menu widgets.");
        var widgetContext = new WidgetBuildContext(context.Services, context.Palette, context.Widgets, context.Navigator);
        WidgetHostViewModel Host() => new(factory, widgetContext, widgetId, "button");
        vm.SetHosts(Host(), Host(), Host(), Host(), Host(), Host(), config.ShowGallery);
        return new(BuiltInScreen.Present<GameStartView>(context.Services, vm, context.Palette), vm, Category);
    }
}

/// <summary>Persisted title-screen options. Widget ids are reserved for the configurable menu renderer.</summary>
public sealed class TitleScreenConfiguration : PresentationConfig
{
    public bool ShowGallery { get; set; } = true;
    public Dictionary<string, string> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GameScreenTemplate(IGameFlowFactory flow) : ScreenTemplate<GameScreenConfiguration>("builtin.game", "game")
{
    protected override ScreenPresentation Build(ScreenInstanceDefinition instance, GameScreenConfiguration config, ScreenBuildContext context)
    {
        var options = BuiltInScreen.Options(context);
        if (context.Parameter is GalNet.Core.Runtime.GameSnapshot snapshot) options = options with { RestoreSnapshot = snapshot };
        if (context.Parameter is string startNode) options = options with { StartNodeId = startNode, IsGalleryPresentation = true };
        var widgetContext = new WidgetBuildContext(context.Services, context.Palette, context.Widgets, context.Navigator);
        var vm = flow.CreateRun(context.Navigator, options, widgetContext, config);
        return new(BuiltInScreen.Present<GameRunView>(context.Services, vm, context.Palette), vm, Category);
    }
}

public sealed class GameScreenConfiguration : PresentationConfig
{
    public Dictionary<string, string> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SettingsScreenTemplate(IGameFlowFactory flow) : ScreenTemplate<SettingsScreenConfiguration>("builtin.settings", "settings")
{
    protected override ScreenPresentation Build(ScreenInstanceDefinition instance, SettingsScreenConfiguration config, ScreenBuildContext context)
    {
        var vm = flow.CreateSettings(context.Navigator);
        var factory = context.Services.GetRequiredService<IWidgetFactory>();
        var widgetContext = new WidgetBuildContext(context.Services, context.Palette, context.Widgets, context.Navigator);
        WidgetHostViewModel Host(string role, string fallback, string category) => new(factory, widgetContext,
            config.Widgets.TryGetValue(role, out var id) ? id : fallback, category);
        vm.SetHosts(
            Host("BgmVolume", "slider.default", "slider"), Host("SfxVolume", "slider.default", "slider"),
            Host("VoiceVolume", "slider.default", "slider"), Host("TextSpeed", "slider.default", "slider"),
            Host("AutoDelay", "slider.default", "slider"), Host("QuickDelay", "slider.default", "slider"),
            Host("Fullscreen", "toggle.default", "toggle"), Host("BackButton", "button.default", "button"));
        return new(BuiltInScreen.Present<SettingsView>(context.Services, vm, context.Palette), vm, Category);
    }
}

public sealed class SettingsScreenConfiguration : PresentationConfig
{
    public Dictionary<string, string> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SaveLoadScreenTemplate(IGameFlowFactory flow) : ScreenTemplate<SaveLoadScreenConfiguration>("builtin.save-load", "save-load")
{
    protected override ScreenPresentation Build(ScreenInstanceDefinition instance, SaveLoadScreenConfiguration config, ScreenBuildContext context)
    {
        var options = BuiltInScreen.Options(context);
        var mode = string.Equals(context.Parameter as string, "save", StringComparison.OrdinalIgnoreCase) ? SaveLoadMode.Save : SaveLoadMode.Load;
        var vm = flow.CreateSaveLoad(context.Navigator, options, mode);
        var factory = context.Services.GetRequiredService<IWidgetFactory>();
        var widgetContext = new WidgetBuildContext(context.Services, context.Palette, context.Widgets, context.Navigator);
        WidgetHostViewModel Host(string role, string fallback, string category) => new(factory, widgetContext, config.Widgets.TryGetValue(role, out var id) ? id : fallback, category);
        vm.SetHosts(() => Host("Slot", "save-slot.default", "save-slot"), Host("PageButton", "button.default", "button"), Host("PageButton", "button.default", "button"), Host("BackButton", "button.default", "button"));
        _ = vm.RefreshAsync();
        return new(BuiltInScreen.Present<SaveLoadView>(context.Services, vm, context.Palette), vm, Category);
    }
}

public sealed class SaveLoadScreenConfiguration : PresentationConfig
{
    public Dictionary<string, string> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GalleryScreenTemplate(IGameFlowFactory flow) : ScreenTemplate<GalleryScreenConfiguration>("builtin.gallery", "gallery")
{
    protected override ScreenPresentation Build(ScreenInstanceDefinition instance, GalleryScreenConfiguration config, ScreenBuildContext context)
    {
        var vm = flow.CreateGallery(context.Navigator, BuiltInScreen.Options(context));
        var factory = context.Services.GetRequiredService<IWidgetFactory>();
        var widgetContext = new WidgetBuildContext(context.Services, context.Palette, context.Widgets, context.Navigator);
        WidgetHostViewModel Host(string role, string fallback) => new(factory, widgetContext, config.Widgets.TryGetValue(role, out var id) ? id : fallback, "button");
        vm.SetHosts(() => Host("CategoryButton", "button.default"), () => Host("ItemButton", "button.gallery-item"), Host("BackButton", "button.default"));
        return new(BuiltInScreen.Present<GalleryView>(context.Services, vm, context.Palette), vm, Category);
    }
}

public sealed class GalleryScreenConfiguration : PresentationConfig
{
    public Dictionary<string, string> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
