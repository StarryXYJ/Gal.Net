using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Inspector.Views;

public partial class EntryInspectorView : UserControl
{
    private EntryInspectorViewModel? _model;
    public EntryInspectorView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ShaderPreviewPanel.MaxHeight = Math.Max(40, Bounds.Height * 0.5);
        DataContextChanged += (_, _) => Connect();
        AttachedToVisualTree += (_, _) => Connect();
        DetachedFromVisualTree += (_, _) => Disconnect();
    }
    private void Disconnect() { if (_model is not null) _model.PropertyChanged -= Changed; _model = null; }
    private void Connect() { Disconnect(); _model = DataContext as EntryInspectorViewModel; if (_model is not null) { _model.PropertyChanged += Changed; FocusRequestedField(); } }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(EntryInspectorViewModel.RequestedField)) return;
        FocusRequestedField();
    }
    private void FocusRequestedField()
    {
        var model = _model;
        var entryId = model?.Entry?.StableId;
        if (model?.RequestedField is not { } field) return;
        var path = model.RequestedFieldPath;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(model, _model) || model.Entry?.StableId != entryId || model.RequestedField != field || !ReferenceEquals(path, model.RequestedFieldPath)) return;
            if (field == "condition")
            {
                EntryConditionInput.BringIntoView();
                EntryConditionInput.GetVisualDescendants().OfType<Avalonia.Input.InputElement>().FirstOrDefault(input => input.Focusable)?.Focus();
                return;
            }
            var row = this.GetVisualDescendants().OfType<FieldEditorRow>().FirstOrDefault(item => item.Context?.Field.Id == field);
            if (row is null) return;
            foreach (var expander in row.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
            row.BringIntoView();
            var editor = row.Editor;
            if (editor is not null && path is { Count: > 0 })
            {
                var matches = editor.GetVisualDescendants().OfType<Control>()
                    .Where(control => control is NumericUpDown or TextBox or ComboBox or CheckBox && control.DataContext is FieldEditorContext context &&
                        string.Equals(context.Field.Id, path[^1], StringComparison.OrdinalIgnoreCase))
                    .GroupBy(control => control.DataContext).ToArray();
                if (matches.Length == 1) { editor = matches[0].First(); editor.BringIntoView(); }
            }
            var input = editor as Avalonia.Input.InputElement;
            if (input?.Focusable != true) input = editor?.GetVisualDescendants().OfType<Avalonia.Input.InputElement>().FirstOrDefault(input => input.Focusable && input.IsEffectivelyVisible);
            input?.Focus();
        }, DispatcherPriority.Loaded);
    }
}
