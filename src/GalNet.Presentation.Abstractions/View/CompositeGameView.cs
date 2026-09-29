using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Default game-scope mount for primitive entry modules.</summary>
public sealed class CompositeGameView : IGameView
{
    private readonly IReadOnlyList<IEntryModule> _modules;
    private readonly IReadOnlyDictionary<string, PrimitiveEntryBase> _entries;
    private bool _disposed;

    public CompositeGameView(IEnumerable<IEntryModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        (_modules, _entries) = FreezeModules(modules);
        Primitives = Array.AsReadOnly(_entries.Values.ToArray());
    }

    public IReadOnlyCollection<PrimitiveEntryBase> Primitives { get; }

    public bool TryGetEntry(string primitiveType, out PrimitiveEntryBase? entry) =>
        _entries.TryGetValue(primitiveType, out entry);

    public PrimitiveInstance? Dispatch(
        PrimitiveEntry entry,
        IGameRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(runtime);
        ThrowIfDisposed();
        if (!_entries.TryGetValue(entry.Type, out var definition))
            return null;

        var normalizedEntry = new PrimitiveEntry(
            entry.Type,
            PrimitiveArgumentHelper.Normalize(definition.Parameters, entry.Arguments),
            entry.BatchId)
        {
            Id = entry.Id,
            Condition = entry.Condition
        };
        var context = new PrimitiveCreateContext(definition, normalizedEntry, runtime, cancellationToken);
        var instance = definition.CreateInstance(context);
        if (!string.Equals(instance.BatchId, normalizedEntry.BatchId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Primitive '{entry.Type}' created an instance with a different BatchId.");

        instance.Dispatch();
        return instance;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        List<Exception>? exceptions = null;
        foreach (var module in _modules.Reverse())
        {
            try { module.Dispose(); }
            catch (Exception exception) { (exceptions ??= []).Add(exception); }
        }
        if (exceptions is { Count: > 0 })
            throw new AggregateException(exceptions);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CompositeGameView));
    }

    private static (IReadOnlyList<IEntryModule> Modules, IReadOnlyDictionary<string, PrimitiveEntryBase> Entries)
        FreezeModules(IEnumerable<IEntryModule> modules)
    {
        var moduleList = new List<IEntryModule>();
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new Dictionary<string, PrimitiveEntryBase>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            if (!moduleIds.Add(module.Id))
                throw new InvalidOperationException($"Entry module '{module.Id}' is already mounted.");
            moduleList.Add(module);
            foreach (var entry in module.PrimitiveEntries.Values)
                if (!entries.TryAdd(entry.Name, entry))
                    throw new InvalidOperationException($"Primitive entry '{entry.Name}' is already mounted.");
        }
        return (moduleList.AsReadOnly(), new System.Collections.ObjectModel.ReadOnlyDictionary<string, PrimitiveEntryBase>(entries));
    }
}
