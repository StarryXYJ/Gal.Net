using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Variable;

namespace GalNet.Editor.ViewModels;

/// <summary>Preview values only. Declaration editing belongs to the project Variables panel.</summary>
public sealed class VariableListEditorViewModel : ObservableObject
{
    public IReadOnlyList<VariableEditorItemViewModel> Items { get; }
    public bool ShowCurrentValue { get; private set; }

    public VariableListEditorViewModel(IReadOnlyList<ProjectVariableDefinition> definitions, bool showCurrentValue,
        Func<string, Variable?> getCurrentValue, Action<string, object> setCurrentValue)
    {
        ShowCurrentValue = showCurrentValue;
        Items = definitions.Select(definition =>
        {
            var name = definition.Name;
            return new VariableEditorItemViewModel(definition, showCurrentValue,
                getCurrentValue(name), value => setCurrentValue(name, value));
        }).ToArray();
    }

    public void SetCurrentValueVisibility(bool visible)
    {
        ShowCurrentValue = visible;
        OnPropertyChanged(nameof(ShowCurrentValue));
        foreach (var item in Items) item.SetCurrentValueVisible(visible);
    }

    public void UpdateCurrentValue(string name, Variable variable) =>
        Items.FirstOrDefault(item => item.Name == name)?.SetCurrentValue(variable);
}

/// <summary>Detached declaration metadata and an editable copy of a runtime value.</summary>
public sealed class VariableEditorItemViewModel : ObservableObject
{
    private readonly Action<object> _write;
    private Variable _currentValue;
    public string Name { get; }
    public VariableType SelectedType { get; }
    public string DefaultValueText { get; }
    public bool CanEditCurrentValue { get; private set; }
    public bool IsBool => SelectedType == VariableType.Bool;
    public bool IsInt => SelectedType == VariableType.Int;
    public bool IsFloat => SelectedType == VariableType.Float;
    public bool IsString => SelectedType == VariableType.String;

    public VariableEditorItemViewModel(ProjectVariableDefinition definition, bool showCurrentValue,
        Variable? currentValue, Action<object> write)
    {
        Name = definition.Name;
        SelectedType = definition.Type;
        DefaultValueText = definition.DefaultValue.Value.Serialize();
        _currentValue = (currentValue ?? definition.DefaultValue).Clone();
        CanEditCurrentValue = showCurrentValue;
        _write = write;
    }

    public bool CurrentBoolValue { get => _currentValue.AsBool(); set => Commit(value); }
    public decimal CurrentIntValue { get => _currentValue.AsInt(); set => Commit((int)value); }
    public decimal CurrentFloatValue { get => (decimal)_currentValue.AsFloat(); set => Commit((float)value); }
    public string CurrentStringValue { get => _currentValue.AsString(); set => Commit(value ?? string.Empty); }

    private void Commit(object value)
    {
        if (!CanEditCurrentValue) return;
        _currentValue.SetValue(value);
        _write(value);
    }

    public void SetCurrentValueVisible(bool visible)
    {
        CanEditCurrentValue = visible;
        OnPropertyChanged(nameof(CanEditCurrentValue));
    }

    public void SetCurrentValue(Variable variable)
    {
        _currentValue = variable.Clone();
        OnPropertyChanged(nameof(CurrentBoolValue));
        OnPropertyChanged(nameof(CurrentIntValue));
        OnPropertyChanged(nameof(CurrentFloatValue));
        OnPropertyChanged(nameof(CurrentStringValue));
    }
}
