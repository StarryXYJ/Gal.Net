using GalNet.Core.Primitives;

namespace GalNet.Core.View;

/// <summary>
/// Framework-neutral Game Scope facade. Primitive modules are validated and frozen
/// at construction; runtime routing only performs prefix and command lookup.
/// </summary>
public sealed class CompositeGameView : IGameView
{
    private static readonly PrimitiveDispatch Skipped = new(
        PrimitiveDispatchStatus.Skipped,
        new PrimitiveExecutionPolicy(false, false, null),
        Task.FromResult(PrimitiveResult.Empty));

    private readonly IReadOnlyDictionary<string, IPrimitiveModule> _modules;
    private readonly IReadOnlyDictionary<string, PrimitiveDescriptor> _descriptors;
    private bool _disposed;

    /// <summary>Creates a module-only Game Scope.</summary>
    public CompositeGameView(IEnumerable<IPrimitiveModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        (_modules, _descriptors) = FreezeModules(modules);
        Primitives = _descriptors.Values.ToArray();
    }

    public IReadOnlyCollection<PrimitiveDescriptor> Primitives { get; }

    public bool TryGetDescriptor(string primitiveType, out PrimitiveDescriptor? descriptor) =>
        _descriptors.TryGetValue(primitiveType, out descriptor);

    public PrimitiveDispatch Dispatch(
        PrimitiveInvocation invocation,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(control);
        if (_disposed || !TrySplit(invocation.TypeId, out var prefix, out var command) ||
            !_modules.TryGetValue(prefix, out var module))
            return Skipped;

        try
        {
            return module.Dispatch(command, invocation.Context, invocation.Arguments, control, cancellationToken) ?? Skipped;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Skipped;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? exceptions = null;
        foreach (var module in _modules.Values.Reverse())
        {
            try { module.Dispose(); }
            catch (Exception exception) { (exceptions ??= []).Add(exception); }
        }
        if (exceptions is { Count: > 0 }) throw new AggregateException(exceptions);
    }

    private static (IReadOnlyDictionary<string, IPrimitiveModule> Modules, IReadOnlyDictionary<string, PrimitiveDescriptor> Descriptors)
        FreezeModules(IEnumerable<IPrimitiveModule> modules)
    {
        var modulesByPrefix = new Dictionary<string, IPrimitiveModule>(StringComparer.Ordinal);
        var descriptors = new Dictionary<string, PrimitiveDescriptor>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            ValidatePrefix(module.Prefix);
            if (!modulesByPrefix.TryAdd(module.Prefix, module))
                throw new InvalidOperationException($"Primitive module prefix '{module.Prefix}' is already registered.");

            foreach (var descriptor in module.Descriptors ?? throw new InvalidOperationException($"Primitive module '{module.Prefix}' has no descriptor collection."))
            {
                ArgumentNullException.ThrowIfNull(descriptor);
                var command = GetCommand(module.Prefix, descriptor.TypeId);
                if (string.IsNullOrWhiteSpace(command))
                    throw new InvalidOperationException($"Primitive '{descriptor.TypeId}' does not belong to module '{module.Prefix}'.");
                if (!descriptors.TryAdd(descriptor.TypeId, descriptor))
                    throw new InvalidOperationException($"Primitive '{descriptor.TypeId}' is already registered.");
            }
        }
        return (modulesByPrefix, descriptors);
    }

    private static bool TrySplit(string typeId, out string prefix, out string command)
    {
        prefix = "";
        command = "";
        if (string.IsNullOrWhiteSpace(typeId)) return false;
        var separator = typeId.IndexOf('.');
        if (separator <= 0 || separator == typeId.Length - 1) return false;
        prefix = typeId[..separator];
        command = typeId[(separator + 1)..];
        return true;
    }

    private static string? GetCommand(string prefix, string typeId)
    {
        if (!TrySplit(typeId, out var typePrefix, out var command) || typePrefix != prefix) return null;
        return command;
    }

    private static void ValidatePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains('.') ||
            !string.Equals(prefix, prefix.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("Primitive module prefixes must be non-empty, lowercase, dot-free identifiers.", nameof(prefix));
    }

}
