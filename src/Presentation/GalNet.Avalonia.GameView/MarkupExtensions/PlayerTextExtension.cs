using Avalonia.Data;

namespace GalNet.Avalonia.GameView.MarkupExtensions;

/// <summary>Host supplies observable text values; shared pages retain English fallbacks.</summary>
public sealed class PlayerTextExtension(string key, string fallback)
{
    public static Func<string, object>? Resolve { get; set; }

    public object ProvideValue(IServiceProvider serviceProvider) => CreateBinding(key, fallback);

    internal static object CreateBinding(string key, string fallback, string? stringFormat = null) => Resolve is { } resolve
        ? new ReflectionBinding("Value") { Source = resolve(key), Mode = BindingMode.OneWay, StringFormat = stringFormat }
        : fallback;
}
