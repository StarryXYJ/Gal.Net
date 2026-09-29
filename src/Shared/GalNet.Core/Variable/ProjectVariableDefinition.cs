namespace GalNet.Core.Variable;

/// <summary>Project-level variable declaration and its initial value.</summary>
public sealed class ProjectVariableDefinition
{
    public string Name { get; set; } = "";
    public Variable DefaultValue { get; set; } = new();

    /// <summary>
    /// Declared value type. Changing it replaces the default value with that type's zero value,
    /// while retaining the variable UID and synchronizing its name.
    /// </summary>
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

    /// <summary>Creates an independent declaration while preserving the variable UID and typed value.</summary>
    public ProjectVariableDefinition Clone()
    {
        return new ProjectVariableDefinition
        {
            Name = Name,
            DefaultValue = new Variable
            {
                Uid = DefaultValue.Uid,
                Name = Name,
                Value = CloneValue(DefaultValue)
            }
        };
    }

    private static VariableValue CloneValue(Variable variable) => variable.Type switch
    {
        VariableType.Bool => VariableValue.From(variable.AsBool()),
        VariableType.Int => VariableValue.From(variable.AsInt()),
        VariableType.Float => VariableValue.From(variable.AsFloat()),
        _ => VariableValue.From(variable.AsString())
    };
}
