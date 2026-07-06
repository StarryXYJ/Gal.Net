using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
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

    public string? StringFormat { get; set; }

    public object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return string.Empty;

        if (EditorLocalizationService.Current is not { } localization)
            return Key;

        if (!Key.Contains('.'))
        {
            var binding = new MultiBinding
            {
                Converter = new LocalizeKeyConverter(localization),
                StringFormat = StringFormat
            };

            binding.Bindings.Add(new ReflectionBinding(Key));
            binding.Bindings.Add(new ReflectionBinding(nameof(IEditorLocalizationService.CurrentCulture))
            {
                Source = localization
            });

            return binding;
        }

        return new ReflectionBinding($"[{Key}]")
        {
            Source = localization,
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

            return localization[key];
        }
    }
}
