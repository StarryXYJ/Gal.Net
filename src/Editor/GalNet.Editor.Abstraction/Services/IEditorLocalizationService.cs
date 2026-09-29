using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace GalNet.Editor.Abstraction.Services;

public interface IEditorLocalizationService : INotifyPropertyChanged
{
    CultureInfo CurrentCulture { get; }
    IReadOnlyList<CultureInfo> AvailableCultures { get; }
    string this[string key] { get; }
    string Format(string key, params object[] args);
    void ApplyLocale(string localeCode);
    void ApplyCulture(CultureInfo culture);
}