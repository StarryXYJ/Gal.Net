using Avalonia.Controls;

namespace GalNet.Game.Controls.Forms;

/// <summary>A fresh control and its subscriptions. Release is invoked at most once, even if it fails.</summary>
public sealed class FieldEditorLease(Control control, Action? release = null) : IDisposable
{
    private Action? _release = release;
    public Control Control { get; } = control ?? throw new ArgumentNullException(nameof(control));
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
