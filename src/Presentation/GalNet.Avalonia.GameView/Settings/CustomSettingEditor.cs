using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace GalNet.Avalonia.GameView.Settings;

/// <summary>A custom editor owns a fresh lease only while it belongs to the visual tree.</summary>
public sealed class CustomSettingEditor : ContentControl
{
    private SettingEditorLease? _lease;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RecreateEditor();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseEditor();
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.IsAttachedToVisualTree()) RecreateEditor();
    }
    private void RecreateEditor()
    {
        ReleaseEditor();
        if (DataContext is not ICustomSetting setting) return;
        _lease = setting.CreateEditor();
        Content = _lease.Control;
    }
    private void ReleaseEditor()
    {
        Content = null;
        _lease?.Dispose();
        _lease = null;
    }
}
