namespace GalNet.Core.Primitives;

/// <summary>A Game Scope object addressable by a stable, editor-generated identifier.</summary>
public abstract class RuntimeHandle : IDisposable
{
    public required Guid Id { get; init; }
    public abstract string TypeId { get; }
    public abstract void Dispose();
}

/// <summary>Owns and resolves runtime handles for one Game Scope.</summary>
public interface IHandleManager : IDisposable
{
    void Add(RuntimeHandle handle);
    bool TryGet(Guid id, out RuntimeHandle? handle);
    bool TryGet<THandle>(Guid id, out THandle? handle) where THandle : RuntimeHandle;
    bool Remove(Guid id);
    IReadOnlyCollection<RuntimeHandle> GetAll();
}

/// <summary>Thread-safe Game Scope handle registry with deterministic ownership of disposal.</summary>
public sealed class HandleManager : IHandleManager
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, RuntimeHandle> _handles = [];
    private bool _disposed;

    public void Add(RuntimeHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.Id == Guid.Empty)
            throw new ArgumentException("Runtime handle identifiers cannot be empty.", nameof(handle));

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_handles.TryAdd(handle.Id, handle))
                throw new InvalidOperationException($"Runtime handle '{handle.Id}' is already registered.");
        }
    }

    public bool TryGet(Guid id, out RuntimeHandle? handle)
    {
        lock (_gate)
            return _handles.TryGetValue(id, out handle);
    }

    public bool TryGet<THandle>(Guid id, out THandle? handle) where THandle : RuntimeHandle
    {
        lock (_gate)
        {
            if (_handles.TryGetValue(id, out var candidate) && candidate is THandle typed)
            {
                handle = typed;
                return true;
            }
        }

        handle = null;
        return false;
    }

    public bool Remove(Guid id)
    {
        RuntimeHandle? handle;
        lock (_gate)
        {
            ThrowIfDisposed();
            _handles.Remove(id, out handle);
        }

        handle?.Dispose();
        return true;
    }

    public IReadOnlyCollection<RuntimeHandle> GetAll()
    {
        lock (_gate)
            return _handles.Values.ToArray();
    }

    public void Dispose()
    {
        RuntimeHandle[] handles;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            handles = _handles.Values.ToArray();
            _handles.Clear();
        }

        List<Exception>? failures = null;
        foreach (var handle in handles)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
