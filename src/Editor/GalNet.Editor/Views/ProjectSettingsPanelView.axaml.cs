using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class ProjectSettingsPanelView : UserControl
{
    private ProjectSettingsPanelViewModel? _observed;
    public ProjectSettingsPanelView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        DataContextChanged += (_, _) => ObserveDraft();
        AttachedToVisualTree += (_, _) => ObserveDraft();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_observed is not null) _observed.DraftReset -= ResetTextUndo;
            _observed = null;
        };
    }

    private void ObserveDraft()
    {
        if (ReferenceEquals(_observed, DataContext)) return;
        if (_observed is not null) _observed.DraftReset -= ResetTextUndo;
        _observed = DataContext as ProjectSettingsPanelViewModel;
        if (_observed is not null) _observed.DraftReset += ResetTextUndo;
    }

    private void ResetTextUndo()
    {
        foreach (var input in this.GetVisualDescendants().OfType<TextBox>())
        {
            var enabled = input.IsUndoEnabled;
            input.IsUndoEnabled = false;
            input.IsUndoEnabled = enabled;
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || DataContext is not ProjectSettingsPanelViewModel viewModel) return;
        if (args.Key == Key.Escape)
        {
            viewModel.CancelDraftCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.None)
        {
            args.Handled = true;
            try { await viewModel.CommitDraftAsync(); }
            catch (System.IO.InvalidDataException) { }
        }
    }
}
