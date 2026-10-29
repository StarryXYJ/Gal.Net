using System.Text.Json;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GalNet.Editor.Abstraction.Schemas;

/// <summary>Editor-facing input kind for a serialized entry parameter.</summary>
public enum EntryParameterType
{
    Text,
    MultilineText,
    Integer,
    Float,
    Autocomplete,
    ImageAsset,
    AudioAsset,
    VideoAsset,
    EffectProgramAsset,
    Select,
    VariableName,
    Expression,
    Json
}

/// <summary>Editor input hints projected from the single parameter contract.</summary>
public static class EntrySchema
{
    public static EntryParameterType GetEditorType(DynamicParameterDescriptor parameter)
    {
        if (parameter.Editor?.EditorId is { } editorId && editorId != ValueEditorIds.Numeric)
            foreach (var type in Enum.GetValues<EntryParameterType>())
                if (GetEditorId(type) == editorId) return type;
        if (parameter.ValueType == typeof(int)) return EntryParameterType.Integer;
        if (parameter.ValueType == typeof(float) || parameter.ValueType == typeof(double) || parameter.ValueType == typeof(decimal)) return EntryParameterType.Float;
        if (parameter.ValueType == typeof(JsonElement)) return EntryParameterType.Json;
        if (parameter.ValueType == typeof(bool)) return EntryParameterType.Select;
        return EntryParameterType.Text;
    }

    private static string GetEditorId(EntryParameterType type) => type switch
    {
        EntryParameterType.Integer or EntryParameterType.Float => ValueEditorIds.Numeric,
        EntryParameterType.Json => ValueEditorIds.Json,
        EntryParameterType.Select => ValueEditorIds.Choice,
        EntryParameterType.Text => ValueEditorIds.Text,
        _ => "entry." + type.ToString().ToLowerInvariant()
    };
}
