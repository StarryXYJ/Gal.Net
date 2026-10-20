using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class GameTextFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, GameTextAuthoringService texts)
    {
        var multiline = context.Field.Editor?.EditorId.Contains("multiline", StringComparison.OrdinalIgnoreCase) == true;
        var input = new TextBox { Name = "GameTextInput", AcceptsReturn = multiline, TextWrapping = multiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap, MinHeight = multiline ? 80 : 0 };
        var hint = new TextBlock { Name = "GameTextHint", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 };
        var panel = new StackPanel { Children = { input, hint }, Spacing = 4 };
        var disposed = false; var updating = false; var dirty = false; var busy = false;
        LocalizationTextTarget? target = null;
        JsonElement? original = null;
        string originalText = "";
        string? cachedKey = null, cachedText = null;
        var cachedUses = 0;
        string Key() => context.Value.Json is { ValueKind: JsonValueKind.String } json ? json.GetString() ?? "" : "";
        string Owner() => (context.HostContext as IEntryFieldOrigin)?.EntryId ?? Guid.NewGuid().ToString("N");
        bool Composing() => input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        void ReadSource() { cachedKey = Key(); cachedText = texts.Read(cachedKey); cachedUses = texts.Uses(cachedKey); }
        void Refresh()
        {
            if (disposed) return;
            input.IsEnabled = context.IsEnabled && !busy;
            var key = Key(); if (key != cachedKey) ReadSource(); var value = cachedText; var uses = cachedUses;
            input.PlaceholderText = key.Length > 0 && value is null ? texts.Text["GameText.Missing"] : texts.Text["GameText.Placeholder"];
            hint.Text = key.Length > 0 && value is null ? texts.Text["GameText.Missing"] : uses > 1 ? texts.Text.Format("GameText.Shared", uses) : "";
            hint.IsVisible = hint.Text.Length > 0;
            if (!dirty)
            {
                updating = true;
                try { input.Text = context.Value.Json is { ValueKind: not (JsonValueKind.String or JsonValueKind.Null) } invalid ? invalid.GetRawText() : value ?? ""; }
                finally { updating = false; }
            }
        }
        async Task Commit(bool force = false)
        {
            if (disposed || busy || !dirty && !force || !context.IsEnabled || Composing()) return;
            if (!dirty) original = context.Value.Json?.Clone();
            if (original is { } previous ? context.Value.Json is not { } now || !JsonElement.DeepEquals(previous, now) : context.Value.Json is not null)
            { context.ReportIssue(new("text.changed", texts.Text["Error.text.changed"])); return; }
            busy = true;
            GameTextDraft? draft = null;
            try
            {
                draft = texts.Register(target ?? texts.Capture(Key(), Owner()), input.Text ?? "");
                if (await context.CommitAsync(new(JsonSerializer.SerializeToElement(draft.Token)))) { dirty = false; target = null; context.RevertDraft(); }
            }
            catch (Exception error) { context.ReportIssue(new("field.commit", EditorText.Error(error).Render(texts.Text))); }
            finally { if (draft is not null) texts.Release(draft); busy = false; Refresh(); }
        }
        input.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty || updating || disposed) return;
            var wasDirty = dirty; dirty = true;
            if (!wasDirty)
            {
                original = context.Value.Json?.Clone(); originalText = cachedText ?? "";
                try { target = texts.Capture(Key(), Owner()); }
                catch (Exception error) { context.ReportIssue(new("field.commit", EditorText.Error(error).Render(texts.Text))); }
            }
            if ((input.Text ?? "") == originalText) { dirty = false; target = null; context.RevertDraft(); Refresh(); return; }
            context.Text = input.Text ?? "";
        };
        input.LostFocus += (_, _) => Dispatcher.UIThread.Post(async () => { if (!disposed && !input.IsFocused) await Commit(); }, DispatcherPriority.Background);
        input.TextInput += (_, _) => { if (!input.IsFocused) Dispatcher.UIThread.Post(async () => { if (!disposed) await Commit(); }, DispatcherPriority.Background); };
        input.AddHandler(InputElement.KeyDownEvent, async (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Escape && !Composing()) { args.Handled = true; dirty = false; target = null; context.RevertDraft(); Refresh(); }
            else if (args.Key == Avalonia.Input.Key.Enter && (!multiline || args.KeyModifiers.HasFlag(KeyModifiers.Control)) && !Composing()) { args.Handled = true; await Commit(force: true); }
        }, RoutingStrategies.Tunnel);
        void Changed(object? sender, PropertyChangedEventArgs args) { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
        void SourceChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(SourceChanged); return; }
            if (!disposed) { ReadSource(); Refresh(); }
        }
        context.PropertyChanged += Changed; texts.Changed += SourceChanged; texts.Text.PropertyChanged += Changed;
        Refresh();
        return new(panel, () => { disposed = true; context.PropertyChanged -= Changed; texts.Changed -= SourceChanged; texts.Text.PropertyChanged -= Changed; });
    }
}
