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
        DataContextChanged += (_, _) => Connect();
        AttachedToVisualTree += (_, _) => Connect();
        DetachedFromVisualTree += (_, _) => Disconnect();
    }
    private void Disconnect() { if (_model is not null) _model.PropertyChanged -= Changed; _model = null; }
    private void Connect() { Disconnect(); _model = DataContext as EntryInspectorViewModel; if (_model is not null) _model.PropertyChanged += Changed; }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(EntryInspectorViewModel.RequestedField)) return;
        Dispatcher.UIThread.Post(() =>
        {
            var row = this.GetVisualDescendants().OfType<FieldEditorRow>().FirstOrDefault(item => item.Context?.Field.Id == _model?.RequestedField);
            if (row is null) return;
            row.BringIntoView(); row.GetVisualDescendants().OfType<Avalonia.Input.InputElement>().FirstOrDefault(input => input.Focusable && input.IsEffectivelyVisible)?.Focus();
        }, DispatcherPriority.Loaded);
    }
}
