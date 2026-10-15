using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>UI-thread draft and commit state. Owns subscriptions, never the borrowed binding.</summary>
public sealed class FieldEditorContext : INotifyPropertyChanged, IDisposable
{
    private readonly IFieldBinding _binding;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed, _dirty, _submitting;
    private int _draftVersion;
    private string _text = "";
    private FieldValue? _pending;
    public FieldEditorContext(FieldDescriptor field, IFieldBinding binding)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(binding);
        Field = field;
        _binding = binding;
        Refresh();
        _binding.Changed += OnChanged;
    }

    public FieldDescriptor Field { get; }
    public FieldValue Value { get; private set; }
    public JsonElement? EffectiveValue => (_pending ?? Value).Json ?? Field.DefaultValue;
    public object Label => _binding.Label ?? Field.LabelKey ?? Field.Id;
    public IReadOnlyList<FieldEditorOption> Options { get; private set; } = [];
    public ValueIssue? Issue { get; private set; }
    public bool IsSubmitting => _submitting;
    public bool HasDraft => _dirty;
    public bool IsEnabled => !_disposed && !_submitting && _binding.IsEnabled;
    public bool IsVisible => _binding.IsVisible;
    public string Text
    {
        get => _text;
        set
        {
            if (_disposed || _text == value) return;
            _text = value;
            _dirty = true;
            _draftVersion++;
            _ = ParseText(out var issue);
            Issue = issue;
            Notify();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public object? ReadValue() => EffectiveValue is { } json ? ValueCodec.Deserialize(Field.Value, json) : null;

    public Task<bool> CommitValueAsync(object? value)
    {
        if (_disposed) return Task.FromResult(false);
        try { return CommitAsync(new(ValueCodec.Serialize(Field.Value, value))); }
        catch (Exception exception)
        {
            Issue = new("value.invalid", exception.Message);
            Notify();
            return Task.FromResult(false);
        }
    }

    public Task<bool> CommitSliderValueAsync(object? value)
    {
        if (_disposed) return Task.FromResult(false);
        FieldValue candidate;
        try { candidate = new(ValueCodec.Serialize(Field.Value, value)); }
        catch (Exception exception)
        {
            Issue = new("value.invalid", exception.Message);
            Notify();
            return Task.FromResult(false);
        }
        if (_binding is not IFieldPreviewBinding { SupportsPreview: true } previewBinding)
            return CommitAsync(candidate);
        if (_submitting || !_binding.IsEnabled || !_binding.IsVisible) return Task.FromResult(false);

        ValueIssue? issue;
        try { issue = previewBinding.Preview(candidate); }
        catch (Exception exception) { issue = new("field.preview", exception.Message); }
        if (_disposed) return Task.FromResult(false);
        _dirty = false;
        _pending = null;
        Refresh();
        Issue = issue;
        Notify();
        return Task.FromResult(issue is null);
    }

    public Task<bool> CommitTextAsync()
    {
        if (_disposed || !_dirty) return Task.FromResult(false);
        var candidate = ParseText(out var issue);
        if (issue is null) return CommitAsync(candidate);
        if (!_disposed) { Issue = issue; Notify(); }
        return Task.FromResult(false);
    }

    public async Task<bool> CommitAsync(FieldValue candidate)
    {
        if (_disposed || _submitting) return false;
        if (!_binding.IsEnabled || !_binding.IsVisible)
        {
            Issue = new("field.unavailable", "Field is currently unavailable.");
            Notify();
            return false;
        }
        try
        {
            Issue = candidate.Json is { } json ? ValueValidation.Validate(Field.Value, json)
                : Field.IsRequired && Field.DefaultValue is null ? new("field.required", "Field must be provided.") : null;
        }
        catch (Exception exception) { Issue = new("field.validation", exception.Message); }
        if (Issue is not null) { Notify(); return false; }
        var version = _draftVersion;
        _pending = candidate;
        _submitting = true;
        Notify();
        try
        {
            var issue = await _binding.CommitAsync(candidate, _lifetime.Token);
            if (_disposed) return false;
            if (version == _draftVersion)
            {
                Issue = issue;
                if (issue is null) { _dirty = false; _pending = null; Refresh(); }
                else Notify();
            }
            return issue is null;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _draftVersion) { Issue = new("field.cancelled", "Edit was cancelled."); Notify(); }
            return false;
        }
        catch (Exception exception)
        {
            if (!_disposed && version == _draftVersion) { Issue = new("field.commit", exception.Message); Notify(); }
            return false;
        }
        finally { _submitting = false; _pending = null; if (!_disposed) Notify(); }
    }

    private FieldValue ParseText(out ValueIssue? issue)
    {
        try
        {
            var json = ValueCodec.ParseEditorText(Field.Value, Text);
            issue = ValueValidation.Validate(Field.Value, json);
            return new(json);
        }
        catch (JsonException exception) { issue = new("value.type", "Enter a complete JSON value.", exception.Path); return default; }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
        { issue = new("value.type", exception.Message); return default; }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshChanged();
        else Dispatcher.UIThread.Post(RefreshChanged);
    }

    private void RefreshChanged()
    {
        if (_disposed) return;
        try { Refresh(); }
        catch (Exception exception) { Issue = new("field.binding", exception.Message); Notify(); }
    }

    private void Refresh()
    {
        if (_disposed) return;
        Value = _binding.Read();
        Options = ReadOptions();
        if (Issue?.Code == "field.binding") Issue = null;
        if (!_dirty) _text = EffectiveValue is { } json ? ValueCodec.ToEditorText(Field.Value, json) : "";
        Notify();
    }

    private ReadOnlyCollection<FieldEditorOption> ReadOptions()
    {
        if (_binding.Options is { } options)
        {
            if (options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != options.Count)
                throw new ArgumentException("Choice option IDs must be unique.");
            return Array.AsReadOnly(options.ToArray());
        }
        IEnumerable<JsonElement> values = [];
        if (Field.Value.TryGetConstraint("enum", out var choices)) values = choices.EnumerateArray();
        else if (Field.Value.TryGetConstraint("options", out choices))
            values = choices.EnumerateArray().Select(choice => ValueCodec.FromEditorText(Field.Value, choice.GetString()!));
        else if ((Nullable.GetUnderlyingType(Field.Value.ValueType) ?? Field.Value.ValueType) is { IsEnum: true } type)
            values = Enum.GetValues(type).Cast<object>().Select(value => ValueCodec.Serialize(Field.Value, value));
        return Array.AsReadOnly(values.Select((value, index) => new FieldEditorOption(index.ToString(CultureInfo.InvariantCulture),
            value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText(), value)).ToArray());
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _binding.Changed -= OnChanged;
        try { _lifetime.Cancel(); }
        finally { _lifetime.Dispose(); }
    }
}
