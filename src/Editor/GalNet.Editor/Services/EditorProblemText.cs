using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

/// <summary>Author-facing descriptions; raw validator details stay available separately.</summary>
internal static class EditorProblemText
{
    public static string Label(FieldDescriptor field, IEditorLocalizationService? text = null) =>
        Lookup(field.LabelKey ?? "Entry.Parameter." + field.Id, field.Id, text);

    public static string Lookup(string key, string fallback, IEditorLocalizationService? text = null)
    {
        var value = (text ?? EditorLocalizationService.Current)?[key];
        return string.IsNullOrEmpty(value) || value == key || value == "[" + key + "]" || value == "#" + key + "#" ? fallback : value;
    }

    public static string Value(FieldDescriptor field, ValueIssue issue, IEditorLocalizationService? text = null)
    {
        text ??= EditorLocalizationService.Current;
        if (text is null) return issue.Message;
        if (issue.Code == "editor.numericFallback") return text["Problem.NumericFallback"];
        var key = issue.Code switch
        {
            "field.required" => "Required",
            "value.type" => "Type",
            "value.range" => "Range",
            "value.finite" => "Finite",
            "value.null" => "Null",
            "value.option" => "Option",
            "value.length" or "value.items" => "Length",
            "value.spin" => "Spin",
            "field.conflict" => "Conflict",
            _ => "Invalid"
        };
        var minimum = field.Value.TryGetConstraint("minimum", out var min) ? min.GetRawText() : "−∞";
        var maximum = field.Value.TryGetConstraint("maximum", out var max) ? max.GetRawText() : "+∞";
        if (key == "Range" && minimum == "−∞") key = "Maximum";
        else if (key == "Range" && maximum == "+∞") key = "Minimum";
        var type = Nullable.GetUnderlyingType(field.Value.ValueType) ?? field.Value.ValueType;
        var kind = type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? "Number"
            : DefaultNumeric(type) ? "Integer" : type == typeof(string) ? "Text" : type == typeof(bool) ? "Boolean" : "Json";
        return text.Format("Problem.Value." + key, Label(field, text), text["Problem.Kind." + kind], minimum, maximum);
    }

    private static bool DefaultNumeric(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
        || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
}
