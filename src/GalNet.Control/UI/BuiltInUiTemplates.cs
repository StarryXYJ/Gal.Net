using GalNet.Core.UI;
using GalNet.Control.UI.Instances;
using GalNet.Control.ViewModels;

namespace GalNet.Control.UI;

/// <summary>Metadata for built-in templates. Rendering stays in the existing typed Avalonia views.</summary>
public sealed class BuiltInWidgetTemplate(string id, string category, params string[] colorKeys) : IWidgetTemplate
{
    public string Id { get; } = id;
    public string Category { get; } = category;
    public IReadOnlyCollection<string> ColorKeys { get; } = colorKeys;
    public void Validate(WidgetInstanceDefinition instance, ICollection<UiValidationError> errors)
    {
        if (!string.Equals(instance.TemplateId, Id, StringComparison.OrdinalIgnoreCase))
            errors.Add(new($"Widgets.{instance.Id}", "Template id mismatch."));
    }
}

public sealed class BuiltInScreenTemplate(string id, string category) : IScreenBuilderTemplate
{
    public string Id { get; } = id;
    public string Category { get; } = category;
    public void Validate(ScreenInstanceDefinition instance, ICollection<UiValidationError> errors)
    {
        if (!string.Equals(instance.TemplateId, Id, StringComparison.OrdinalIgnoreCase))
            errors.Add(new($"Screens.{instance.Id}", "Template id mismatch."));
    }

    public object Build(ScreenInstanceDefinition instance, ScreenBuildContext context) => Id switch
    {
        "builtin.title" => context.GameFlowFactory.CreateStart(context.Navigation, context.Options),
        "builtin.game" => context.GameFlowFactory.CreateRun(context.Navigation, context.Options),
        "builtin.settings" => context.GameFlowFactory.CreateSettings(context.Navigation),
        "builtin.save-load" => context.GameFlowFactory.CreateSaveLoad(context.Navigation, context.Options, SaveLoadMode.Load),
        "builtin.gallery" => context.GameFlowFactory.CreateGallery(context.Navigation, context.Options),
        _ => throw new InvalidOperationException($"Screen template '{Id}' cannot build a screen.")
    };
}

public static class BuiltInUiTemplates
{
    public static void Register(TemplateRegistry registry)
    {
        ((IWidgetTemplateRegistry)registry).Register(new BuiltInWidgetTemplate("builtin.button", "button", "surface", "text", "accent"));
        ((IWidgetTemplateRegistry)registry).Register(new BuiltInWidgetTemplate("builtin.dialogue", "dialogue", "surface", "text"));
        ((IWidgetTemplateRegistry)registry).Register(new BuiltInWidgetTemplate("builtin.choice", "choice", "surface", "text", "accent"));
        foreach (var key in new[] { "title", "game", "settings", "save-load", "gallery" })
            ((IScreenTemplateRegistry)registry).Register(new BuiltInScreenTemplate($"builtin.{key}", key));
    }
}
