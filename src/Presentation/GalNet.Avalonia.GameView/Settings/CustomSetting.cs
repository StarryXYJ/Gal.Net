using GalNet.Game.Controls.Forms;

namespace GalNet.Avalonia.GameView.Settings;

public sealed record SettingEditorContext<T>(SettingItem<T> Setting);
public interface ICustomSetting
{
    FieldEditorLease CreateEditor();
}

public sealed class CustomSetting<T>(string id, string nameKey, T defaultValue, Func<T> read,
    Func<T, CancellationToken, Task> apply, Func<SettingEditorContext<T>, FieldEditorLease> editorFactory,
    object? label = null, Func<T>? forcedValue = null)
    : SettingItem<T>(id, nameKey, defaultValue, read, apply, label, forcedValue), ICustomSetting
{
    public FieldEditorLease CreateEditor() => editorFactory(new(this));
}
