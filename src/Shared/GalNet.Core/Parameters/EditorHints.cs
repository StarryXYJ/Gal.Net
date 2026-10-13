namespace GalNet.Core.Parameters;

/// <summary>Optional presentation metadata. Step does not restrict legal values.</summary>
public sealed record EditorHints(string? EditorId = null, double? Step = null,
    string? Format = null, string? UnitKey = null);

/// <summary>Shared editor names; hosts may register any other string ID.</summary>
public static class ValueEditorIds
{
    public const string Numeric = "numeric";
    public const string Slider = "slider";
    public const string Toggle = "toggle";
    public const string Choice = "choice";
    public const string Text = "text";
    public const string Json = "json";
}
