using GalNet.Core.Variable;

namespace GalNet.Editor.ViewModels;

public sealed class ConditionVariableSuggestion
{
    public string Name { get; init; } = "";
    public VariableScope Scope { get; init; }
    public string DisplayText => $"{Name} ({Scope.ToString().ToLowerInvariant()})";
}
