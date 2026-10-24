using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class PropertyEditorSupport
{
    public static string Text(string key) => EditorLocalizationService.Current?[key] ?? key;
    public static IDisposable Localize(Action refresh)
    {
        var text = EditorLocalizationService.Current;
        void Changed(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") refresh(); }
        if (text is not null) text.PropertyChanged += Changed;
        refresh();
        return new Subscription(() => { if (text is not null) text.PropertyChanged -= Changed; });
    }
    public static (Button Button, IDisposable Lifetime) Repair(FieldEditorContext context)
    {
        var button = new Button { Name = "RepairProperty", Content = "⋯" };
        var flyout = new Flyout(); button.Flyout = flyout;
        FieldEditorLease? lease = null;
        flyout.Opened += (_, _) =>
        {
            lease?.Dispose(); context.RevertDraft();
            lease = DefaultFieldEditors.CreateJson(context);
            lease.Control.Width = 400; lease.Control.MaxHeight = 360;
            flyout.Content = lease.Control;
        };
        flyout.Closed += (_, _) => { context.RevertDraft(); lease?.Dispose(); lease = null; flyout.Content = null; };
        var locale = Localize(() => { var text = Text("Form.Repair"); ToolTip.SetTip(button, text); AutomationProperties.SetName(button, text); });
        return (button, new Subscription(() => { flyout.Hide(); lease?.Dispose(); locale.Dispose(); }));
    }
    internal sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
