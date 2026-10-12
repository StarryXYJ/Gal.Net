using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Variable;

/// <summary>
/// 变量封装对象 —— 持有一个 VariableValue（discriminated union），全局唯一标识。
/// player 和 save 变量共用命名空间，UID 不可重名。
/// </summary>
[JsonConverter(typeof(VariableJsonConverter))]
public sealed class Variable
{
    /// <summary>全局唯一标识</summary>
    public string Uid { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>变量名（显示用）</summary>
    public string Name { get; set; } = "";

    /// <summary>当前值（discriminated union）</summary>
    [JsonIgnore]
    public VariableValue Value { get; set; } = VariableValue.From("");

    // ── 快捷读取（委托给 Value）───────────────────────────────────────────

    public VariableType Type => Value.Type;
    public bool AsBool() => Value.AsBool();
    public int AsInt() => Value.AsInt();
    public float AsFloat() => Value.AsFloat();
    public string AsString() => Value.AsString();

    // ── 快捷写入（替换 Value 实例）───────────────────────────────────────

    public void SetValue(bool v) => Value = VariableValue.From(v);
    public void SetValue(int v) => Value = VariableValue.From(v);
    public void SetValue(float v) => Value = VariableValue.From(v);
    public void SetValue(string v) => Value = VariableValue.From(v);
    public void SetValue(object v) => Value = VariableValue.FromObject(v);
}

/// <summary>
/// Variable 的类型化 JSON 转换器；只接受当前序列化格式。
/// </summary>
public sealed class VariableJsonConverter : JsonConverter<Variable>
{
    public override Variable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var uid = root.GetProperty("Uid").GetString() ?? throw new JsonException("Variable Uid is required.");
        var name = root.GetProperty("Name").GetString() ?? throw new JsonException("Variable Name is required.");
        var typeVal = (VariableType)root.GetProperty("Type").GetInt32();
        var propVal = root.GetProperty("Value");
        var value = typeVal switch
        {
            VariableType.Bool => VariableValue.From(propVal.GetBoolean()),
            VariableType.Int => VariableValue.From(propVal.GetInt32()),
            VariableType.Float => VariableValue.From(propVal.GetSingle()),
            VariableType.String => VariableValue.From(propVal.GetString() ?? throw new JsonException("Variable string value is required.")),
            _ => throw new JsonException($"Unknown variable type '{typeVal}'.")
        };

        return new Variable
        {
            Uid = uid,
            Name = name,
            Value = value
        };
    }

    public override void Write(Utf8JsonWriter writer, Variable value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Uid", value.Uid);
        writer.WriteString("Name", value.Name);
        writer.WriteNumber("Type", (int)value.Type);

        switch (value.Type)
        {
            case VariableType.Bool:
                writer.WriteBoolean("Value", value.AsBool());
                break;
            case VariableType.Int:
                writer.WriteNumber("Value", value.AsInt());
                break;
            case VariableType.Float:
                writer.WriteNumber("Value", value.AsFloat());
                break;
            default:
                writer.WriteString("Value", value.AsString());
                break;
        }
        writer.WriteEndObject();
    }
}
