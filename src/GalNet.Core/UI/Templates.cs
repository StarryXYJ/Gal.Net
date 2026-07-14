using System.Text.Json.Nodes;

namespace GalNet.Core.UI;

public interface IWidgetTemplate
{
    string Id { get; }
    string Category { get; }
    IReadOnlyCollection<string> ColorKeys { get; }
    void Validate(WidgetInstanceDefinition instance, ICollection<UiValidationError> errors);
}

public interface IScreenTemplate
{
    string Id { get; }
    string Category { get; }
    void Validate(ScreenInstanceDefinition instance, ICollection<UiValidationError> errors);
}

public interface IWidgetTemplateRegistry
{
    IEnumerable<IWidgetTemplate> Templates { get; }
    void Register(IWidgetTemplate template);
    bool TryGet(string id, out IWidgetTemplate? template);
}

public interface IScreenTemplateRegistry
{
    IEnumerable<IScreenTemplate> Templates { get; }
    void Register(IScreenTemplate template);
    bool TryGet(string id, out IScreenTemplate? template);
}

/// <summary>Resolves palette keys or literal colours and notifies renderers when a key changes.</summary>
public interface IColorPalette
{
    event Action<string>? ColorChanged;
    bool TryGet(string key, out string color);
    string Resolve(string keyOrLiteral, string fallback = "#FFFFFFFF");
}
