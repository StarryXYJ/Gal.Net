using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Entry;

namespace GalNet.Editor.Models.Graph;

public sealed record EntryTypeOption(string Value, string DisplayNameKey);
public sealed record EntrySelectOption(string Value, string DisplayNameKey);

public sealed record EntryParameterDefinition(
    string Id,
    EntryParameterType Type,
    string DisplayNameKey,
    string DefaultValue,
    IReadOnlyList<EntrySelectOption> Options);

public partial class EntryParameterEditorItemViewModel : ObservableObject
{
    private readonly Action<string, string> _setValue;

    public EntryParameterDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayNameKey => Definition.DisplayNameKey;
    public EntryParameterType Type => Definition.Type;
    public IReadOnlyList<EntrySelectOption> Options => Definition.Options;
    public IReadOnlyList<string> Suggestions { get; }
    public bool IsMultiline => Type == EntryParameterType.MultilineText;
    public AssetPickerFilter AssetFilter => Type switch
    {
        EntryParameterType.ImageAsset => AssetPickerFilter.Image,
        EntryParameterType.AudioAsset => AssetPickerFilter.Audio,
        EntryParameterType.VideoAsset => AssetPickerFilter.Video,
        _ => AssetPickerFilter.All
    };

    [ObservableProperty] private string _stringValue;
    [ObservableProperty] private decimal? _numericValue;

    protected EntryParameterEditorItemViewModel(EntryParameterDefinition definition, string value, IReadOnlyList<string> suggestions, Action<string, string> setValue)
    {
        Definition = definition;
        Suggestions = suggestions;
        _setValue = setValue;
        _stringValue = value;
        _numericValue = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    partial void OnStringValueChanged(string value) => _setValue(Id, value ?? "");

    partial void OnNumericValueChanged(decimal? value)
    {
        if (value is null) return;
        var formatted = Type == EntryParameterType.Integer
            ? decimal.Truncate(value.Value).ToString("0", CultureInfo.InvariantCulture)
            : value.Value.ToString("0.################", CultureInfo.InvariantCulture);
        _setValue(Id, formatted);
    }
}

public sealed class TextEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class AutocompleteEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class IntegerEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class FloatEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class AssetEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class SelectEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
