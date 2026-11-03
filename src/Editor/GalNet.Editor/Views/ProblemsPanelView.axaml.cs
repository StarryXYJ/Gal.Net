using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class ProblemsPanelView : UserControl
{
    public ProblemsPanelView() => InitializeComponent();
    private void OnProblemDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (args.Source is not Visual visual || visual.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
        if (visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is AuthorProblem problem
            && DataContext is ProblemsPanelViewModel vm) vm.NavigateCommand.Execute(problem);
    }
    private void OnProblemKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || sender is not ListBox { SelectedItem: AuthorProblem problem } || DataContext is not ProblemsPanelViewModel vm) return;
        vm.NavigateCommand.Execute(problem); args.Handled = true;
    }
}
