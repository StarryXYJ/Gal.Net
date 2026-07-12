using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using GalNet.Core.Variable;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;
public partial class BranchConditionListEditorControl : ReorderableListControl
{
    public static readonly StyledProperty<IEnumerable<ConditionVariableSuggestion>?> ConditionSuggestionsProperty = AvaloniaProperty.Register<BranchConditionListEditorControl, IEnumerable<ConditionVariableSuggestion>?>(nameof(ConditionSuggestions));
    public static readonly StyledProperty<IEnumerable<ProjectVariableDefinition>?> ValidationVariablesProperty = AvaloniaProperty.Register<BranchConditionListEditorControl, IEnumerable<ProjectVariableDefinition>?>(nameof(ValidationVariables));
    public IEnumerable<ConditionVariableSuggestion>? ConditionSuggestions { get => GetValue(ConditionSuggestionsProperty); set => SetValue(ConditionSuggestionsProperty, value); }
    public IEnumerable<ProjectVariableDefinition>? ValidationVariables { get => GetValue(ValidationVariablesProperty); set => SetValue(ValidationVariablesProperty, value); }
    public BranchConditionListEditorControl() { InitializeComponent(); InitializeDragDrop(ItemsListBox); }
}
