using System.Text.Json.Nodes;

namespace GalNet.Core.UI;

/// <summary>Serializable, project-owned description of the game's low-code UI.</summary>
public sealed class UiProject
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DefaultViews { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WidgetInstanceDefinition> Widgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ScreenInstanceDefinition> Screens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Project-owned data for one widget. It never contains Avalonia objects.</summary>
public sealed class WidgetInstanceDefinition
{
    public string Id { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public JsonObject Configuration { get; set; } = [];
    public Dictionary<string, string> ColorOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Project-owned data for one screen. Widget references belong to Configuration.</summary>
public sealed class ScreenInstanceDefinition
{
    public string Id { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public JsonObject Configuration { get; set; } = [];
}

public sealed record UiValidationError(string Path, string Message);
