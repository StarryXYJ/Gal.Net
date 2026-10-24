using GalNet.Contracts.Builtins;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

/// <summary>A game-scoped, ordinal action registry for custom.invoke. Frozen when registered with a view.</summary>
public sealed class BuiltinCustomActionModule(bool strictMissingActions = false) : IPrimitiveModule
{
    private readonly Dictionary<string, Func<PrimitiveCreateContext, string, PrimitiveInstance>> _actions = new(StringComparer.Ordinal);
    private readonly PrimitiveTaskScope _operations = new();
    private bool _frozen;
    private bool _disposed;

    /// <summary>Registers a synchronous, non-blocking action. Quick falls back to execute; CatchUp defaults to no operation.</summary>
    public BuiltinCustomActionModule RegisterImmediate(string id, Action<string> execute,
        Action<string>? quick = null, Action<string>? catchUp = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return RegisterImmediate(id, (_, argument) => execute(argument),
            quick is null ? null : (_, argument) => quick(argument),
            catchUp is null ? null : (_, argument) => catchUp(argument));
    }

    /// <summary>Registers an action with access to runtime, entry identity, and scope cancellation.</summary>
    public BuiltinCustomActionModule RegisterImmediate(string id, Action<PrimitiveCreateContext, string> execute,
        Action<PrimitiveCreateContext, string>? quick = null, Action<PrimitiveCreateContext, string>? catchUp = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return RegisterFactory(id, (context, argument) => new ActionInstance(
            () => execute(context, argument), quick is null ? null : () => quick(context, argument),
            catchUp is null ? null : () => catchUp(context, argument), context.BatchId));
    }

    /// <summary>Registers an asynchronous operation. Quick falls back to execute; CatchUp defaults to no operation.</summary>
    public BuiltinCustomActionModule RegisterAsync(string id, Func<string, CancellationToken, Task> execute,
        bool blocking = false, Func<string, CancellationToken, Task>? quick = null,
        Func<string, CancellationToken, Task>? catchUp = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return RegisterAsync(id, (_, argument, token) => execute(argument, token), blocking,
            quick is null ? null : (_, argument, token) => quick(argument, token),
            catchUp is null ? null : (_, argument, token) => catchUp(argument, token));
    }

    /// <summary>Registers an asynchronous operation with runtime access and optional story blocking.</summary>
    public BuiltinCustomActionModule RegisterAsync(string id, Func<PrimitiveCreateContext, string, CancellationToken, Task> execute,
        bool blocking = false, Func<PrimitiveCreateContext, string, CancellationToken, Task>? quick = null,
        Func<PrimitiveCreateContext, string, CancellationToken, Task>? catchUp = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return RegisterFactory(id, (context, argument) => new OperationInstance(
            token => execute(context, argument, token), quick is null ? null : token => quick(context, argument, token),
            catchUp is null ? null : token => catchUp(context, argument, token),
            context.BatchId, blocking, _operations, context.ScopeCancellation));
    }

    /// <summary>Registers a fresh instance factory. The instance owns all mode, skip, and completion behavior.
    /// Perform side effects during instance dispatch, not in this factory; preserve context.BatchId.</summary>
    public BuiltinCustomActionModule RegisterFactory(string id, Func<PrimitiveCreateContext, string, PrimitiveInstance> factory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_frozen) throw new InvalidOperationException("Custom action registration is frozen.");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_actions.TryAdd(id, factory)) throw new InvalidOperationException($"Custom action '{id}' is already registered.");
        return this;
    }

    public void Register(PrimitiveRegistry registry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_frozen) throw new InvalidOperationException("Custom action registration is frozen.");
        ArgumentNullException.ThrowIfNull(registry);
        _frozen = true;
        registry.Register<CustomInvokeOptions>(BuiltinEntryIds.InvokeCustom, CreateInstance);
    }

    private PrimitiveInstance CreateInstance(PrimitiveCreateContext context, CustomInvokeOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_actions.TryGetValue(options.Action, out var factory)) return factory(context, options.Argument);
        return new ImmediatePrimitiveInstance(() =>
        {
            GameLog.Logger.Warning("Custom action {ActionId} is not registered at group {GroupId}, entry {EntryId}",
                options.Action, context.Runtime.CurrentNodeId,
                context.Entry.StableId ?? context.Entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (strictMissingActions) throw new InvalidOperationException($"Custom action '{options.Action}' is not registered.");
        }, batchId: context.BatchId);
    }

    public void Dispose() { _disposed = true; _operations.Dispose(); GC.SuppressFinalize(this); }
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        try { await _operations.DisposeAsync().ConfigureAwait(false); }
        finally { GC.SuppressFinalize(this); }
    }

    private sealed class ActionInstance(Action execute, Action? quick, Action? catchUp, string? batchId)
        : PrimitiveInstance(batchId)
    {
        public override bool IsBlocking => false;
        public override bool IsSkippable => false;
        protected override void OnDispatch() => Run(execute);
        protected override void OnQuickDispatch() => Run(quick ?? execute);
        protected override void OnCatchUpDispatch() => Run(catchUp);
        private void Run(Action? action) { action?.Invoke(); TryComplete(); }
    }

    private sealed class OperationInstance(Func<CancellationToken, Task> execute, Func<CancellationToken, Task>? quick,
        Func<CancellationToken, Task>? catchUp, string? batchId, bool blocking, PrimitiveTaskScope operations,
        CancellationToken token)
        : AsyncPrimitiveInstance(batchId, blocking, token)
    {
        protected override Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var operation = InvocationMode switch
            {
                PrimitiveInvocationMode.Quick => quick ?? execute,
                PrimitiveInvocationMode.CatchUp => catchUp,
                _ => execute
            };
            if (operation is null) return Task.CompletedTask;
            return operations.Run(async lifetimeToken =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
                linked.Token.ThrowIfCancellationRequested();
                await operation(linked.Token).ConfigureAwait(false);
            });
        }
    }
}
