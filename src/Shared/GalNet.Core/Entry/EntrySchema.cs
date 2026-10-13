using System.Text.Json;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GalNet.Core.Entry;

/// <summary>Small immutable helpers used by outer authoring catalogs.</summary>
public static class EntrySchema
{
    public static IReadOnlyDictionary<string, EntryParameterType> Parameters(params (string Name, EntryParameterType Type)[] items) =>
        new Dictionary<string, EntryParameterType>(items.ToDictionary(x => x.Name, x => x.Type), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Defaults(params (string Name, string Value)[] items) =>
        new Dictionary<string, string>(items.ToDictionary(x => x.Name, x => x.Value), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Options(params (string Name, string[] Values)[] items) =>
        new Dictionary<string, IReadOnlyList<string>>(items.ToDictionary(x => x.Name, x => (IReadOnlyList<string>)x.Values), StringComparer.Ordinal);

    public static DynamicParameterTable DynamicParameters(
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? options = null,
        Action<JsonElement>? validateArguments = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new DynamicParameterTable(parameters.Select(pair =>
        {
            var constraints = CreateConstraints(options?.GetValueOrDefault(pair.Key));
            return new DynamicParameterDescriptor(
                pair.Key,
                GetValueType(pair.Value),
                defaultValue: defaults is not null && defaults.TryGetValue(pair.Key, out var value)
                    ? ValueCodec.FromEditorText(new ValueDescriptor(GetValueType(pair.Value)), value)
                    : null,
                constraints: constraints, editor: new EditorHints(GetEditorId(pair.Value)));
        }), validateArguments);
    }

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

    private static Type GetValueType(EntryParameterType type) => type switch
    {
        EntryParameterType.Integer => typeof(int),
        EntryParameterType.Float => typeof(float),
        EntryParameterType.Json => typeof(JsonElement),
        _ => typeof(string)
    };

    private static JsonElement CreateConstraints(IReadOnlyList<string>? options)
    {
        var constraints = new Dictionary<string, object?>();
        if (options is not null) constraints["options"] = options;
        return JsonSerializer.SerializeToElement(constraints);
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
