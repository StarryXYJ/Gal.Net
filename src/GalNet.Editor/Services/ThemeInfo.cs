using Avalonia.Styling;

namespace GalNet.Editor.Services;

public sealed record ThemeInfo(
    string Name,
    string DisplayName,
    ThemeVariant Variant);
