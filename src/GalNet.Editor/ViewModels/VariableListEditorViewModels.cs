using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Variable;
using GalNet.Editor.Models;
using GalNet.Editor.Controls;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

public sealed partial class VariableListEditorViewModel : ObservableObject
{
    private readonly VariableDefinitionCollection _definitions;
    private readonly VariableScope _scope;
    private readonly Action _persistDefinitions;
    private readonly Func<string, Variable?> _getCurrentValue;
    private readonly Action<string, object> _setCurrentValue;
    private readonly Action<string> _removeCurrentValue;
    private readonly Action<string, string> _renameCurrentValue;
    private readonly Func<string, VariableScope, bool> _isNameAvailable;
    private readonly Func<string, VariableScope, string> _resolveAvailableName;
    private readonly Action<string>? _onNameConflict;
    private readonly bool _allowCurrentEditing;

    public ObservableCollection<VariableEditorItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _showCurrentValue;

    public VariableListEditorViewModel(
        List<ProjectVariableDefinition> source,
        VariableScope scope,
        bool showCurrentValue,
        bool allowCurrentEditing,
        Func<string, VariableScope, bool> isNameAvailable,
        Func<string, VariableScope, string> resolveAvailableName,
        Func<string, Variable?> getCurrentValue,
        Action<string, object> setCurrentValue,
        Action<string> removeCurrentValue,
        Action<string, string> renameCurrentValue,
        Action persistDefinitions,
        Action<string>? onNameConflict = null)
    {
        _definitions = new VariableDefinitionCollection(source);
        _scope = scope;
        _persistDefinitions = persistDefinitions;
        _getCurrentValue = getCurrentValue;
        _setCurrentValue = setCurrentValue;
        _removeCurrentValue = removeCurrentValue;
        _renameCurrentValue = renameCurrentValue;
        _isNameAvailable = isNameAvailable;
        _resolveAvailableName = resolveAvailableName;
        _onNameConflict = onNameConflict;
        _allowCurrentEditing = allowCurrentEditing;
        ShowCurrentValue = showCurrentValue;

        Reload();
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var definition in _definitions.Items)
            Items.Add(CreateItem(definition));
    }

    public void SetCurrentValueVisibility(bool visible)
    {
        ShowCurrentValue = visible;
        foreach (var item in Items)
            item.SetCurrentValueVisible(visible);
    }

    public void UpdateCurrentValue(string name, Variable variable)
    {
        var item = Items.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        item?.SetCurrentValue(variable);
    }

    [RelayCommand]
    public void AddVariable()
    {
        var index = _definitions.Count + 1;
        var name = GenerateUniqueName($"var_{_scope.ToString().ToLowerInvariant()}_{index}");
        var definition = _definitions.Add(name);
        var item = CreateItem(definition);
        Items.Add(item);
        _persistDefinitions();
    }

    [RelayCommand]
    private void RemoveVariable(VariableEditorItemViewModel? item)
    {
        if (item is null)
            return;

        _definitions.Remove(item.Definition);
        Items.Remove(item);
        _removeCurrentValue(item.Name);
        _persistDefinitions();
    }

    [RelayCommand]
    private void Reorder(ReorderRequest? request)
    {
        if (request?.Item is not VariableEditorItemViewModel item)
            return;
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0 || !_definitions.Move(item.Definition, request.NewIndex))
            return;

        Items.Move(oldIndex, request.NewIndex);
        _persistDefinitions();
    }

    private VariableEditorItemViewModel CreateItem(ProjectVariableDefinition definition)
    {
        var currentValue = _getCurrentValue(definition.Name);
        return new VariableEditorItemViewModel(
            definition,
            _scope,
            ShowCurrentValue,
            _allowCurrentEditing,
            currentValue,
            _isNameAvailable,
            _resolveAvailableName,
            OnItemChanged,
            OnItemRenamed,
            OnCurrentValueChanged,
            _onNameConflict);
    }

    private void OnItemChanged(VariableEditorItemViewModel item)
    {
        item.Definition.DefaultValue.Name = item.Name;
        _persistDefinitions();
    }

    private void OnItemRenamed(VariableEditorItemViewModel item, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        _renameCurrentValue(oldName, newName);
        item.Definition.DefaultValue.Name = newName;
        _persistDefinitions();
    }

    private void OnCurrentValueChanged(VariableEditorItemViewModel item, object value)
    {
        if (!ShowCurrentValue || !_allowCurrentEditing)
            return;

        _setCurrentValue(item.Name, value);
    }

    private string GenerateUniqueName(string baseName)
    {
        var root = VariableNameRules.Sanitize(baseName);
        var candidate = root;
        var suffix = 1;
        while (!_isNameAvailable(candidate, _scope))
            candidate = $"{root}_{suffix++}";

        return candidate;
    }
}

public static class VariableTypeValues
{
    public static IReadOnlyList<VariableType> All { get; } =
        [VariableType.Bool, VariableType.Int, VariableType.Float, VariableType.String];
}

public sealed partial class VariableEditorItemViewModel : ObservableValidator
{
    private readonly VariableScope _scope;
    private readonly bool _allowCurrentEditing;
    private readonly Func<string, VariableScope, bool> _isNameAvailable;
    private readonly Func<string, VariableScope, string> _resolveAvailableName;
    private readonly Action<VariableEditorItemViewModel> _onChanged;
    private readonly Action<VariableEditorItemViewModel, string, string> _onRenamed;
    private readonly Action<VariableEditorItemViewModel, object> _onCurrentValueChanged;
    private readonly Action<string>? _onNameConflict;

    private Variable _currentValue;
    private bool _reverting;
    private bool _settingType;

    public ProjectVariableDefinition Definition { get; }

    private string _name;

    [Required(ErrorMessage = "Variable name is required.")]
    [RegularExpression("^[A-Za-z0-9_]+$", ErrorMessage = "Only ASCII letters, digits, and underscore are allowed.")]
    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value, true))
                return;

            HandleNameChanged(value);
        }
    }

    [ObservableProperty]
    private bool _isCurrentValueVisible;

    public bool HasNameError => HasErrors;
    public string NameValidationMessage => GetValidationMessage(nameof(Name));

    public VariableType SelectedType
    {
        get => Definition.Type;
        set
        {
            if (_settingType || Definition.Type == value)
                return;

            _settingType = true;
            Definition.Type = value;
            Definition.DefaultValue.Name = Name;
            _currentValue = CreateTypedVariable(Name, value);
            OnPropertyChanged(nameof(SelectedType));
            RaiseValuePropertiesChanged();
            _onChanged(this);
            _onCurrentValueChanged(this, GetCurrentBoxedValue());
            _settingType = false;
        }
    }

    public bool CanEditCurrentValue => _allowCurrentEditing && IsCurrentValueVisible;
    public bool IsBool => SelectedType == VariableType.Bool;
    public bool IsInt => SelectedType == VariableType.Int;
    public bool IsFloat => SelectedType == VariableType.Float;
    public bool IsString => SelectedType == VariableType.String;

    public bool DefaultBoolValue
    {
        get => Definition.DefaultValue.AsBool();
        set
        {
            Definition.DefaultValue.SetValue(value);
            _onChanged(this);
        }
    }

    public decimal DefaultIntValue
    {
        get => Definition.DefaultValue.AsInt();
        set
        {
            Definition.DefaultValue.SetValue((int)value);
            _onChanged(this);
        }
    }

    public decimal DefaultFloatValue
    {
        get => (decimal)Definition.DefaultValue.AsFloat();
        set
        {
            Definition.DefaultValue.SetValue((float)value);
            _onChanged(this);
        }
    }

    public string DefaultStringValue
    {
        get => Definition.DefaultValue.AsString();
        set
        {
            Definition.DefaultValue.SetValue(value ?? string.Empty);
            _onChanged(this);
        }
    }

    public bool CurrentBoolValue
    {
        get => _currentValue.AsBool();
        set
        {
            _currentValue.SetValue(value);
            _onCurrentValueChanged(this, value);
        }
    }

    public decimal CurrentIntValue
    {
        get => _currentValue.AsInt();
        set
        {
            _currentValue.SetValue((int)value);
            _onCurrentValueChanged(this, (int)value);
        }
    }

    public decimal CurrentFloatValue
    {
        get => (decimal)_currentValue.AsFloat();
        set
        {
            _currentValue.SetValue((float)value);
            _onCurrentValueChanged(this, (float)value);
        }
    }

    public string CurrentStringValue
    {
        get => _currentValue.AsString();
        set
        {
            _currentValue.SetValue(value ?? string.Empty);
            _onCurrentValueChanged(this, value ?? string.Empty);
        }
    }

    public VariableEditorItemViewModel(
        ProjectVariableDefinition definition,
        VariableScope scope,
        bool showCurrentValue,
        bool allowCurrentEditing,
        Variable? currentValue,
        Func<string, VariableScope, bool> isNameAvailable,
        Func<string, VariableScope, string> resolveAvailableName,
        Action<VariableEditorItemViewModel> onChanged,
        Action<VariableEditorItemViewModel, string, string> onRenamed,
        Action<VariableEditorItemViewModel, object> onCurrentValueChanged,
        Action<string>? onNameConflict = null)
    {
        Definition = definition;
        _scope = scope;
        _allowCurrentEditing = allowCurrentEditing;
        _isNameAvailable = isNameAvailable;
        _resolveAvailableName = resolveAvailableName;
        _onChanged = onChanged;
        _onRenamed = onRenamed;
        _onCurrentValueChanged = onCurrentValueChanged;
        _onNameConflict = onNameConflict;
        _name = definition.Name;
        _currentValue = currentValue is null ? CreateTypedVariable(definition.Name, definition.Type) : CloneVariable(currentValue, definition.Name);
        _isCurrentValueVisible = showCurrentValue;
        ErrorsChanged += OnErrorsChanged;
        ValidateProperty(_name, nameof(Name));
        NotifyValidationStateChanged();
    }

    private void HandleNameChanged(string value)
    {
        if (_reverting)
            return;

        var oldName = Definition.Name;
        var sanitized = VariableNameRules.Sanitize(value, oldName);
        if (!string.Equals(value, sanitized, StringComparison.Ordinal))
        {
            Name = sanitized;
            return;
        }

        if (string.Equals(sanitized, oldName, StringComparison.Ordinal))
        {
            ClearErrors(nameof(Name));
            NotifyValidationStateChanged();
            return;
        }

        if (!_isNameAvailable(sanitized, _scope))
        {
            var resolved = _resolveAvailableName(sanitized, _scope);
            _reverting = true;
            Name = resolved;
            _reverting = false;
            ClearErrors(nameof(Name));
            NotifyValidationStateChanged();
            if (!string.Equals(resolved, oldName, StringComparison.Ordinal))
            {
                Definition.Name = resolved;
                Definition.DefaultValue.Name = resolved;
                _onRenamed(this, oldName, resolved);
            }
            _onNameConflict?.Invoke(sanitized);
            return;
        }

        // Valid rename — update definition without triggering restart
        ClearErrors(nameof(Name));
        NotifyValidationStateChanged();
        Definition.Name = sanitized;
        Definition.DefaultValue.Name = sanitized;
        _onRenamed(this, oldName, sanitized);
    }

    public void SetCurrentValueVisible(bool visible)
    {
        IsCurrentValueVisible = visible;
        OnPropertyChanged(nameof(CanEditCurrentValue));
    }

    public void SetCurrentValue(Variable variable)
    {
        _currentValue = CloneVariable(variable, Name);
        RaiseCurrentValuePropertiesChanged();
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Name))
            NotifyValidationStateChanged();
    }

    private void NotifyValidationStateChanged()
    {
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(NameValidationMessage));
    }

    private string GetValidationMessage(string propertyName) =>
        GetErrors(propertyName)
            .Cast<object>()
            .FirstOrDefault()?.ToString() ?? string.Empty;

    private void RaiseValuePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsBool));
        OnPropertyChanged(nameof(IsInt));
        OnPropertyChanged(nameof(IsFloat));
        OnPropertyChanged(nameof(IsString));
        OnPropertyChanged(nameof(DefaultBoolValue));
        OnPropertyChanged(nameof(DefaultIntValue));
        OnPropertyChanged(nameof(DefaultFloatValue));
        OnPropertyChanged(nameof(DefaultStringValue));
        RaiseCurrentValuePropertiesChanged();
        OnPropertyChanged(nameof(CanEditCurrentValue));
    }

    private void RaiseCurrentValuePropertiesChanged()
    {
        OnPropertyChanged(nameof(CurrentBoolValue));
        OnPropertyChanged(nameof(CurrentIntValue));
        OnPropertyChanged(nameof(CurrentFloatValue));
        OnPropertyChanged(nameof(CurrentStringValue));
    }

    private object GetCurrentBoxedValue() => SelectedType switch
    {
        VariableType.Bool => _currentValue.AsBool(),
        VariableType.Int => _currentValue.AsInt(),
        VariableType.Float => _currentValue.AsFloat(),
        _ => _currentValue.AsString()
    };

    private static Variable CreateTypedVariable(string name, VariableType type)
    {
        var variable = new Variable { Name = name };
        switch (type)
        {
            case VariableType.Bool:
                variable.SetValue(false);
                break;
            case VariableType.Int:
                variable.SetValue(0);
                break;
            case VariableType.Float:
                variable.SetValue(0f);
                break;
            default:
                variable.SetValue(string.Empty);
                break;
        }

        return variable;
    }

    private static Variable CloneVariable(Variable variable, string name)
    {
        var clone = new Variable { Name = name };
        switch (variable.Type)
        {
            case VariableType.Bool:
                clone.SetValue(variable.AsBool());
                break;
            case VariableType.Int:
                clone.SetValue(variable.AsInt());
                break;
            case VariableType.Float:
                clone.SetValue(variable.AsFloat());
                break;
            default:
                clone.SetValue(variable.AsString());
                break;
        }

        return clone;
    }
}
