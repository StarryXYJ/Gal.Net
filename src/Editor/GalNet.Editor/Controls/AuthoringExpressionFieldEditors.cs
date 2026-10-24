using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Variable;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Completion and expression feedback with the shared, project-owned input lifecycle.</summary>
internal static class AuthoringExpressionFieldEditors
{
    public static FieldEditorLease Expression(FieldEditorContext context,
        Func<IReadOnlyList<ConditionVariableSuggestion>> suggestions, Func<IReadOnlyList<ProjectVariableDefinition>> variables)
    {
        var control = new ConditionEditorControl { Name = "ExpressionInput" };
        return Bind(control, context, ConditionEditorControl.TextProperty, () => control.Text, value => control.Text = value,
            () => { control.Suggestions = suggestions(); control.ValidationVariables = variables(); });
    }

    public static FieldEditorLease Variable(FieldEditorContext context, Func<IReadOnlyList<string>> suggestions)
    {
        var control = new ComboBox { Name = "VariableNameInput", IsEditable = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        return Bind(control, context, ComboBox.TextProperty, () => control.Text ?? "", value => control.Text = value,
            () => control.ItemsSource = suggestions());
    }

    private static FieldEditorLease Bind(Control control, FieldEditorContext context, AvaloniaProperty property,
        Func<string> read, Action<string> write, Action completion)
    {
        var syncing = false; var disposed = false;
        bool Composing() => control.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        var guard = context.DraftState?.RegisterGuard(() => !Composing());
        void Refresh()
        {
            if (disposed) return;
            syncing = true;
            try { completion(); write(context.Text); control.IsEnabled = context.IsEnabled; }
            finally { syncing = false; }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        void Edited(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (!syncing && args.Property == property) context.Text = read(); }
        void LostFocus(object? sender, RoutedEventArgs args)
        {
            Dispatcher.UIThread.Post(async () => { if (!disposed && !control.IsKeyboardFocusWithin && !Composing()) await context.CommitTextAsync(); }, DispatcherPriority.Background);
        }
        void KeyDown(object? sender, KeyEventArgs args)
        {
            if (args.Handled || Composing()) return;
            if (args.Key == Key.Escape) { context.RevertDraft(); args.Handled = true; }
            else if (args.Key == Key.Enter) { _ = context.CommitTextAsync(); args.Handled = true; }
        }
        context.PropertyChanged += Changed; control.PropertyChanged += Edited;
        control.LostFocus += LostFocus; control.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Bubble);
        Refresh();
        return new(control, () =>
        {
            disposed = true; guard?.Dispose(); context.PropertyChanged -= Changed; control.PropertyChanged -= Edited;
            control.LostFocus -= LostFocus; control.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        });
    }
}
