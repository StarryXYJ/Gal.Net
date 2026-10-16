using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public static class LocalizedBindings
{
    public static IDisposable Bind<T>(AvaloniaObject target, AvaloniaProperty<T> property, IEditorLocalizationService localization,
        string key, params object[] arguments) => target.Bind(property, new ReflectionBinding(nameof(IEditorLocalizationService.CurrentCulture))
        {
            Source = localization,
            Converter = new FuncValueConverter<object?, string>(_ => localization.Format(key, arguments))
        });
}
