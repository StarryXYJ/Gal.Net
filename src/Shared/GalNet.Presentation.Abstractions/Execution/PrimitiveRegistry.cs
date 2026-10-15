using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GalNet.Presentation.Abstractions.Execution;

/// <summary>Registers instance factories against a game contract during view composition.</summary>
public sealed class PrimitiveRegistry
{
    private readonly IEntryCatalog _catalog;
    internal Dictionary<string, Func<PrimitiveCreateContext, PrimitiveInstance>> Instructions { get; } = new(StringComparer.Ordinal);
    private bool _frozen;
    private Func<string, bool>? _selection;

    internal PrimitiveRegistry(IEntryCatalog catalog) => _catalog = catalog;
    internal void Freeze() => _frozen = true;

    public void Register(string type, Func<PrimitiveCreateContext, PrimitiveInstance> factory) => Add(type, factory, null);

    /// <summary>Checks the DTO contract now and decodes independent Options for each invocation.</summary>
    public void Register<T>(string type, Func<PrimitiveCreateContext, T, PrimitiveInstance> factory)
        where T : IFieldDto, IFieldSchema<T>, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        Add(type, context => factory(context, ValueCodec.DeserializeFields<T>(context.Parameters.Fields, context.Arguments)), typeof(T));
    }

    private void Add(string type, Func<PrimitiveCreateContext, PrimitiveInstance> factory, Type? optionsType)
    {
        ObjectDisposedException.ThrowIf(_frozen, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        if (_selection is not null && !_selection(type)) return;
        if (!_catalog.TryGet(type, out var schema) || schema.Kind != EntryKind.Primitive)
            throw new InvalidOperationException($"Instruction '{type}' has no primitive schema.");
        if (optionsType is not null && schema.FieldDataType != optionsType)
            throw new InvalidOperationException($"Instruction '{type}' requires DTO '{optionsType.FullName}', but its contract declares '{schema.FieldDataType?.FullName ?? "none"}'.");
        if (!Instructions.TryAdd(type, factory))
            throw new InvalidOperationException($"Instruction '{type}' is already registered.");
    }

    /// <summary>Registers only a module's instructions declared as primitives by the selected catalog.</summary>
    public void RegisterDeclared(IPrimitiveModule module, IEntryCatalog catalog)
    {
        ObjectDisposedException.ThrowIf(_frozen, this);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(catalog);
        var previous = _selection;
        _selection = type => (previous?.Invoke(type) ?? true) && catalog.TryGet(type, out var schema) && schema.Kind == EntryKind.Primitive;
        try { module.Register(this); }
        finally { _selection = previous; }
    }

    /// <summary>Synchronous, non-blocking behavior. CatchUp repeats dispatch unless overridden.</summary>
    public void Immediate(string type, Action<PrimitiveCreateContext> dispatch, Action<PrimitiveCreateContext>? catchUp = null)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        Register(type, context => new ImmediatePrimitiveInstance(() => dispatch(context), batchId: context.BatchId,
            catchUp: catchUp is null ? null : () => catchUp(context)));
    }

    public void Immediate<T>(string type, Action<PrimitiveCreateContext, T> dispatch, Action<PrimitiveCreateContext, T>? catchUp = null)
        where T : IFieldDto, IFieldSchema<T>, new()
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        Register<T>(type, (context, options) => new ImmediatePrimitiveInstance(() => dispatch(context, options),
            batchId: context.BatchId, catchUp: catchUp is null ? null : () => catchUp(context, options)));
    }

    /// <summary>A single asynchronous operation. Quick executes normally; CatchUp repeats execution unless overridden.</summary>
    public void Async<T>(string type, Func<PrimitiveCreateContext, T, CancellationToken, Task> execute, bool blocking = false,
        Func<PrimitiveCreateContext, T, CancellationToken, Task>? catchUp = null)
        where T : IFieldDto, IFieldSchema<T>, new()
    {
        ArgumentNullException.ThrowIfNull(execute);
        Register<T>(type, (context, options) => new OperationInstance(
            token => execute(context, options, token), catchUp is null ? null : token => catchUp(context, options, token),
            context.BatchId, blocking, context.ScopeCancellation));
    }

    private sealed class OperationInstance(Func<CancellationToken, Task> execute,
        Func<CancellationToken, Task>? catchUp, string? batchId, bool blocking, CancellationToken token)
        : AsyncPrimitiveInstance(batchId, blocking, token)
    {
        protected override Task ExecuteAsync(CancellationToken cancellationToken) =>
            (InvocationMode == PrimitiveInvocationMode.CatchUp ? catchUp ?? execute : execute)(cancellationToken);
    }
}
