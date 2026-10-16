using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

/// <summary>Untranslated presentation data; rendering never changes author content or history.</summary>
public sealed record EditorText(string? Key, params object[] Arguments)
{
    public static EditorText Literal(string text) => new(null, text);
    public string Render(IEditorLocalizationService localization) => Key is null ? (string)Arguments[0] : localization.Format(Key, Arguments.Select(argument => argument is EditorText text ? (object)text.Render(localization) : argument).ToArray());
    public static EditorText Error(Exception error) => error is EditorOperationException coded
        ? new("Error." + coded.Code, coded.Arguments)
        : new("Error.Unexpected", error.Message);

    // Existing delegate records keep invariant descriptions until their domain migration.
    // Interpret only this explicit built-in set, never author names or arbitrary strings as translation keys.
    public static EditorText History(string description) => description switch
    {
        "Rename node" => new("History.Rename"),
        "Move nodes" => new("History.Move"),
        "Change connections" => new("History.Connect"),
        "Delete nodes and Group sources" => new("History.DeleteNodes"),
        "Create node and source" => new("History.CreateNode"),
        "Change project name and settings" => new("History.Settings"),
        "Add choice option" => new("History.AddChoice"),
        "Delete choice option" => new("History.DeleteChoice"),
        "Move choice option" => new("History.MoveChoice"),
        "Add condition" => new("History.AddCondition"),
        "Delete condition" => new("History.DeleteCondition"),
        "Move condition" => new("History.MoveCondition"),
        "Add entry" => new("History.AddEntry"),
        "Delete entry" => new("History.DeleteEntry"),
        "Move entry" => new("History.MoveEntry"),
        "Edit variable definitions" => new("History.Variables"),
        _ when description.StartsWith("Edit ", StringComparison.Ordinal) => new("History.EditField", description[5..]),
        _ when description.StartsWith("Add ", StringComparison.Ordinal) && description.EndsWith(" entries", StringComparison.Ordinal)
            && int.TryParse(description.AsSpan(4, description.Length - 12), out var count) => new("History.AddEntries", count),
        _ => Literal(description)
    };
}
