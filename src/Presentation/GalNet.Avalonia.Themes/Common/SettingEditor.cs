using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace GalNet.Avalonia.Themes.Common;

/// <summary>Checks theme/host overrides before falling back to the built-in setting templates.</summary>
public sealed class SettingEditor : ContentControl
{
    public static readonly StyledProperty<IReadOnlyList<IDataTemplate>?> EditorTemplatesProperty =
        AvaloniaProperty.Register<SettingEditor, IReadOnlyList<IDataTemplate>?>(nameof(EditorTemplates));
    public IReadOnlyList<IDataTemplate>? EditorTemplates
    {
        get => GetValue(EditorTemplatesProperty);
        set => SetValue(EditorTemplatesProperty, value);
    }
    protected override Type StyleKeyOverride => typeof(ContentControl);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty || change.Property == EditorTemplatesProperty)
            ContentTemplate = EditorTemplates?.FirstOrDefault(template => template.Match(Content));
    }
}
