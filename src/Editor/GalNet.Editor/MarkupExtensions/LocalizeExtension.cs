using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;

namespace GalNet.Editor.MarkupExtensions;

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
    public string? Path { get; set; }
    public string? FallbackPath { get; set; }

    public string? StringFormat { get; set; }

    public object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key) && string.IsNullOrWhiteSpace(Path))
            return string.Empty;

        if (EditorLocalizationService.Current is not { } localization)
            return Key;

        if (Path is not null || !Key!.Contains('.'))
        {
            var binding = new MultiBinding
            {
                Converter = new LocalizeKeyConverter(localization),
                StringFormat = StringFormat
            };

            binding.Bindings.Add(new ReflectionBinding(Path ?? Key!));
            binding.Bindings.Add(new ReflectionBinding(nameof(IEditorLocalizationService.CurrentCulture))
            {
                Source = localization
            });
            if (FallbackPath is not null) binding.Bindings.Add(new ReflectionBinding(FallbackPath));

            return binding;
        }

        return new ReflectionBinding(nameof(IEditorLocalizationService.CurrentCulture))
        {
            Source = localization,
            Converter = new FuncValueConverter<object?, string>(_ => localization[Key!]),
            Mode = BindingMode.OneWay,
            StringFormat = StringFormat
        };
    }

    private sealed class LocalizeKeyConverter(IEditorLocalizationService localization) : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 0 || values[0] is not string key || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var text = localization[key];
            return text == "[" + key + "]" && values.Count > 2 && values[2] is string fallback ? fallback : text;
        }
    }
}
