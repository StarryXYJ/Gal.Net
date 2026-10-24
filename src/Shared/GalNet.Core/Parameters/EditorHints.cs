namespace GalNet.Core.Parameters;

/// <summary>Optional presentation metadata. Step does not restrict legal values.</summary>
public sealed record EditorHints(string? EditorId = null, double? Step = null,
    string? Format = null, string? UnitKey = null, bool IsCommon = false)
{
    // Previously built project contracts call the original four-argument constructor.
    public EditorHints(string? EditorId, double? Step, string? Format, string? UnitKey)
        : this(EditorId, Step, Format, UnitKey, false) { }

    public void Deconstruct(out string? editorId, out double? step, out string? format, out string? unitKey)
    { editorId = EditorId; step = Step; format = Format; unitKey = UnitKey; }
}

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
