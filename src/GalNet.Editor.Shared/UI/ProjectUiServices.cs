using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Services;
using GalNet.Core.UI;

namespace GalNet.Editor.Shared.UI;

/// <summary>Editor-owned persistence for UI instance files. Runtime Control never touches disk.</summary>
public sealed class FileUiProjectProvider : IUiProjectProvider, IWidgetInstanceProvider, IScreenInstanceProvider
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _root;
    public UiProject Current { get; private set; }
    public event Action? Changed;

    public FileUiProjectProvider(string projectRoot, UiProject? defaults = null)
    {
        _root = Path.Combine(projectRoot, "UI");
        Current = Load(defaults ?? UiProjectDefaults.Create());
    }
    public void Replace(UiProject project) { Current = project; NotifyChanged(); }
    public void NotifyChanged() => Changed?.Invoke();
    public bool TryGetWidget(string id, out WidgetInstanceDefinition? instance) => Current.Widgets.TryGetValue(id, out instance);
    public bool TryGetScreen(string id, out ScreenInstanceDefinition? instance) => Current.Screens.TryGetValue(id, out instance);
    public bool TryGetDefaultScreen(string categoryKey, out ScreenInstanceDefinition? instance)
    {
        if (Current.DefaultViews.TryGetValue(categoryKey, out var id)) return TryGetScreen(id, out instance);
        instance = null;
        return false;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var widgets = Path.Combine(_root, "WidgetInstance");
        var screens = Path.Combine(_root, "ScreenInstance");
        Directory.CreateDirectory(widgets); Directory.CreateDirectory(screens);
        var manifest = new UiProject { Version = Current.Version, Colors = Current.Colors, DefaultViews = Current.DefaultViews };
        await File.WriteAllTextAsync(Path.Combine(_root, "ui.json"), JsonSerializer.Serialize(manifest, Options), cancellationToken);
        foreach (var item in Current.Widgets.Values)
            await File.WriteAllTextAsync(Path.Combine(widgets, item.Id + ".json"), JsonSerializer.Serialize(item, Options), cancellationToken);
        foreach (var item in Current.Screens.Values)
            await File.WriteAllTextAsync(Path.Combine(screens, item.Id + ".json"), JsonSerializer.Serialize(item, Options), cancellationToken);
    }
    public IReadOnlyList<UiValidationError> Validate()
    {
        var errors = new List<UiValidationError>();
        foreach (var (category, id) in Current.DefaultViews)
            if (!Current.Screens.ContainsKey(id)) errors.Add(new($"DefaultViews.{category}", "Screen instance does not exist."));
        return errors;
    }
    private UiProject Load(UiProject defaults)
    {
        var manifestPath = Path.Combine(_root, "ui.json");
        if (!File.Exists(manifestPath)) return defaults;
        var project = JsonSerializer.Deserialize<UiProject>(File.ReadAllText(manifestPath), Options) ?? defaults;
        // Existing projects may predate newly-added built-ins. Keep their explicit
        // values, while materialising any missing default screen/widget instances.
        defaults.Version = project.Version;
        foreach (var (key, value) in project.Colors) defaults.Colors[key] = value;
        foreach (var (key, value) in project.DefaultViews) defaults.DefaultViews[key] = value;
        foreach (var (key, value) in project.Widgets) defaults.Widgets[key] = value;
        foreach (var (key, value) in project.Screens) defaults.Screens[key] = value;
        LoadInstances(Path.Combine(_root, "WidgetInstance"), defaults.Widgets);
        LoadInstances(Path.Combine(_root, "ScreenInstance"), defaults.Screens);
        if (!Directory.Exists(Path.Combine(_root, "WidgetInstance"))) LoadInstances(Path.Combine(_root, "ControlInstance"), defaults.Widgets);
        if (!Directory.Exists(Path.Combine(_root, "ScreenInstance"))) LoadInstances(Path.Combine(_root, "ViewInstance"), defaults.Screens);
        return defaults;
    }
    private static void LoadInstances<T>(string path, Dictionary<string, T> destination) where T : class
    {
        if (!Directory.Exists(path)) return;
        foreach (var pathFile in Directory.EnumerateFiles(path, "*.json"))
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(pathFile), Options);
            var id = value switch { WidgetInstanceDefinition widget => widget.Id, ScreenInstanceDefinition screen => screen.Id, _ => null };
            if (!string.IsNullOrWhiteSpace(id) && value is not null) destination[id] = value;
        }
    }
}

public sealed class ProjectColorPalette : IColorPalette
{
    private readonly IUiProjectProvider _project;
    public ProjectColorPalette(IUiProjectProvider project) { _project = project; _project.Changed += () => OnPropertyChanged("Item[]"); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IBrush this[string key] => SolidColorBrush.Parse(_project.Current.Colors.TryGetValue(key, out var value) ? value : "#FFFF00FF");
    public void Set(string key, string value) { _project.Current.Colors[key] = value; _project.NotifyChanged(); }
    public bool CanDelete(string key) => !_project.Current.Widgets.Values.Any(x => x.ColorOverrides.Values.Contains(key, StringComparer.OrdinalIgnoreCase));
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public static class UiProjectDefaults
{
    public static UiProject Create() => new()
    {
        Colors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PrimaryColor"] = "#8ED8FF",
            ["PrimaryColorHover"] = "#B5E7FF",
            ["Background0"] = "#111118",
            ["Background1"] = "#292933",
            ["Background2"] = "#3A3A47",
            ["HighlightColor"] = "#8ED8FF",
            ["FontColor0"] = "#FFFFFFFF",
            ["FontColor1"] = "#C8C8D0",
            ["FontColor2"] = "#868692",
            ["FontHighlightColor"] = "#111118",
            ["HighlightBackground"] = "#8ED8FF",
            ["BorderColor"] = "#535364",
            ["DisabledColor"] = "#5C5C68",
            ["DangerColor"] = "#FF7085"
        },
        DefaultViews = new(StringComparer.OrdinalIgnoreCase) { ["title"] = "title.default", ["game"] = "game.default", ["settings"] = "settings.default", ["save-load"] = "save-load.default", ["gallery"] = "gallery.default" },
        Screens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["title.default"] = new() { Id = "title.default", TemplateId = "builtin.title", Configuration = new JsonObject { ["ShowGallery"] = true, ["Widgets"] = new JsonObject { ["MenuButton"] = "button.title" } } },
            ["game.default"] = new() { Id = "game.default", TemplateId = "builtin.game", Configuration = new JsonObject { ["Widgets"] = new JsonObject { ["AutoToggle"] = "toggle.command", ["QuickToggle"] = "toggle.command", ["SaveButton"] = "button.command", ["LoadButton"] = "button.command", ["SettingsButton"] = "button.command", ["MenuButton"] = "button.command", ["ScreenshotButton"] = "button.command", ["HideButton"] = "button.command" } } },
            ["settings.default"] = new() { Id = "settings.default", TemplateId = "builtin.settings", Configuration = new JsonObject { ["Widgets"] = new JsonObject { ["BgmVolume"] = "slider.default", ["SfxVolume"] = "slider.default", ["VoiceVolume"] = "slider.default", ["TextSpeed"] = "slider.default", ["AutoDelay"] = "slider.default", ["QuickDelay"] = "slider.default", ["Fullscreen"] = "toggle.default", ["BackButton"] = "button.default" } } },
            ["save-load.default"] = new() { Id = "save-load.default", TemplateId = "builtin.save-load", Configuration = new JsonObject { ["Widgets"] = new JsonObject { ["Slot"] = "save-slot.default", ["PageButton"] = "button.default", ["BackButton"] = "button.default" } } },
            ["gallery.default"] = new() { Id = "gallery.default", TemplateId = "builtin.gallery", Configuration = new JsonObject { ["Widgets"] = new JsonObject { ["CategoryButton"] = "button.default", ["ItemButton"] = "button.gallery-item", ["BackButton"] = "button.default" } } }
        },
        Widgets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["button.default"] = new() { Id = "button.default", TemplateId = "builtin.button", Configuration = new JsonObject { ["FontSize"] = 16, ["Background"] = "Background1", ["Foreground"] = "FontColor0", ["BorderBrush"] = "BorderColor" } },
            ["button.title"] = new() { Id = "button.title", TemplateId = "builtin.title-button", Configuration = new JsonObject { ["FontSize"] = 24, ["Background"] = "HighlightBackground", ["Foreground"] = "FontHighlightColor" } },
            ["button.command"] = new() { Id = "button.command", TemplateId = "builtin.button", Configuration = new JsonObject { ["Foreground"] = "FontColor1", ["HoverForeground"] = "PrimaryColor", ["PressedForeground"] = "HighlightColor" } },
            ["button.gallery-item"] = new() { Id = "button.gallery-item", TemplateId = "builtin.button", Configuration = new JsonObject { ["Width"] = 180, ["Height"] = 120, ["Background"] = "Background1", ["Foreground"] = "FontColor0" } },
            ["dialogue.default"] = new() { Id = "dialogue.default", TemplateId = "builtin.dialogue", Configuration = new JsonObject { ["Background"] = "Background1", ["Foreground"] = "FontColor0", ["BorderBrush"] = "BorderColor" } },
            ["choice.default"] = new() { Id = "choice.default", TemplateId = "builtin.choice", Configuration = new JsonObject { ["Foreground"] = "FontColor0", ["Background"] = "Background1" } },
            ["slider.default"] = new() { Id = "slider.default", TemplateId = "builtin.slider", Configuration = new JsonObject { ["Foreground"] = "PrimaryColor", ["Background"] = "Background2", ["BorderBrush"] = "BorderColor" } },
            ["toggle.default"] = new() { Id = "toggle.default", TemplateId = "builtin.toggle", Configuration = new JsonObject { ["Foreground"] = "FontColor0" } },
            ["toggle.command"] = new() { Id = "toggle.command", TemplateId = "builtin.command-toggle", Configuration = new JsonObject { ["Foreground"] = "FontColor1", ["HoverForeground"] = "PrimaryColor", ["PressedForeground"] = "HighlightColor" } },
            ["save-slot.default"] = new() { Id = "save-slot.default", TemplateId = "builtin.save-slot", Configuration = new JsonObject { ["Background"] = "Background1", ["Foreground"] = "FontColor0", ["BorderBrush"] = "BorderColor" } }
        }
    };
}
