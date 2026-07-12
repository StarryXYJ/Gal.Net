using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using GalNet.Core.Variable;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;
public partial class BranchOptionListEditorControl : ReorderableListControl
{
    public static readonly StyledProperty<IEnumerable<ConditionVariableSuggestion>?> ConditionSuggestionsProperty = AvaloniaProperty.Register<BranchOptionListEditorControl, IEnumerable<ConditionVariableSuggestion>?>(nameof(ConditionSuggestions));
    public static readonly StyledProperty<IEnumerable<ProjectVariableDefinition>?> ValidationVariablesProperty = AvaloniaProperty.Register<BranchOptionListEditorControl, IEnumerable<ProjectVariableDefinition>?>(nameof(ValidationVariables));
    public IEnumerable<ConditionVariableSuggestion>? ConditionSuggestions { get => GetValue(ConditionSuggestionsProperty); set => SetValue(ConditionSuggestionsProperty, value); }
    public IEnumerable<ProjectVariableDefinition>? ValidationVariables { get => GetValue(ValidationVariablesProperty); set => SetValue(ValidationVariablesProperty, value); }
    public BranchOptionListEditorControl() { InitializeComponent(); InitializeDragDrop(ItemsListBox); }
}
