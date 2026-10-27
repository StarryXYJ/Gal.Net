using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Parameters;
using GalNet.Core.Serialization;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class VariantsShowOptions
{
    [Common, Field(0, Required = true, EditorId = ValueEditorIds.Text)] public string HandleId { get; set; } = "";
    [Common, Field(1, Required = true, EditorId = "entry.variantsasset")] public string AssetId { get; set; } = "";
    [Field(2, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Selection { get; set; }
    [Field(3, DefaultJson = "{}", EditorId = ValueEditorIds.Json, StructureType = typeof(GalNet.Contracts.Builtins.Scene.LayerTransform), StructureAllowsJsonString = true, StructureIgnoresCase = true)] public JsonElement Transform { get; set; }
    [Field(4, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 0;
    [Field(5, Minimum = 0, Maximum = 1, EditorId = ValueEditorIds.Slider, Step = .01)] public float Opacity { get; set; } = 1;
    [Field(6, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<LayerDisplayMode>))]
    public LayerDisplayMode? DisplayMode { get; set; } = LayerDisplayMode.Native;
}

[GenerateFieldSchema]
public partial class VariantsSelectOptions
{
    [Common, Field(0, Required = true, EditorId = ValueEditorIds.Text)] public string HandleId { get; set; } = "";
    [Common, Field(1, Required = true, EditorId = ValueEditorIds.Text)] public string Dimension { get; set; } = "";
    [Common, Field(2, Required = true, EditorId = ValueEditorIds.Text)] public string Value { get; set; } = "";
}

public static class VariantsArgumentValidation
{
    public static Dictionary<string, string> ReadSelection(JsonElement selection)
    {
        if (selection.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Variant selection must be an object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in selection.EnumerateObject())
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(property.Value.GetString()) || !result.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidDataException("Variant selection requires unique dimension keys and nonempty string values.");
        return result;
    }

    internal static void Show(JsonElement arguments)
    {
        RequireText(arguments, "handleId", "assetId");
        _ = ReadSelection(arguments.GetProperty("selection"));
        _ = ReadTransform(arguments.GetProperty("transform"));
    }

    public static LayerTransform ReadTransform(JsonElement value)
    {
        var options = BuiltinJson.CreateOptions(new() { PropertyNameCaseInsensitive = true, AllowDuplicateProperties = false });
        var transform = value.ValueKind == JsonValueKind.String
            ? JsonCodec.Deserialize<LayerTransform>(value.GetString()!, options)
            : JsonCodec.Deserialize<LayerTransform>(value, options);
        if (transform is null || !float.IsFinite(transform.X) || !float.IsFinite(transform.Y) || !float.IsFinite(transform.RotationDegrees)
            || !float.IsFinite(transform.ScaleX) || !float.IsFinite(transform.ScaleY) || transform.ScaleX < .001f || transform.ScaleY < .001f)
            throw new InvalidDataException("Variant transform requires finite values and positive scale.");
        return transform;
    }

    internal static void Select(JsonElement arguments)
    {
        RequireText(arguments, "handleId", "dimension");
        if (!arguments.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            throw new InvalidDataException("'value' requires a nonempty string.");
    }

    private static void RequireText(JsonElement arguments, params string[] fields)
    {
        foreach (var field in fields)
            if (!arguments.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException($"'{field}' requires nonempty text.");
    }
}
