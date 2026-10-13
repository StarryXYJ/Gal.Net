using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Describes the JSON input actually consumed by typed editor commands.</summary>
public static class CommandSchemaBuilder
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static EditorCommandSchema Build(Type commandType, string commandId)
    {
        var schema = (JsonObject)Options.GetJsonSchemaAsNode(commandType);
        var properties = schema["properties"]!.AsObject();
        properties["commandId"] = new JsonObject { ["type"] = "string", ["const"] = commandId };
        var required = new JsonArray("commandId");
        var nullable = new NullabilityInfoContext();
        var constructor = commandType.GetConstructors().OrderByDescending(item => item.GetParameters().Length).First();
        var parameters = new List<EditorCommandParameter>();
        foreach (var parameter in constructor.GetParameters())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!);
            if (name == "commandId") continue;
            var isRequired = !parameter.HasDefaultValue &&
                Nullable.GetUnderlyingType(parameter.ParameterType) is null &&
                nullable.Create(parameter).ReadState != NullabilityState.Nullable;
            var exported = properties[name]?.DeepClone();
            var parameterSchema = exported as JsonObject ?? new JsonObject();
            if (exported is JsonValue booleanSchema)
                parameterSchema["allOf"] = new JsonArray(booleanSchema.DeepClone());
            if (parameter.HasDefaultValue)
                parameterSchema["default"] = JsonSerializer.SerializeToNode(parameter.DefaultValue, parameter.ParameterType, Options);
            properties[name] = parameterSchema.DeepClone();
            if (isRequired) required.Add(name);
            var type = parameterSchema["type"] is JsonValue value && value.TryGetValue<string>(out var single)
                ? single
                : GetJsonType(parameter.ParameterType);
            parameters.Add(new EditorCommandParameter(name, type, isRequired, $"Value for {name}.")
            { JsonSchema = JsonSerializer.SerializeToElement(parameterSchema) });
        }
        schema["required"] = required;
        return new EditorCommandSchema(parameters.AsReadOnly()) { JsonSchema = JsonSerializer.SerializeToElement(schema) };
    }

    private static string GetJsonType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string) || type.IsEnum) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(JsonElement)) return "any";
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "number";
        if (type.GetInterfaces().Append(type).Any(item => item.IsGenericType &&
                (item.GetGenericTypeDefinition() == typeof(IDictionary<,>) || item.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))))
            return "object";
        return typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ? "array" : "object";
    }
}
