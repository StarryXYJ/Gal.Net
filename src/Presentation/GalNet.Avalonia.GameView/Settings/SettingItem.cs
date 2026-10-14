using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Avalonia.GameView.Settings;

public enum NumericEditorKind { NumericUpDown, Slider }
public sealed record SettingAvailability(bool IsVisible = true, bool IsEnabled = true, string? ReasonKey = null);

/// <summary>Frozen category structure; values and cached availability remain observable.</summary>
public sealed class SettingCategory
{
    public SettingCategory(string id, string nameKey, IEnumerable<SettingItem> items, object? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        NameKey = nameKey;
        Label = label ?? nameKey;
        Items = Array.AsReadOnly(items.ToArray());
    }
    public string Id { get; }
    public string NameKey { get; }
    public object Label { get; }
    public IReadOnlyList<SettingItem> Items { get; }
}

public abstract class SettingItem : ObservableObject
{
    private SettingAvailability _availability = new();
    private bool _isApplying;
    private string? _errorKey;
    internal SettingsCoordinator Coordinator { get; set; } = new();
    protected SettingItem(string id, string nameKey, object? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        NameKey = nameKey;
        Label = label ?? nameKey;
    }
    public string Id { get; }
    public string NameKey { get; }
    public object Label { get; }
    public string? DescriptionKey { get; init; }
    public abstract Type ValueType { get; }
    public virtual FieldDescriptor? Field => null;
    public IReadOnlyList<FieldDescriptor> Fields => Field is { } descriptor ? [descriptor] : [];
    public Func<FieldDescriptor, IFieldBinding> FieldBinding => _ => (IFieldBinding)this;
    public Func<SettingAvailability>? EvaluateAvailability { get; init; }
    public SettingAvailability Availability => _availability;
    public bool IsVisible => _availability.IsVisible;
    public bool IsEnabled => _availability.IsEnabled && !IsApplying && !IsForced;
    public virtual bool IsForced => false;
    public bool IsApplying
    {
        get => _isApplying;
        protected set { if (SetProperty(ref _isApplying, value)) OnPropertyChanged(nameof(IsEnabled)); }
    }
    public string? ErrorKey { get => _errorKey; protected set => SetProperty(ref _errorKey, value); }
    protected virtual SettingAvailability EvaluateState() => EvaluateAvailability?.Invoke() ?? new();
    public void RefreshAvailability()
    {
        var next = EvaluateState();
        if (next == _availability) return;
        _availability = next;
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsEnabled));
    }
    public abstract void Refresh();
}

public abstract class SettingItem<T> : SettingItem, IFieldBinding
{
    private readonly Func<T> _read;
    private readonly Func<T, CancellationToken, Task> _apply;
    private readonly Func<T>? _forcedValue;
    protected SettingItem(string id, string nameKey, T defaultValue, Func<T> read,
        Func<T, CancellationToken, Task> apply, object? label = null, Func<T>? forcedValue = null)
        : base(id, nameKey, label)
    {
        DefaultValue = defaultValue;
        _read = read;
        _apply = apply;
        _forcedValue = forcedValue;
    }
    public override Type ValueType => typeof(T);
    public ValueDescriptor ValueContract { get; protected set; } = new(typeof(T));
    public EditorHints? Editor { get; init; }
    public override FieldDescriptor Field => new(Id, ValueContract, isRequired: true,
        editor: GetEditorHints(), labelKey: NameKey, descriptionKey: DescriptionKey);
    protected virtual EditorHints? GetEditorHints() => Editor;
    protected virtual IReadOnlyList<FieldEditorOption>? FieldOptions => null;
    IReadOnlyList<FieldEditorOption>? IFieldBinding.Options => FieldOptions;
    public event EventHandler? Changed;
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    FieldValue IFieldBinding.Read() => new(ValueCodec.Serialize(ValueContract, Value));
    async Task<ValueIssue?> IFieldBinding.CommitAsync(FieldValue value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value.Json is not { } json) return new("setting.required", "A setting value is required.", Id);
        var typed = (T)ValueCodec.Deserialize(ValueContract, json)!;
        return await TrySetValueAsync(typed, cancellationToken) ? null : new("setting.apply", ErrorKey ?? "Settings.Cancelled", Id);
    }
    public override bool IsForced => _forcedValue is not null;
    public T DefaultValue { get; set; }
    public Func<T, string?>? Validator { get; init; }
    public T Value
    {
        get => _forcedValue is { } forced ? forced() : _read();
        set => _ = TrySetValueAsync(value);
    }
    protected virtual string? Validate(T value) => Validator?.Invoke(value);
    public Task<bool> TrySetValueAsync(T value, CancellationToken cancellationToken = default) =>
        Coordinator.EnqueueAsync(() => ApplyValueAsync(value, cancellationToken), cancellationToken);

    protected async Task<bool> ApplyValueAsync(T value, CancellationToken cancellationToken, bool systemChange = false)
    {
        RefreshAvailability();
        if (IsForced || (!systemChange && !IsEnabled)) { ErrorKey = IsForced ? "Settings.Forced" : Availability.ReasonKey ?? "Settings.Unavailable"; return false; }
        if (Validate(value) is { } error) { ErrorKey = error; Refresh(); return false; }
        IsApplying = true;
        ErrorKey = null;
        try
        {
            if (!EqualityComparer<T>.Default.Equals(Value, value))
            {
                await _apply(value, cancellationToken);
                Coordinator.MarkPendingSave();
            }
            Refresh();
            Coordinator.RefreshAvailability();
            try { await Coordinator.PersistAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { ErrorKey = "Settings.Cancelled"; return false; }
            catch (Exception) { ErrorKey = "Settings.SaveFailed"; return false; }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { ErrorKey = "Settings.Cancelled"; return false; }
        catch (Exception) { ErrorKey = "Settings.ApplyFailed"; return false; }
        finally { IsApplying = false; Refresh(); }
    }

    public override void Refresh() => OnPropertyChanged(nameof(Value));
}

/// <summary>All dependent edits use the same serialized application/persistence queue.</summary>
public sealed class SettingsCoordinator
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private IReadOnlyList<SettingItem> _items = [];
    private bool _pendingSave;
    public Func<CancellationToken, Task> SaveAsync { get; init; } = _ => Task.CompletedTask;
    public void Attach(IReadOnlyList<SettingCategory> categories)
    {
        if (categories.Select(category => category.Id).Distinct(StringComparer.Ordinal).Count() != categories.Count)
            throw new ArgumentException("Setting category IDs must be unique.", nameof(categories));
        _items = Array.AsReadOnly(categories.SelectMany(category => category.Items).ToArray());
        if (_items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != _items.Count)
            throw new ArgumentException("Setting item IDs must be unique.", nameof(categories));
        foreach (var item in _items) item.Coordinator = this;
        RefreshAvailability();
    }
    public Task FlushAsync()
    {
        lock (_gate)
        {
            var flush = FlushCoreAsync(_tail);
            _tail = ObserveAsync(flush);
            return flush;
        }
    }
    private async Task FlushCoreAsync(Task previous)
    {
        await Task.Yield();
        await previous;
        await PersistAsync(CancellationToken.None);
    }
    public void RefreshAvailability() { foreach (var item in _items) item.RefreshAvailability(); }
    internal void MarkPendingSave() => _pendingSave = true;
    internal async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (!_pendingSave) return;
        await SaveAsync(cancellationToken);
        _pendingSave = false;
    }
    internal Task<bool> EnqueueAsync(Func<Task<bool>> work, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = RunAsync(_tail, work, cancellationToken);
            _tail = ObserveAsync(result);
            return result;
        }
    }
    private static async Task<bool> RunAsync(Task previous, Func<Task<bool>> work, CancellationToken cancellationToken)
    {
        await Task.Yield();
        await previous;
        if (cancellationToken.IsCancellationRequested) return false;
        return await work();
    }
    private static async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch (Exception) { /* An individual failed edit must not poison subsequent edits. */ }
    }
}
