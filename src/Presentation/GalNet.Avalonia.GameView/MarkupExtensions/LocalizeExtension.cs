namespace GalNet.Avalonia.GameView.MarkupExtensions;

/// <summary>Creates a live UI text binding using the host's <see cref="PlayerTextExtension.Resolve"/> adapter.</summary>
public sealed class LocalizeExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public string? Key { get; set; }

    public string? StringFormat { get; set; }

    public object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key)) return string.Empty;

        return PlayerTextExtension.CreateBinding(Key, Key, StringFormat);
    }
}
