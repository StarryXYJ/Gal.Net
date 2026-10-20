using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class CharacterFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, CharacterAuthoringService characters)
    {
        var input = new TextBox { Name = "CharacterInput" };
        var choices = new ListBox { Name = "CharacterChoices", MaxHeight = 240, MinWidth = 260 };
        var border = new Border { Child = choices, BorderThickness = new(1) };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("SemiColorBackground0");
        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("SemiColorBorder");
        var popup = new Popup { PlacementTarget = input, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Child = border };
        var panel = new StackPanel { Children = { input, popup } };
        var lifetime = new CancellationTokenSource();
        var rows = characters.Rows();
        var disposed = false; var updating = false; var busy = false; var dirty = false;
        bool Composing() => input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        string CurrentKey() => context.Value.Json is { } json ? json.ValueKind == JsonValueKind.String ? json.GetString() ?? "" : json.GetRawText() : "";
        void Filter()
        {
            if (disposed) return;
            if (!context.IsEnabled) { popup.IsOpen = false; return; }
            var search = input.Text ?? "";
            choices.ItemsSource = rows.Where(row => row.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(50).ToArray();
            popup.IsOpen = input.IsFocused && choices.ItemCount > 0 && !busy;
        }
        void Refresh()
        {
            if (disposed) return;
            input.IsEnabled = context.IsEnabled && !busy;
            input.PlaceholderText = characters.Text["Characters.SpeakerHint"];
            if (!dirty && !context.HasDraft)
            {
                updating = true;
                try { input.Text = context.IsEnabled ? rows.FirstOrDefault(row => row.Id == CurrentKey())?.DisplayName ?? CurrentKey() : CurrentKey(); }
                finally { updating = false; }
            }
        }
        async Task Commit(string? selected = null)
        {
            if (disposed || busy || !context.IsEnabled || Composing() || !dirty && selected is null) return;
            busy = true; popup.IsOpen = false; Refresh();
            try
            {
                var value = selected ?? (string.IsNullOrWhiteSpace(input.Text) ? "" : input.Text);
                if (await context.CommitAsync(new(JsonSerializer.SerializeToElement(value)), lifetime.Token))
                { rows = characters.Rows(); dirty = false; context.RevertDraft(); }
            }
            finally { busy = false; Refresh(); }
        }
        void QueueCommit() => Dispatcher.UIThread.Post(async () =>
        {
            if (!disposed && !input.IsKeyboardFocusWithin && !choices.IsKeyboardFocusWithin) await Commit();
        }, DispatcherPriority.Background);
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        void SourceChanged() { if (!disposed && context.IsEnabled) { rows = characters.Rows(); Refresh(); if (input.IsFocused) Filter(); } }
        // TextChanged is deferred; a programmatic refresh would outlive the updating guard.
        input.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty && !updating && !disposed)
            { dirty = true; context.Text = input.Text ?? ""; Filter(); if (!input.IsFocused) QueueCommit(); }
        };
        input.GotFocus += (_, _) => Filter(); input.LostFocus += (_, _) => QueueCommit(); choices.LostFocus += (_, _) => QueueCommit();
        input.TextInput += (_, _) => { if (!input.IsFocused) QueueCommit(); };
        input.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && !Composing()) { args.Handled = true; await Commit(); }
            else if (args.Key == Key.Escape) { args.Handled = true; popup.IsOpen = false; dirty = false; context.RevertDraft(); Refresh(); }
            else if (args.Key == Key.Down && choices.ItemCount > 0) { args.Handled = true; popup.IsOpen = true; choices.SelectedIndex = 0; choices.Focus(); }
        };
        choices.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && choices.SelectedItem is CharacterRow row) { args.Handled = true; await Commit(row.Id); input.Focus(); popup.IsOpen = false; }
            else if (args.Key == Key.Escape) { args.Handled = true; dirty = false; context.RevertDraft(); Refresh(); input.Focus(); popup.IsOpen = false; }
        };
        choices.PointerReleased += async (_, args) =>
        {
            if ((args.Source as Control)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is CharacterRow row)
            { await Commit(row.Id); input.Focus(); popup.IsOpen = false; }
        };
        context.PropertyChanged += Changed; characters.Text.PropertyChanged += Changed; characters.Changed += SourceChanged;
        Refresh();
        return new(panel, () => { disposed = true; popup.IsOpen = false; lifetime.Cancel(); lifetime.Dispose(); context.PropertyChanged -= Changed; characters.Text.PropertyChanged -= Changed; characters.Changed -= SourceChanged; });
    }
}
