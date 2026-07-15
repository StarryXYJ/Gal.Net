using GalNet.Core.UI;

namespace GalNet.Control.Abstraction.UI;

/// <summary>Data-only description of a preset setting. The editor renders this without knowing a preset's views.</summary>
public sealed record UiSettingDefinition(
    string Key,
    string DisplayNameKey,
    UiSettingType Type,
    string DefaultValue,
    IReadOnlyList<UiSettingOption>? Options = null,
    double? Minimum = null,
    double? Maximum = null,
    AssetPickerFilter? AssetFilter = null);

public sealed record UiSettingOption(string Value, string DisplayNameKey);
public enum UiSettingType { Text, Integer, Float, Color, Asset, Boolean, Select }

/// <param name="DisplayNameKey">Editor i18n key; never user-facing source text.</param>
/// <param name="DescriptionKey">Editor i18n key; never user-facing source text.</param>
public sealed record UiPresetMetadata(string Id, UiPageKind Page, string DisplayNameKey, string DescriptionKey);

/// <summary>Preset metadata and configuration schema. It intentionally contains no Avalonia view or Editor dependency.</summary>
public interface IUiPagePreset
{
    UiPresetMetadata Metadata { get; }
    IReadOnlyList<UiSettingDefinition> Settings { get; }
    IReadOnlyDictionary<string, string> CreateDefaultSettings();
}

public interface IUiPresetRegistry
{
    IReadOnlyList<IUiPagePreset> GetPresets(UiPageKind page);
    IUiPagePreset GetRequired(string id);
    IUiPagePreset GetDefault(UiPageKind page);
}
