using System.Text.Json;
using Avalonia.Controls;
using GalNet.Control.Views;
using GalNet.Core.Services;
using GalNet.Core.UI;

namespace GalNet.Control.UI;

/// <summary>DI-owned registry. Templates are concrete services, not data loaded from projects.</summary>
public sealed class TemplateRegistry : IWidgetTemplateRegistry, IScreenTemplateRegistry
{
    private readonly Dictionary<string, IWidgetTemplate> _widgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IScreenTemplate> _screens = new(StringComparer.OrdinalIgnoreCase);
    public IEnumerable<IWidgetTemplate> Templates => _widgets.Values;
    IEnumerable<IScreenTemplate> IScreenTemplateRegistry.Templates => _screens.Values;
    public void Register(IWidgetTemplate template) => _widgets.Add(template.Id, template);
    public void Register(IScreenTemplate template) => _screens.Add(template.Id, template);
    public bool TryGet(string id, out IWidgetTemplate? template) => _widgets.TryGetValue(id, out template);
    public bool TryGet(string id, out IScreenTemplate? template) => _screens.TryGetValue(id, out template);
}

public sealed class UiColorPalette : IColorPalette
{
    private readonly IUiProjectProvider _project;
    public event Action<string>? ColorChanged;
    public UiColorPalette(IUiProjectProvider project) { _project = project; _project.Changed += () => ColorChanged?.Invoke("*"); }
    public bool TryGet(string key, out string color) => _project.Current.Colors.TryGetValue(key, out color!);
    public string Resolve(string keyOrLiteral, string fallback = "#FFFFFFFF") =>
        keyOrLiteral.StartsWith('#') ? keyOrLiteral : TryGet(keyOrLiteral, out var color) ? color : fallback;
}

/// <summary>Disk layout for UI/: ui.json plus one JSON file per instance.</summary>
public sealed class FileUiProjectProvider : IUiProjectProvider
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
        foreach (var (category, viewId) in Current.DefaultViews)
            if (!Current.Screens.ContainsKey(viewId)) errors.Add(new($"DefaultViews.{category}", "Screen instance does not exist."));
        foreach (var widget in Current.Widgets.Values)
            foreach (var color in widget.ColorOverrides.Values.Where(c => !c.StartsWith('#') && !Current.Colors.ContainsKey(c)))
                errors.Add(new($"Widgets.{widget.Id}", $"Colour key '{color}' does not exist."));
        return errors;
    }

    private UiProject Load(UiProject defaults)
    {
        var path = Path.Combine(_root, "ui.json");
        if (!File.Exists(path)) return defaults;
        var project = JsonSerializer.Deserialize<UiProject>(File.ReadAllText(path), Options) ?? defaults;
        LoadInstances(Path.Combine(_root, "WidgetInstance"), project.Widgets);
        LoadInstances(Path.Combine(_root, "ScreenInstance"), project.Screens);
        // Previous preview builds wrote these names. Load them only when the new directory is absent.
        if (!Directory.Exists(Path.Combine(_root, "WidgetInstance")))
            LoadInstances(Path.Combine(_root, "ControlInstance"), project.Widgets);
        if (!Directory.Exists(Path.Combine(_root, "ScreenInstance")))
            LoadInstances(Path.Combine(_root, "ViewInstance"), project.Screens);
        return project;
    }
    private static void LoadInstances<T>(string path, Dictionary<string, T> target) where T : class
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            var item = JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options);
            var id = item switch { WidgetInstanceDefinition w => w.Id, ScreenInstanceDefinition s => s.Id, _ => null };
            if (!string.IsNullOrWhiteSpace(id) && item is not null) target[id] = item;
        }
    }
}

public static class UiProjectDefaults
{
    public static UiProject Create() => new()
    {
        Colors = new(StringComparer.OrdinalIgnoreCase) { ["surface"] = "#111118", ["text"] = "#FFFFFFFF", ["accent"] = "#8ED8FF" },
        DefaultViews = new(StringComparer.OrdinalIgnoreCase) { ["title"] = "title.default", ["game"] = "game.default", ["settings"] = "settings.default", ["save-load"] = "save-load.default", ["gallery"] = "gallery.default" },
        Screens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["title.default"] = new() { Id = "title.default", TemplateId = "builtin.title" },
            ["game.default"] = new() { Id = "game.default", TemplateId = "builtin.game" },
            ["settings.default"] = new() { Id = "settings.default", TemplateId = "builtin.settings" },
            ["save-load.default"] = new() { Id = "save-load.default", TemplateId = "builtin.save-load" },
            ["gallery.default"] = new() { Id = "gallery.default", TemplateId = "builtin.gallery" }
        }
    };
}

public sealed class GameScreenRouter : IGameScreenRouter
{
    private readonly IUiProjectProvider _project;
    private readonly Func<ScreenInstanceDefinition, object?, object> _factory;
    public string? CurrentCategory { get; private set; }
    public event Action<object?>? CurrentScreenChanged;
    public GameScreenRouter(IUiProjectProvider project, Func<ScreenInstanceDefinition, object?, object> factory) { _project = project; _factory = factory; }
    public bool CanNavigate(string categoryKey) => _project.Current.DefaultViews.ContainsKey(categoryKey);
    public Task NavigateAsync(string categoryKey, object? parameter = null, CancellationToken cancellationToken = default)
    {
        if (!_project.Current.DefaultViews.TryGetValue(categoryKey, out var id) || !_project.Current.Screens.TryGetValue(id, out var screen))
            throw new InvalidOperationException($"No UI screen is registered for category '{categoryKey}'.");
        CurrentCategory = categoryKey;
        CurrentScreenChanged?.Invoke(_factory(screen, parameter));
        return Task.CompletedTask;
    }
}
