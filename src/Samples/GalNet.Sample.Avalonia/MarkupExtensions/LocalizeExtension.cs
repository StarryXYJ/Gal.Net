using System;
using Avalonia.Data;
using GalNet.Sample.Avalonia.Services;

namespace GalNet.Sample.Avalonia.MarkupExtensions;

/// <summary>Creates a live binding to a localized Sample UI string.</summary>
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

        if (SampleLocalizationService.Current is not { } localization) return Key;

        return new ReflectionBinding("Value")
        {
            Source = localization.GetString(Key),
            Mode = BindingMode.OneWay,
            StringFormat = StringFormat
        };
    }
}
