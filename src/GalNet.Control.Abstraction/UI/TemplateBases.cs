using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using GalNet.Core.UI;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Abstraction.UI;

/// <summary>Common, serializable presentation settings shared by screen and widget configurations.</summary>
/// <remarks>Colour values are palette keys, never literal brush values.</remarks>
public class PresentationConfig
{
    public double? FontSize { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? MinWidth { get; set; }
    public double? MinHeight { get; set; }
    public double? CornerRadius { get; set; }
    public double? BorderThickness { get; set; }
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public string? BorderBrush { get; set; }
    public string? HoverForeground { get; set; }
    public string? HoverBackground { get; set; }
    public string? PressedForeground { get; set; }
    public string? PressedBackground { get; set; }
    public string? DisabledForeground { get; set; }
    public string? DisabledBackground { get; set; }

    public IEnumerable<string> ColorKeys()
    {
        foreach (var key in new[] { Foreground, Background, BorderBrush, HoverForeground, HoverBackground,
                     PressedForeground, PressedBackground, DisabledForeground, DisabledBackground })
            if (!string.IsNullOrWhiteSpace(key)) yield return key;
    }
}

/// <summary>
/// Typed adapter for plugin-facing widget templates. Core stores JSON while template code owns its schema.
/// </summary>
public abstract class WidgetTemplate<TWidget, TConfig>(string id, string category) : IWidgetTemplate
    where TWidget : AvaloniaControl
    where TConfig : PresentationConfig, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string Id { get; } = id;
    public string Category { get; } = category;
    public virtual IReadOnlyCollection<string> ColorKeys => Array.Empty<string>();

    public void Validate(WidgetInstanceDefinition instance, ICollection<UiValidationError> errors)
    {
        if (!string.Equals(instance.TemplateId, Id, StringComparison.OrdinalIgnoreCase))
            errors.Add(new($"Widgets.{instance.Id}", "Template id mismatch."));
        if (!TryRead(instance.Configuration, out var config, out var error))
        {
            errors.Add(new($"Widgets.{instance.Id}.Configuration", error!));
            return;
        }
        Validate(config!, errors, $"Widgets.{instance.Id}.Configuration");
    }

    public WidgetPresentation Build(WidgetInstanceDefinition instance, WidgetBuildContext context)
    {
        if (!TryRead(instance.Configuration, out var config, out var error))
            throw new InvalidOperationException($"Invalid configuration for widget '{instance.Id}': {error}");
        var view = Create(config!, context);
        var viewModel = CreateViewModel(config!, view, context);
        view.DataContext = viewModel;
        PaletteScope.SetPalette(view, context.Palette);
        return new(view, viewModel);
    }

    protected virtual void Validate(TConfig config, ICollection<UiValidationError> errors, string path) { }
    protected abstract TWidget Create(TConfig config, WidgetBuildContext context);
    protected virtual object CreateViewModel(TConfig config, TWidget view, WidgetBuildContext context) => view;

    private static bool TryRead(JsonObject configuration, out TConfig? config, out string? error)
    {
        try
        {
            config = configuration.Deserialize<TConfig>(JsonOptions) ?? new TConfig();
            error = null;
            return true;
        }
        catch (JsonException ex) { config = null; error = ex.Message; return false; }
    }
}

/// <summary>Typed adapter for screen templates with project-owned JSON configuration.</summary>
public abstract class ScreenTemplate<TConfig>(string id, string category) : IScreenTemplate
    where TConfig : PresentationConfig, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string Id { get; } = id;
    public string Category { get; } = category;

    public void Validate(ScreenInstanceDefinition instance, ICollection<UiValidationError> errors)
    {
        if (!string.Equals(instance.TemplateId, Id, StringComparison.OrdinalIgnoreCase))
            errors.Add(new($"Screens.{instance.Id}", "Template id mismatch."));
        try { Validate(instance.Configuration.Deserialize<TConfig>(JsonOptions) ?? new TConfig(), errors, $"Screens.{instance.Id}.Configuration"); }
        catch (JsonException ex) { errors.Add(new($"Screens.{instance.Id}.Configuration", ex.Message)); }
    }

    public ScreenPresentation Build(ScreenInstanceDefinition instance, ScreenBuildContext context)
    {
        try { return Build(instance, instance.Configuration.Deserialize<TConfig>(JsonOptions) ?? new TConfig(), context); }
        catch (JsonException ex) { throw new InvalidOperationException($"Invalid configuration for screen '{instance.Id}'.", ex); }
    }

    protected virtual void Validate(TConfig config, ICollection<UiValidationError> errors, string path) { }
    protected abstract ScreenPresentation Build(ScreenInstanceDefinition instance, TConfig config, ScreenBuildContext context);
}
