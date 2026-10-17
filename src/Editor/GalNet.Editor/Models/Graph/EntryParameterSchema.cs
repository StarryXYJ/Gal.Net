using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Models;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Models.Graph;

public sealed record EntryTypeOption(string Value, string Category, string DisplayNameKey);
public sealed record EntrySelectOption(string Value, string DisplayNameKey);

/// <summary>Schema-driven edits read the author document directly; no DTO snapshot or synchronization is needed.</summary>
public sealed class EntryParameterFormBinding : IDisposable
{
    private readonly EntryEditorItemViewModel _owner;
    private EventHandler? _changed;
    private bool _disposed;
    public EntryParameterFormBinding(EntryEditorItemViewModel owner, IReadOnlyList<FieldDescriptor> fields, Type? dtoType = null)
    {
        _owner = owner;
        Fields = fields;
        var members = fields.ToDictionary(field => field.Id, field => (IFieldBinding)new DocumentField(this, field), StringComparer.Ordinal);
        Binding = field => members[field.Id];
        Item = new(owner.StableId, dtoType, fields, Binding, (patch, token) => _disposed
            ? Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed."))
            : owner.CommitParametersAsync(patch.ToDictionary(pair => pair.Key, pair => (fields.Single(field => field.Id == pair.Key), pair.Value)), token));
    }
    public DtoEditorItem Item { get; }
    public IReadOnlyList<FieldDescriptor> Fields { get; }
    public Func<FieldDescriptor, IFieldBinding> Binding { get; }
    public void Refresh() { if (!_disposed) _changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose() { _disposed = true; _changed = null; }

    private sealed class DocumentField(EntryParameterFormBinding form, FieldDescriptor field) : IFieldBinding
    {
        public bool IsEnabled => !form._disposed;
        public FieldValue Read() => form._owner.Parameters.TryGetValue(field.Id, out var text)
            ? new(ValueCodec.ParseEditorText(field.Value, text)) : default;
        public event EventHandler? Changed
        {
            add { ObjectDisposedException.ThrowIf(form._disposed, form); form._changed += value; }
            remove { form._changed -= value; }
        }
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return form._disposed ? Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed.", field.Id)) :
                form._owner.CommitParameterAsync(field, value, cancellationToken);
        }
    }
}

public sealed record EntryParameterDefinition(
    string Id,
    EntryParameterType Type,
    string DisplayNameKey,
    string DefaultValue,
    IReadOnlyList<EntrySelectOption> Options,
    FieldDescriptor Field);

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
        EntryParameterType.EffectProgramAsset => AssetPickerFilter.EffectProgram,
        _ => AssetPickerFilter.All
    };

    [ObservableProperty] private string _stringValue;

    protected EntryParameterEditorItemViewModel(EntryParameterDefinition definition, string value, IReadOnlyList<string> suggestions, Action<string, string> setValue)
    {
        Definition = definition;
        Suggestions = suggestions;
        _setValue = setValue;
        _stringValue = value;
    }

    partial void OnStringValueChanged(string value) => _setValue(Id, value ?? "");

}

/// <summary>Editor authoring adapter. Commits still replace Parameters so graph history and persistence observe the edit.</summary>
public sealed class SchemaEntryParameterEditorItemViewModel(EntryParameterDefinition definition, EntryEditorItemViewModel owner,
    Action<string, string> set) : EntryParameterEditorItemViewModel(definition, "", [], set), IFieldBinding
{
    public IReadOnlyList<FieldDescriptor> Fields { get; } = [definition.Field];
    public Func<FieldDescriptor, IFieldBinding> FieldBinding => _ => this;
    public event EventHandler? Changed;
    object? IFieldBinding.Label => Definition.DisplayNameKey;
    public void RefreshValue() => Changed?.Invoke(this, EventArgs.Empty);
    public FieldValue Read() => owner.Parameters.TryGetValue(Id, out var text)
        ? new(ValueCodec.ParseEditorText(Definition.Field.Value, text)) : default;
    IReadOnlyList<FieldEditorOption>? IFieldBinding.Options => Definition.Options.Count == 0 ? null : Array.AsReadOnly(Definition.Options.Select(option =>
        new FieldEditorOption(option.Value, option, Definition.Field.Value.ValueType == typeof(bool)
            ? ValueCodec.Serialize(Definition.Field.Value, bool.Parse(option.Value))
            : ValueCodec.FromEditorText(Definition.Field.Value, option.Value))).ToArray());
    public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.ParameterFields.Contains(this)) return Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed.", Id));
        return owner.CommitParameterAsync(Definition.Field, value, cancellationToken);
    }
}

public sealed class TextEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class AutocompleteEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class AssetEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class VariableNameEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class ExpressionEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
