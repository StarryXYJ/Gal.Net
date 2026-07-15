using GalNet.Control.View;
using GalNet.Control.Views;
using GalNet.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using GalNet.Control.Services;
using GalNet.Control.UI;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.UI;
using Ursa.Controls;
using Avalonia.Controls;
using Avalonia.Media;
using GalNet.Core.Assets;

namespace GalNet.Control.ViewModels;

public sealed class GameFlowFactory : IGameFlowFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUiPresetRegistry _presets;
    private readonly Dictionary<IGameScreenNavigator, GameRunViewModel> _runs = [];

    public GameFlowFactory(IServiceProvider serviceProvider, IUiPresetRegistry presets)
    {
        _serviceProvider = serviceProvider;
        _presets = presets;
    }

    public GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions options) => new(this, options);

    public object BuildScreen(IGameScreenNavigator navigator, string screen, object? parameter, GameFlowOptions options)
    {
        return screen.ToLowerInvariant() switch
        {
            "title" => CreateTitle(navigator, options),
            "game" => CreateRun(navigator, options, options.Ui.Game, parameter),
            "settings" => CreateSettings(navigator, options.Ui.Settings),
            "save-load" => CreateSaveLoad(navigator, options, parameter as string == "save" ? SaveLoadMode.Save : SaveLoadMode.Load, options.Ui.SaveLoad),
            "gallery" => CreateGallery(navigator, options, options.Ui.Gallery),
            _ => throw new InvalidOperationException($"Unknown built-in screen '{screen}'.")
        };
    }

    private object CreateTitle(IGameScreenNavigator navigator, GameFlowOptions options)
    {
        var selection = options.Ui.GetPage(UiPageKind.Title);
        var preset = _presets.GetRequired(selection.PresetId);
        var config = CreateTitleConfiguration(options.Ui.Title, preset, selection.Settings);
        return string.Equals(preset.Metadata.Id, "builtin.title.text-menu", StringComparison.OrdinalIgnoreCase)
            ? new TextMenuTitleViewModel(navigator, _serviceProvider.GetService<IGameExitService>(), options,
                options.SaveService ?? _serviceProvider.GetService<ISaveService>(), config, selection.Settings, options.AssetManager)
            : CreateStart(navigator, options, config, selection.Settings.GetValueOrDefault("horizontalAlignment", "center"), options.AssetManager);
    }

    private static TitleUiConfiguration CreateTitleConfiguration(TitleUiConfiguration defaults, IUiPagePreset preset, IReadOnlyDictionary<string, string> settings)
    {
        var values = preset.CreateDefaultSettings().ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings) values[key] = value;
        var config = new TitleUiConfiguration
        {
            BackgroundColor = defaults.BackgroundColor, BackgroundImage = defaults.BackgroundImage, BackgroundStretch = defaults.BackgroundStretch, ContentPadding = defaults.ContentPadding, TitleColor = defaults.TitleColor,
            TitleFontSize = defaults.TitleFontSize, ButtonColor = defaults.ButtonColor, ButtonTextColor = defaults.ButtonTextColor,
            ButtonHoverColor = defaults.ButtonHoverColor, ButtonWidth = defaults.ButtonWidth, ButtonHeight = defaults.ButtonHeight,
            MenuSpacing = defaults.MenuSpacing, ShowGallery = defaults.ShowGallery, MenuTextColor = defaults.MenuTextColor,
            MenuFontSize = defaults.MenuFontSize, TitleMenuGap = defaults.TitleMenuGap, MenuHoverTextColor = defaults.MenuHoverTextColor
        };
        if (TryNumber(values, "titleFontSize", out var titleSize)) config.TitleFontSize = titleSize;
        if (TryNumber(values, "contentPadding", out var contentPadding)) config.ContentPadding = contentPadding;
        if (TryNumber(values, "menuItemWidth", out var buttonWidth)) config.ButtonWidth = buttonWidth;
        if (TryNumber(values, "menuItemHeight", out var buttonHeight)) config.ButtonHeight = buttonHeight;
        if (TryNumber(values, "menuSpacing", out var spacing)) config.MenuSpacing = spacing;
        if (TryNumber(values, "titleMenuGap", out var gap)) config.TitleMenuGap = gap;
        if (TryNumber(values, "menuFontSize", out var menuFontSize)) config.MenuFontSize = menuFontSize;
        if (values.TryGetValue("showGallery", out var gallery) && bool.TryParse(gallery, out var showGallery)) config.ShowGallery = showGallery;
        if (TryColor(values, "titleColor", out var titleColor)) config.TitleColor = titleColor;
        if (TryColor(values, "backgroundColor", out var backgroundColor)) config.BackgroundColor = backgroundColor;
        if (values.TryGetValue("backgroundImage", out var backgroundImage)) config.BackgroundImage = string.IsNullOrWhiteSpace(backgroundImage) ? null : backgroundImage;
        if (values.TryGetValue("backgroundStretch", out var backgroundStretch)) config.BackgroundStretch = backgroundStretch;
        if (TryColor(values, "menuTextColor", out var menuTextColor)) config.MenuTextColor = menuTextColor;
        if (TryColor(values, "menuHoverTextColor", out var menuHoverTextColor)) config.MenuHoverTextColor = menuHoverTextColor;
        if (TryColor(values, "menuItemBackgroundColor", out var buttonColor)) config.ButtonColor = buttonColor;
        config.ButtonTextColor = config.MenuTextColor;
        if (TryColor(values, "menuItemHoverBackgroundColor", out var hoverColor)) config.ButtonHoverColor = hoverColor;
        return config;
    }

    private static bool TryNumber(IReadOnlyDictionary<string, string> values, string key, out double result)
    {
        result = default;
        return values.TryGetValue(key, out var value) && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out result);
    }
    private static bool TryColor(IReadOnlyDictionary<string, string> values, string key, out Color result)
    {
        result = default;
        return values.TryGetValue(key, out var value) && Color.TryParse(value, out result);
    }

    public GameStartViewModel CreateStart(IGameScreenNavigator navigator, GameFlowOptions options, TitleUiConfiguration config, string horizontalAlignment = "center", IAssetManager? assets = null) =>
        new(navigator, _serviceProvider.GetService<IGameExitService>(), options,
            options.SaveService ?? _serviceProvider.GetService<ISaveService>(), config, horizontalAlignment, assets);

    public GameRunViewModel CreateRun(IGameScreenNavigator navigator, GameFlowOptions options, GameUiConfiguration config, object? parameter)
    {
        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        var screen = new GameScreenViewModel(config);
        var gameView = new DefaultGameView(settings.GetSnapshot(), config, screen);
        var variableService = options.VariableService ?? _serviceProvider.GetService<IVariableService>();
        var save = options.SaveService ?? _serviceProvider.GetService<ISaveService>();
        var progress = options.ProgressService ?? _serviceProvider.GetService<IGameProgressService>();
        var runOptions = parameter is GalNet.Core.Runtime.GameSnapshot snapshot ? options with { RestoreSnapshot = snapshot } :
            parameter is string startNode ? options with { StartNodeId = startNode, IsGalleryPresentation = true } : options;
        var run = new GameRunViewModel(gameView, settings, variableService, options.GameContentProvider, save, progress, runOptions, () =>
        {
            _ = navigator.NavigateAsync("title");
        });
        _runs[navigator] = run;
        run.CommandRequested += command => HandleRunCommand(command, run, navigator, options, save);
        options.RunCreated?.Invoke(run);
        return run;
    }

    public SettingsViewModel CreateSettings(IGameScreenNavigator navigator, SettingsUiConfiguration config) =>
        new(_serviceProvider.GetRequiredService<ISettingsService>(), navigator, config);

    public SaveLoadViewModel CreateSaveLoad(IGameScreenNavigator navigator, GameFlowOptions options, SaveLoadMode mode, SaveLoadUiConfiguration config)
    {
        var saves = options.SaveService ?? _serviceProvider.GetService<ISaveService>();
        _runs.TryGetValue(navigator, out var run);
        return new SaveLoadViewModel(
            navigator,
            saves,
            mode,
            config,
            loadAction: async slot =>
            {
                if (saves is null) return;
                var snapshot = await saves.LoadAsync(slot);
                if (snapshot is null) return;
                if (run is not null) await run.DisposeAsync();
                await navigator.NavigateAsync("game", snapshot);
            },
            saveAction: run is null ? null : async slot => await run.SaveCurrentAsync(slot));
    }
    public GalleryViewModel CreateGallery(IGameScreenNavigator navigator, GameFlowOptions options, GalleryUiConfiguration config) =>
        new(navigator, options, options.ProgressService ?? _serviceProvider.GetService<IGameProgressService>(), config);

    private void HandleRunCommand(string command, GameRunViewModel run, IGameScreenNavigator navigator, GameFlowOptions options, ISaveService? saves)
    {
        switch (command)
        {
            case "settings": _ = navigator.NavigateAsync("settings"); break;
            case "load": _ = navigator.NavigateAsync("save-load"); break;
            case "save": _ = navigator.NavigateAsync("save-load", "save"); break;
            case "menu":
                _ = run.DisposeAsync();
                _runs.Remove(navigator);
                _ = navigator.NavigateAsync("title");
                break;
            case "screenshot": _ = ShowScreenshotAsync(run); break;
        }
    }

    private static async Task ShowScreenshotAsync(GameRunViewModel run)
    {
        var vm = new ScreenshotDialogViewModel(async (directory, includeUi) =>
        {
            if (!Directory.Exists(directory)) return "The directory does not exist.";
            try
            {
                var path = Path.Combine(directory, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                await File.WriteAllBytesAsync(path, await run.GameView.CapturePngAsync(includeUi));
                return null;
            }
            catch (Exception ex) { return $"Could not save screenshot: {ex.Message}"; }
        });
        await OverlayDialog.ShowCustomAsync<ScreenshotDialog, ScreenshotDialogViewModel, bool>(vm);
    }
}
