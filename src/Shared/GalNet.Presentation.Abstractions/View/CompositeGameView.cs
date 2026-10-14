using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Dispatches validated instructions to independently registered execution modules.</summary>
public sealed class CompositeGameView : IGameView
{
    private readonly IEntryCatalog _catalog;
    private readonly IReadOnlyList<IPrimitiveModule> _modules;
    private readonly Dictionary<string, PrimitiveImplementation> _instructions = new(StringComparer.Ordinal);
    private bool _disposed;

    public CompositeGameView(IEntryCatalog catalog, IEnumerable<IPrimitiveModule> modules)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(modules);
        _catalog = catalog;
        _modules = modules.ToArray();
        foreach (var module in _modules)
            foreach (var instruction in module.Instructions.Values)
            {
                if (!catalog.TryGet(instruction.Type, out var schema) || schema.Kind != EntryKind.Primitive)
                    throw new InvalidOperationException($"Instruction '{instruction.Type}' has no primitive schema.");
                instruction.ValidateDefinition(schema);
                if (!_instructions.TryAdd(instruction.Type, instruction))
                    throw new InvalidOperationException($"Instruction '{instruction.Type}' is already registered.");
            }
        Primitives = Array.AsReadOnly(catalog.Definitions.Where(definition => definition.Kind == EntryKind.Primitive).ToArray());
    }

    public IReadOnlyCollection<EntryDefinition> Primitives { get; }
    public bool TryGetEntry(string primitiveType, out EntryDefinition? entry) => _catalog.TryGet(primitiveType, out entry!);
    public bool CanExecute(string primitiveType) => !_disposed && _instructions.ContainsKey(primitiveType);

    /// <summary>Hosts can require complete execution coverage before starting a game.</summary>
    public void RequireExecutionCoverage()
        => RequireInstructions(Primitives.Select(definition => definition.Type));

    /// <summary>Checks compiled content before a host starts executing it.</summary>
    public void RequireInstructions(IEnumerable<string> typeIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(typeIds);
        var missing = typeIds.Where(type => !_instructions.ContainsKey(type)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0) throw new InvalidOperationException($"Project contract declares primitives without registered execution implementations: {string.Join(", ", missing)}.");
    }

    public PrimitiveInstance Dispatch(PrimitiveEntry entry, IGameRuntime runtime, CancellationToken cancellationToken,
        PrimitiveInvocationMode mode = PrimitiveInvocationMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(runtime);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!entry.IsGeneric)
            throw new InvalidOperationException($"Instruction '{entry.Type}' must be compiled before dispatch.");
        if (!_instructions.TryGetValue(entry.Type, out var implementation))
            throw new InvalidOperationException($"Instruction '{entry.Type}' has no registered implementation.");
        var definition = _catalog.Get(entry.Type);
        var normalized = new PrimitiveEntry(entry.Type,
            PrimitiveArgumentHelper.Normalize(definition.DynamicParameters!, entry.Arguments), entry.BatchId)
        {
            Id = entry.Id,
            StableId = entry.StableId,
            ReadingBoundaryId = entry.ReadingBoundaryId,
            Condition = entry.Condition
        };
        var instance = implementation.CreateInstance(new PrimitiveCreateContext(definition, normalized, runtime, cancellationToken));
        if (!string.Equals(instance.BatchId, normalized.BatchId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Instruction '{entry.Type}' created an instance with a different BatchId.");
        instance.Dispatch(mode);
        return instance;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? errors = null;
        foreach (var module in _modules.Reverse())
            try { module.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? errors = null;
        foreach (var module in _modules.Reverse())
            try { await module.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
    }
}
