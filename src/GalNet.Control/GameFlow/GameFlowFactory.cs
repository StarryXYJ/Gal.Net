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
            "game" => CreateRun(navigator, options, CreateGameConfiguration(GetPageSettings(options.Ui, UiPageKind.Game)), parameter),
            "settings" => CreateSettings(navigator, CreateSettingsConfiguration(GetPageSettings(options.Ui, UiPageKind.Settings))),
            "save-load" => CreateSaveLoad(navigator, options, parameter as string == "save" ? SaveLoadMode.Save : SaveLoadMode.Load, CreateSaveLoadConfiguration(GetPageSettings(options.Ui, UiPageKind.SaveLoad))),
            "gallery" => CreateGallery(navigator, options, CreateGalleryConfiguration(GetPageSettings(options.Ui, UiPageKind.Gallery))),
            "about" => CreateAbout(navigator, options, CreateAboutConfiguration(GetPageSettings(options.Ui, UiPageKind.About))),
            _ => throw new InvalidOperationException($"Unknown built-in screen '{screen}'.")
        };
    }

    private object CreateTitle(IGameScreenNavigator navigator, GameFlowOptions options)
    {
        var selection = options.Ui.GetPage(UiPageKind.Title);
        var preset = _presets.GetRequired(selection.PresetId);
        var config = CreateTitleConfiguration(preset, selection.Settings);
        return string.Equals(preset.Metadata.Id, "builtin.title.text-menu", StringComparison.OrdinalIgnoreCase)
            ? new TextMenuTitleViewModel(navigator, _serviceProvider.GetService<IGameExitService>(), options,
                options.SaveService ?? _serviceProvider.GetService<ISaveService>(), config, selection.Settings, options.AssetManager)
            : CreateStart(navigator, options, config, selection.Settings.GetValueOrDefault("horizontalAlignment", "center"), options.AssetManager);
    }

    private static TitleUiConfiguration CreateTitleConfiguration(IUiPagePreset preset, IReadOnlyDictionary<string, string> settings)
    {
        var values = preset.CreateDefaultSettings().ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings) values[key] = value;
        var config = new TitleUiConfiguration();
        if (TryNumber(values, "titleFontSize", out var titleSize)) config.TitleFontSize = titleSize;
        if (TryNumber(values, "contentPadding", out var contentPadding)) config.ContentPadding = contentPadding;
        if (TryNumber(values, "menuItemWidth", out var buttonWidth)) config.ButtonWidth = buttonWidth;
        if (TryNumber(values, "menuItemHeight", out var buttonHeight)) config.ButtonHeight = buttonHeight;
        if (TryNumber(values, "menuSpacing", out var spacing)) config.MenuSpacing = spacing;
        if (TryNumber(values, "titleMenuGap", out var gap)) config.TitleMenuGap = gap;
        if (TryNumber(values, "menuFontSize", out var menuFontSize)) config.MenuFontSize = menuFontSize;
        if (values.TryGetValue("showGallery", out var gallery) && bool.TryParse(gallery, out var showGallery)) config.ShowGallery = showGallery;
        if (values.TryGetValue("showAbout", out var about) && bool.TryParse(about, out var showAbout)) config.ShowAbout = showAbout;
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

    private IReadOnlyDictionary<string, string> GetPageSettings(UiProject ui, UiPageKind page)
    {
        var selection = ui.GetPage(page);
        var values = _presets.GetRequired(selection.PresetId).CreateDefaultSettings()
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in selection.Settings) values[key] = value;
        return values;
    }

    private static GameUiConfiguration CreateGameConfiguration(IReadOnlyDictionary<string, string> values)
    {
        var config = new GameUiConfiguration();
        ApplyGameSettings(config, values);
        return config;
    }

    private static void ApplyGameSettings(GameUiConfiguration config, IReadOnlyDictionary<string, string> values)
    {
        if (TryColor(values, "dialogueBackgroundColor", out var dialogueBackground)) config.DialogueBackgroundColor = dialogueBackground;
        if (values.TryGetValue("dialogueBackgroundImage", out var dialogueBackgroundImage)) config.DialogueBackgroundImage = string.IsNullOrWhiteSpace(dialogueBackgroundImage) ? null : dialogueBackgroundImage;
        if (TryNumber(values, "dialogueBackgroundImageOpacity", out var imageOpacity)) config.DialogueBackgroundImageOpacity = imageOpacity;
        if (TryColor(values, "dialogueTextColor", out var dialogueText)) config.DialogueTextColor = dialogueText;
        if (TryColor(values, "speakerTextColor", out var speakerText)) config.SpeakerTextColor = speakerText;
        if (TryNumber(values, "dialogueHeight", out var dialogueHeight)) config.DialogueHeight = dialogueHeight;
        if (TryNumber(values, "dialogueMargin", out var dialogueMargin)) config.DialogueMargin = dialogueMargin;
        if (TryNumber(values, "dialogueCornerRadius", out var dialogueCornerRadius)) config.DialogueCornerRadius = dialogueCornerRadius;
        if (TryNumber(values, "dialogueFontSize", out var dialogueFontSize)) config.DialogueFontSize = dialogueFontSize;
        if (values.TryGetValue("choiceLayout", out var choiceLayout)) config.ChoiceLayout = choiceLayout;
        if (TryColor(values, "choiceButtonColor", out var choiceButtonColor)) config.ChoiceButtonColor = choiceButtonColor;
        if (TryColor(values, "choiceButtonTextColor", out var choiceButtonText)) config.ChoiceButtonTextColor = choiceButtonText;
        if (TryNumber(values, "choiceButtonWidth", out var choiceButtonWidth)) config.ChoiceButtonWidth = choiceButtonWidth;
        if (TryNumber(values, "choiceButtonHeight", out var choiceButtonHeight)) config.ChoiceButtonHeight = choiceButtonHeight;
        if (TryNumber(values, "choiceSpacing", out var choiceSpacing)) config.ChoiceSpacing = choiceSpacing;
        if (values.TryGetValue("commandBarVisible", out var commandBarVisible) && bool.TryParse(commandBarVisible, out var visible)) config.CommandBarVisible = visible;
        if (TryColor(values, "commandTextColor", out var commandText)) config.CommandTextColor = commandText;
        if (TryColor(values, "commandHoverTextColor", out var commandHoverText)) config.CommandHoverTextColor = commandHoverText;
        if (TryColor(values, "commandSelectedTextColor", out var commandSelectedText)) config.CommandSelectedTextColor = commandSelectedText;
    }

    private static SettingsUiConfiguration CreateSettingsConfiguration(IReadOnlyDictionary<string, string> values) =>
        ApplySettingsScreenSettings(ApplyStandardScreenSettings(new SettingsUiConfiguration(), values), values);

    private static SaveLoadUiConfiguration CreateSaveLoadConfiguration(IReadOnlyDictionary<string, string> values) =>
        ApplyStandardScreenSettings(new SaveLoadUiConfiguration(), values);

    private static GalleryUiConfiguration CreateGalleryConfiguration(IReadOnlyDictionary<string, string> values) =>
        ApplyStandardScreenSettings(new GalleryUiConfiguration(), values);

    private static AboutUiConfiguration CreateAboutConfiguration(IReadOnlyDictionary<string, string> values)
    {
        var config = new AboutUiConfiguration();
        if (values.TryGetValue("contentAsset", out var contentAsset)) config.ContentAsset = string.IsNullOrWhiteSpace(contentAsset) ? null : contentAsset;
        if (TryNumber(values, "contentPadding", out var padding)) config.ContentPadding = padding;
        if (TryNumber(values, "fontSize", out var fontSize)) config.FontSize = fontSize;
        if (TryNumber(values, "codeFontSize", out var codeFontSize)) config.CodeFontSize = codeFontSize;
        ApplyColor(values, "backgroundColor", value => config.BackgroundColor = value);
        ApplyColor(values, "panelColor", value => config.PanelColor = value);
        ApplyColor(values, "textColor", value => config.TextColor = value);
        ApplyColor(values, "headingColor", value => config.HeadingColor = value);
        ApplyColor(values, "selectionColor", value => config.SelectionColor = value);
        ApplyColor(values, "linkColor", value => config.LinkColor = value);
        ApplyColor(values, "linkHoverColor", value => config.LinkHoverColor = value);
        ApplyColor(values, "linkVisitedColor", value => config.LinkVisitedColor = value);
        ApplyColor(values, "blockquoteBackgroundColor", value => config.BlockquoteBackgroundColor = value);
        ApplyColor(values, "blockquoteBorderColor", value => config.BlockquoteBorderColor = value);
        ApplyColor(values, "codeBackgroundColor", value => config.CodeBackgroundColor = value);
        ApplyColor(values, "codeBorderColor", value => config.CodeBorderColor = value);
        ApplyColor(values, "codeTextColor", value => config.CodeTextColor = value);
        ApplyColor(values, "ruleColor", value => config.RuleColor = value);
        ApplyColor(values, "backButtonForegroundColor", value => config.BackButtonForegroundColor = value);
        return config;
    }

    private static void ApplyColor(IReadOnlyDictionary<string, string> values, string key, Action<Color> apply)
    {
        if (TryColor(values, key, out var color)) apply(color);
    }

    private static T ApplyStandardScreenSettings<T>(T config, IReadOnlyDictionary<string, string> values) where T : SettingsUiConfiguration
    {
        if (TryColor(values, "backgroundColor", out var background)) config.BackgroundColor = background;
        if (TryColor(values, "panelColor", out var panel)) config.PanelColor = panel;
        if (TryColor(values, "textColor", out var text)) config.TextColor = text;
        if (TryColor(values, "buttonColor", out var button)) config.ButtonColor = button;
        if (TryColor(values, "buttonTextColor", out var buttonText)) config.ButtonTextColor = buttonText;
        if (TryColor(values, "backButtonForegroundColor", out var backButtonForeground)) config.BackButtonForegroundColor = backButtonForeground;
        return config;
    }

    private static SettingsUiConfiguration ApplySettingsScreenSettings(SettingsUiConfiguration config, IReadOnlyDictionary<string, string> values)
    {
        if (TryColor(values, "sliderTrackColor", out var sliderTrack)) config.SliderTrackColor = sliderTrack;
        if (TryColor(values, "sliderFillColor", out var sliderFill)) config.SliderFillColor = sliderFill;
        if (TryColor(values, "sliderThumbColor", out var sliderThumb)) config.SliderThumbColor = sliderThumb;
        if (TryColor(values, "sliderThumbBorderColor", out var sliderThumbBorder)) config.SliderThumbBorderColor = sliderThumbBorder;
        if (TryColor(values, "checkBoxBorderColor", out var checkBoxBorder)) config.CheckBoxBorderColor = checkBoxBorder;
        if (TryColor(values, "checkBoxFillColor", out var checkBoxFill)) config.CheckBoxFillColor = checkBoxFill;
        if (TryColor(values, "checkBoxCheckColor", out var checkBoxCheck)) config.CheckBoxCheckColor = checkBoxCheck;
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
        var gameView = new DefaultGameView(settings.GetSnapshot(), config, screen, options.AssetManager);
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

    public AboutViewModel CreateAbout(IGameScreenNavigator navigator, GameFlowOptions options, AboutUiConfiguration config) =>
        new(navigator, config, options.AssetManager ?? _serviceProvider.GetService<IAssetManager>());

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
