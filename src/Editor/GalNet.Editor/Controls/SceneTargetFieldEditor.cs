using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class SceneTargetFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, SceneTargetCatalog catalog)
    {
        var button = new Button { Name = "SceneTargetField", HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var flyout = new Flyout(); button.Flyout = flyout;
        var lifetime = new CancellationTokenSource(); var disposed = false; var opening = 0; FieldValue before = default;
        SceneTargetSelection? selection = null; SceneTargetPicker? picker = null;
        void Refresh()
        {
            if (disposed) return;
            button.IsEnabled = context.IsEnabled;
            var current = context.Options.FirstOrDefault(option => context.EffectiveValue is { } value && JsonElement.DeepEquals(value, option.Value));
            var empty = context.EffectiveValue is not { ValueKind: JsonValueKind.String } text || string.IsNullOrWhiteSpace(text.GetString());
            var choice = current?.Label as SceneFieldChoice;
            button.Content = choice?.EmptyLabelKey is { } key ? catalog.Text[key]
                : choice?.Choice is { } target ? catalog.Query(choice.Snapshot.GroupId, choice.Snapshot.EntryId).Choices.FirstOrDefault(item => item.Item.Id == target.Item.Id)?.Item.Label ?? catalog.Text["Scene.Unresolved"]
                : catalog.Text[empty ? "Scene.None" : "Scene.Unresolved"];
            picker?.Refresh();
        }
        async void Opened(object? sender, EventArgs args)
        {
            var version = ++opening; context.RevertDraft(); before = context.Value;
            if (context.Options.Select(option => option.Label).OfType<SceneFieldChoice>().FirstOrDefault() is not { } origin) return;
            selection?.Dispose(); picker?.Dispose();
            selection = new(catalog, origin.Snapshot, origin.EmptyLabelKey ?? "Scene.None");
            var current = context.Options.FirstOrDefault(option => context.EffectiveValue is { } value && JsonElement.DeepEquals(value, option.Value));
            if (current is not null) selection.Select(current.Id);
            var active = selection; picker = new() { Width = 500, Height = 420, Margin = new(10) }; picker.Configure(active); flyout.Content = picker;
            picker.Cancelled += () => flyout.Hide();
            picker.Confirmed += Confirm;
            try
            {
                await active.PrepareAsync();
                if (disposed || version != opening || !context.IsEnabled || !ReferenceEquals(selection, active)) return;
                picker.Refresh(); Dispatcher.UIThread.Post(() => { if (!disposed && version == opening) picker?.ResourcePanel.Search.Focus(); });
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!disposed && version == opening && picker is not null) picker.ResourcePanel.Hint = catalog.Text["Scene.LoadFailed"]; }
        }
        async void Confirm()
        {
            if (disposed || selection is null) return;
            context.RevertDraft();
            bool Same(FieldValue left, FieldValue right) => left.IsProvided == right.IsProvided && (!left.IsProvided || JsonElement.DeepEquals(left.Json!.Value, right.Json!.Value));
            if (!context.IsEnabled || !Same(before, context.Value) || !selection.TryResolve(out var handle))
            { picker?.ShowStale(); return; }
            if (await context.CommitAsync(new(JsonSerializer.SerializeToElement(handle)), lifetime.Token) && !disposed) flyout.Hide();
        }
        void Changed(object? sender, PropertyChangedEventArgs args) { if (!disposed) Refresh(); }
        flyout.Opened += Opened;
        flyout.Closed += (_, _) => { opening++; picker?.Dispose(); picker = null; selection?.Dispose(); selection = null; flyout.Content = null; };
        context.PropertyChanged += Changed; catalog.Text.PropertyChanged += Changed; Refresh();
        return new(button, () =>
        {
            disposed = true; opening++; lifetime.Cancel(); flyout.Hide(); lifetime.Dispose();
            context.PropertyChanged -= Changed; catalog.Text.PropertyChanged -= Changed; flyout.Opened -= Opened; picker?.Dispose(); selection?.Dispose();
        });
    }
}
