using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.ComponentModel;

namespace GalNet.Control.Abstraction.UI;

/// <summary>Inherited palette service for a rendered screen tree.</summary>
public sealed class PaletteScope : AvaloniaObject
{
    public static readonly AttachedProperty<IColorPalette?> PaletteProperty =
        AvaloniaProperty.RegisterAttached<PaletteScope, StyledElement, IColorPalette?>(
            "Palette", inherits: true);

    public static void SetPalette(AvaloniaObject element, IColorPalette? value) =>
        element.SetValue(PaletteProperty, value);

    public static IColorPalette? GetPalette(AvaloniaObject element) =>
        element.GetValue(PaletteProperty);
}

/// <summary>
/// Binds an Avalonia brush property to a named entry in the inherited palette.
/// Use as <c>Background="{ui:PaletteBrush surface}"</c>.
/// </summary>
public sealed class PaletteBrushExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var target = (serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget)?.TargetObject;
        return target is StyledElement element
            ? PaletteBinding.Create(element, Key)
            : new Binding { FallbackValue = AvaloniaProperty.UnsetValue };
    }
}

/// <summary>Creates palette bindings for generated controls as well as AXML.</summary>
public static class PaletteBinding
{
    public static Binding Create(StyledElement element, string key) => new(nameof(PaletteBrushProxy.Value))
    {
        Source = new PaletteBrushProxy(element, key),
        Mode = BindingMode.OneWay
    };

    private sealed class PaletteBrushProxy : INotifyPropertyChanged
    {
        private readonly string _key;
        private IColorPalette? _palette;
        public PaletteBrushProxy(StyledElement element, string key)
        {
            _key = key;
            element.GetObservable(PaletteScope.PaletteProperty).Subscribe(new ActionObserver<IColorPalette?>(SetPalette));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public IBrush? Value => _palette?[_key];
        private void SetPalette(IColorPalette? palette)
        {
            if (ReferenceEquals(_palette, palette)) return;
            if (_palette is not null) _palette.PropertyChanged -= PaletteChanged;
            _palette = palette;
            if (_palette is not null) _palette.PropertyChanged += PaletteChanged;
            OnChanged();
        }
        private void PaletteChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "Item[]" or null) OnChanged();
        }
        private void OnChanged() => PropertyChanged?.Invoke(this, new(nameof(Value)));
    }

    private sealed class ActionObserver<T>(Action<T> next) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => next(value);
    }
}
