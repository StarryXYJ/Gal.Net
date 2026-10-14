using System.ComponentModel;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Borrowed DTO members. Hosts may override submission and presence without adding per-DTO adapters.</summary>
public sealed class DtoBinding : IDisposable
{
    private readonly Dictionary<string, MemberBinding> _members;
    private readonly Func<FieldDescriptor, FieldValue, CancellationToken, Task<ValueIssue?>>? _commit;
    private readonly Func<string, bool>? _isProvided;
    private bool _disposed;
    public DtoBinding(IFieldDto data,
        Func<FieldDescriptor, FieldValue, CancellationToken, Task<ValueIssue?>>? commit = null,
        Func<string, bool>? isProvided = null)
    {
        Data = data;
        _commit = commit;
        _isProvided = isProvided;
        _members = data.Fields.ToDictionary(field => field.Id, field => new MemberBinding(this, field), StringComparer.Ordinal);
        Binding = field => _members[field.Id];
    }
    public IFieldDto Data { get; }
    public IReadOnlyList<FieldDescriptor> Fields => Data.Fields;
    public Func<FieldDescriptor, IFieldBinding> Binding { get; }
    public void Refresh() { if (!_disposed) foreach (var member in _members.Values) member.Notify(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var member in _members.Values) member.Detach();
    }

    private sealed class MemberBinding(DtoBinding owner, FieldDescriptor field) : IFieldBinding
    {
        private EventHandler? _changed;
        public bool IsEnabled => !owner._disposed;
        public FieldValue Read() => owner._isProvided?.Invoke(field.Id) == false ? default :
            new(ValueCodec.Serialize(field.Value, owner.Data.ReadField(field.Id)));
        public event EventHandler? Changed
        {
            add
            {
                if (owner._disposed) throw new ObjectDisposedException(nameof(DtoBinding));
                if (_changed is null && owner.Data is INotifyPropertyChanged observable) observable.PropertyChanged += OnChanged;
                _changed += value;
            }
            remove
            {
                _changed -= value;
                if (_changed is null && owner.Data is INotifyPropertyChanged observable) observable.PropertyChanged -= OnChanged;
            }
        }
        private void OnChanged(object? sender, PropertyChangedEventArgs e) => Notify();
        public void Notify() => _changed?.Invoke(this, EventArgs.Empty);
        public void Detach()
        {
            if (owner.Data is INotifyPropertyChanged observable) observable.PropertyChanged -= OnChanged;
            _changed = null;
        }
        public async Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner._disposed) return new("dto.obsolete", "DTO binding has been released.", field.Id);
            if (value.Json is { } json && ValueValidation.Validate(field.Value, json) is { } issue) return issue;
            if (owner._commit is { } commit) return await commit(field, value, cancellationToken);
            if (value.Json is not { } provided) return new("dto.presence", "DTO members require a value; the host must handle omitted fields.", field.Id);
            owner.Data.WriteField(field.Id, ValueCodec.Deserialize(field.Value, provided));
            owner.Refresh();
            return null;
        }
    }
}
