using Avalonia.Controls;

namespace GalNet.Avalonia.GameView.Settings;

public sealed record SettingEditorContext<T>(SettingItem<T> Setting);
public interface ICustomSetting
{
    SettingEditorLease CreateEditor();
}
public sealed class SettingEditorLease(Control control, Action? release = null) : IDisposable
{
    private Action? _release = release;
    public Control Control { get; } = control;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

public sealed class CustomSetting<T>(string id, string nameKey, T defaultValue, Func<T> read,
    Func<T, CancellationToken, Task> apply, Func<SettingEditorContext<T>, SettingEditorLease> editorFactory,
    object? label = null, Func<T>? forcedValue = null)
    : SettingItem<T>(id, nameKey, defaultValue, read, apply, label, forcedValue), ICustomSetting
{
    public SettingEditorLease CreateEditor() => editorFactory(new(this));
}
