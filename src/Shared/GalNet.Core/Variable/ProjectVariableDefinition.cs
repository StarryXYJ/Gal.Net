using System.Text.Json.Serialization;

namespace GalNet.Core.Variable;

/// <summary>Project-level variable declaration and its initial value.</summary>
public sealed class ProjectVariableDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    [JsonPropertyName("defaultValue")]
    public Variable DefaultValue { get; set; } = new();

    /// <summary>
    /// Declared value type. Changing it replaces the default value with that type's zero value,
    /// while retaining the variable UID and synchronizing its name.
    /// </summary>
    [JsonPropertyName("type")]
    public VariableType Type
    {
        get => DefaultValue.Type;
        set
        {
            if (DefaultValue.Type == value)
                return;

            DefaultValue = new Variable
            {
                Uid = DefaultValue.Uid,
                Name = Name,
                Value = value switch
                {
                    VariableType.Bool => VariableValue.From(false),
                    VariableType.Int => VariableValue.From(0),
                    VariableType.Float => VariableValue.From(0f),
                    _ => VariableValue.From("")
                }
            };
        }
    }

    /// <summary>Copies author content without normalizing names; immutable typed values may be shared.</summary>
    public ProjectVariableDefinition Clone() => new()
    {
        Name = Name,
        Description = Description,
        DefaultValue = DefaultValue.Clone()
    };
}
