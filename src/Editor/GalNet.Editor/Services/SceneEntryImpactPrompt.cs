using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public static class SceneEntryImpactPrompt
{
    public static Task<bool> ConfirmAsync(SceneEditImpact impact, IEditorLocalizationService? text, Func<SceneAuthorAddress, string>? describe = null)
    {
        if (!impact.RequiresReview) return Task.FromResult(true);
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(window => window.IsActive);
        return owner is null || text is null ? Task.FromResult(false) : CreateDialog(impact, text, describe).ShowDialog<bool>(owner);
    }
    public static Window CreateDialog(SceneEditImpact impact, IEditorLocalizationService text, Func<SceneAuthorAddress, string>? describe = null)
    {
        var bindings = new List<IDisposable>();
        var dialog = new Window { Width = 680, Height = 420, MinWidth = 420, MinHeight = 240, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        bindings.Add(LocalizedBindings.Bind(dialog, Window.TitleProperty, text, "Scene.Impact.Title"));
        var message = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        bindings.Add(LocalizedBindings.Bind(message, TextBlock.TextProperty, text, "Scene.Impact.Message"));
        var rows = new StackPanel { Spacing = 8 };
        foreach (var change in impact.References)
        {
            var label = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            void Render()
            {
                var field = text[change.Reference.Slot.Domain == SceneIdentityDomain.Playback ? "Scene.Playback" : "Scene.Target"];
                if (change.Reference.Position is { } position) field += " · " + text["Timeline.Frame"] + " " + position.Frame;
                label.Text = (describe?.Invoke(change.Reference.Owner) ?? change.Reference.Owner.GroupId + " / " + change.Reference.Owner.EntryId) + " · " + field + "\n" + text["Scene.Diagnostic." + change.After.Code];
            }
            Render();
            System.ComponentModel.PropertyChangedEventHandler changed = (_, _) => Render();
            text.PropertyChanged += changed;
            bindings.Add(new CallbackDisposable(() => text.PropertyChanged -= changed));
            rows.Children.Add(label);
        }
        var groups = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        void RenderGroups() => groups.Text = text.Format("Scene.Impact.Groups", impact.ChangedGroups.Count);
        RenderGroups(); rows.Children.Add(groups);
        System.ComponentModel.PropertyChangedEventHandler groupChanged = (_, _) => RenderGroups();
        text.PropertyChanged += groupChanged; bindings.Add(new CallbackDisposable(() => text.PropertyChanged -= groupChanged));
        if (impact.ArchivedInputs > 0)
        {
            var inputs = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            bindings.Add(LocalizedBindings.Bind(inputs, TextBlock.TextProperty, text, "Scene.Impact.Inputs", impact.ArchivedInputs)); rows.Children.Add(inputs);
        }
        if (!impact.Complete)
        {
            var incomplete = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            bindings.Add(LocalizedBindings.Bind(incomplete, TextBlock.TextProperty, text, "Scene.Impact.Incomplete")); rows.Children.Add(incomplete);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (key, accept) in new[] { ("Common.Cancel", false), ("Scene.Impact.Continue", true) })
        {
            var button = new Button { Name = accept ? "CommitEntryEdit" : "CancelEntryEdit" };
            bindings.Add(LocalizedBindings.Bind(button, ContentControl.ContentProperty, text, key));
            button.Click += (_, _) => dialog.Close(accept); buttons.Children.Add(button);
        }
        var layout = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 16 };
        layout.Children.Add(message);
        var scroll = new ScrollViewer { Content = rows }; Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons); dialog.Content = layout;
        dialog.Closed += (_, _) => { foreach (var binding in bindings) binding.Dispose(); };
        return dialog;
    }
    private sealed class CallbackDisposable(Action action) : IDisposable { public void Dispose() => action(); }
}
