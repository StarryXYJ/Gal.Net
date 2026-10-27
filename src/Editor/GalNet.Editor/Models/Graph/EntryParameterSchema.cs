using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Schemas;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Models.Graph;

public sealed record EntryTypeOption(string Value, string Category, string DisplayNameKey, IReadOnlyList<string>? SearchAliases = null);
public sealed record EntrySelectOption(string Value, string DisplayNameKey);

public interface IEntryFieldOrigin
{
    string? GroupId { get; }
    string EntryId { get; }
}

/// <summary>Dependent fields submit one author intent, preserving sibling and extension values.</summary>
public interface IEntryParameterPatchOrigin : IEntryFieldOrigin
{
    string EntryType { get; }
    IReadOnlyDictionary<string, JsonElement> Parameters { get; }
    Task<ValueIssue?> CommitPatchAsync(IReadOnlyDictionary<string, FieldValue> patch, CancellationToken token);
}

/// <summary>Schema-driven edits read the author document directly; no DTO snapshot or synchronization is needed.</summary>
public sealed class EntryParameterFormBinding : IDisposable
{
    private readonly EntryEditorItemViewModel _owner;
    private EventHandler? _changed;
    private bool _disposed;
    private readonly string _type;
    public EntryParameterFormBinding(EntryEditorItemViewModel owner, IReadOnlyList<FieldDescriptor> fields, Type? dtoType = null, Func<bool>? isCurrent = null)
    {
        _owner = owner;
        _isCurrent = isCurrent;
        _type = owner.Type;
        Fields = fields;
        var members = fields.ToDictionary(field => field.Id, field => (IFieldBinding)new DocumentField(this, field), StringComparer.Ordinal);
        Binding = field => members[field.Id];
        Item = new(owner.StableId, dtoType, fields, Binding, (patch, token) => !IsCurrent
            ? Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed."))
            : owner.CommitParametersAsync(patch.ToDictionary(pair => pair.Key, pair => (fields.Single(field => field.Id == pair.Key), pair.Value)), token));
    }
    public DtoEditorItem Item { get; }
    private readonly Func<bool>? _isCurrent;
    private bool IsCurrent => !_disposed && _owner.IsCurrent && _owner.Type == _type && (_isCurrent?.Invoke() ?? true);
    public IReadOnlyList<FieldDescriptor> Fields { get; }
    public Func<FieldDescriptor, IFieldBinding> Binding { get; }
    public void Refresh() { if (!_disposed) _changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose() { _disposed = true; _changed = null; }

    private sealed class DocumentField(EntryParameterFormBinding form, FieldDescriptor field) : IFieldBinding, IEntryParameterPatchOrigin
    {
        public FieldDraftState? Draft => GetDraft();
        private FieldDraftState? GetDraft() => form.IsCurrent ? form._owner.EditSession?.Get(form._owner, field) : null;
        public string? GroupId => form._owner.AuthorGroupId;
        public string EntryId => form._owner.StableId;
        public string EntryType => form._owner.Type;
        public IReadOnlyDictionary<string, JsonElement> Parameters => form._owner.Source.Parameters;
        public Task<ValueIssue?> CommitPatchAsync(IReadOnlyDictionary<string, FieldValue> patch, CancellationToken token) => !form.IsCurrent
            ? Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed."))
            : form._owner.CommitParametersAsync(patch.ToDictionary(pair => pair.Key, pair => (form.Fields.Single(field => field.Id == pair.Key), pair.Value)), token);
        public object HostContext => this;
        public object? Label => SceneFieldSemantics.Label(form._owner.Type, @field.Id) ?? @field.LabelKey ?? "Entry.Parameter." + @field.Id;
        public bool IsEnabled => form.IsCurrent;
        public bool CanReset => form._owner.Type != GalNet.Contracts.Builtins.BuiltinEntryIds.PlayTimeline || @field.Id != "timeline" || @field.DefaultValue is not null;
        public bool IsVisible => !form._owner.IsSceneOutput(@field.Id);
        public IReadOnlyList<FieldEditorOption>? Options => form._owner.SceneOptions(@field.Id);
        public FieldValue Read() => form._owner.Source.Parameters.TryGetValue(field.Id, out var json)
            ? new(json.Clone()) : default;
        public event EventHandler? Changed
        {
            add { ObjectDisposedException.ThrowIf(form._disposed, form); form._changed += value; }
            remove { form._changed -= value; }
        }
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return !form.IsCurrent ? Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed.", field.Id)) :
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
    internal EntryParameterFormBinding? ActionForm { get; set; }
    public IFieldBinding? ActionBinding => ActionForm?.Binding(Definition.Field);
    private readonly Action<string, string> _setValue;
    private bool _refreshing;

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

    partial void OnStringValueChanged(string value) { if (!_refreshing) _setValue(Id, value ?? ""); }
    public virtual void RefreshValue(string value)
    {
        ActionForm?.Refresh();
        _refreshing = true;
        try { StringValue = value; }
        finally { _refreshing = false; }
    }

}

/// <summary>Field adapter submits through the project's explicit Entry intent.</summary>
public sealed class SchemaEntryParameterEditorItemViewModel(EntryParameterDefinition definition, EntryEditorItemViewModel owner,
    Action<string, string> set) : EntryParameterEditorItemViewModel(definition, "", [], set), IFieldBinding, IEntryParameterPatchOrigin
{
    public string? GroupId => owner.AuthorGroupId;
    public string EntryId => owner.StableId;
    public string EntryType => owner.Type;
    public IReadOnlyDictionary<string, JsonElement> Parameters => owner.Source.Parameters;
    public Task<ValueIssue?> CommitPatchAsync(IReadOnlyDictionary<string, FieldValue> patch, CancellationToken token) => !IsEnabled
        ? Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed."))
        : owner.CommitParametersAsync(patch.ToDictionary(pair => pair.Key, pair => (owner.DetailForm!.Fields.Single(field => field.Id == pair.Key), pair.Value)), token);
    public object HostContext => this;
    public bool IsEnabled => owner.IsCurrent && owner.Type == _type && owner.ParameterFields.Contains(this);
    public bool CanReset => owner.Type != GalNet.Contracts.Builtins.BuiltinEntryIds.PlayTimeline || Id != "timeline" || Definition.Field.DefaultValue is not null;
    private readonly string _type = owner.Type;
    public IReadOnlyList<FieldDescriptor> Fields { get; } = [definition.Field];
    public Func<FieldDescriptor, IFieldBinding> FieldBinding => _ => this;
    public Type? DtoType => owner.ParameterDtoType;
    bool IFieldBinding.IsVisible => !owner.IsSceneOutput(Id);
    public event EventHandler? Changed;
    object? IFieldBinding.Label => SceneFieldSemantics.Label(owner.Type, Id) ?? Definition.DisplayNameKey;
    public override void RefreshValue(string value) => Changed?.Invoke(this, EventArgs.Empty);
    public FieldValue Read() => owner.Source.Parameters.TryGetValue(Id, out var json)
        ? new(json.Clone()) : default;
    IReadOnlyList<FieldEditorOption>? IFieldBinding.Options => owner.SceneOptions(Id) ?? (Definition.Options.Count == 0 ? null : Array.AsReadOnly(Definition.Options.Select(option =>
        new FieldEditorOption(option.Value, option, Definition.Field.Value.ValueType == typeof(bool)
            ? ValueCodec.Serialize(Definition.Field.Value, bool.Parse(option.Value))
            : ValueCodec.FromEditorText(Definition.Field.Value, option.Value))).ToArray()));
    public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsCurrent || owner.Type != _type || !owner.ParameterFields.Contains(this)) return Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed.", Id));
        return owner.CommitParameterAsync(Definition.Field, value, cancellationToken);
    }
}

public sealed class TextEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class AutocompleteEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class AssetEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class VariableNameEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
public sealed class ExpressionEntryParameterEditorItemViewModel(EntryParameterDefinition d, string v, IReadOnlyList<string> s, Action<string, string> set) : EntryParameterEditorItemViewModel(d, v, s, set);
