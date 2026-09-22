using System.Text.Json;
using GalNet.Core.Runtime;

namespace GalNet.Core.Primitives;

/// <summary>Static metadata owned by a primitive handler.</summary>
public sealed record PrimitiveDescriptor(
    string TypeId,
    DynamicParameterTable Parameters,
    bool CreatesCheckpoint = false);

/// <summary>Where an invocation originated. Timeline events never create checkpoints.</summary>
public enum PrimitiveInvocationOrigin
{
    GroupEntry,
    TimelineEvent,
    Internal
}

/// <summary>Runtime state and source information supplied to one primitive invocation.</summary>
public sealed class PrimitiveContext
{
    public required IGameRuntime Runtime { get; init; }
    public required PrimitiveInvocationOrigin Origin { get; init; }
    public string? SourceId { get; init; }
}

/// <summary>Unparsed, compiled primitive data passed to the routed handler.</summary>
public sealed record PrimitiveInvocation(
    string TypeId,
    PrimitiveContext Context,
    JsonElement Arguments);

/// <summary>Whether routing accepted an invocation or skipped it before execution.</summary>
public enum PrimitiveDispatchStatus
{
    Accepted,
    Skipped
}

/// <summary>Terminal result of an accepted primitive invocation.</summary>
public enum PrimitiveResultStatus
{
    Succeeded,
    Failed
}

/// <summary>Optional value and terminal outcome returned by a primitive.</summary>
public sealed record PrimitiveResult(PrimitiveResultStatus Status, JsonElement? Value)
{
    public static PrimitiveResult Empty { get; } = new(PrimitiveResultStatus.Succeeded, null);
    public static PrimitiveResult Failed { get; } = new(PrimitiveResultStatus.Failed, null);
}

/// <summary>Resolved execution behavior for one invocation.</summary>
public sealed record PrimitiveExecutionPolicy(bool Blocking, bool Skippable, string? BatchId);

/// <summary>Routing result returned synchronously while work continues in <see cref="Completion"/>.</summary>
public sealed record PrimitiveDispatch(
    PrimitiveDispatchStatus Status,
    PrimitiveExecutionPolicy Policy,
    Task<PrimitiveResult> Completion);

/// <summary>Thread-safe, idempotent skip signal for a single invocation.</summary>
public sealed class PrimitiveExecutionControl
{
    private readonly TaskCompletionSource _skipRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task SkipRequested => _skipRequested.Task;

    public bool RequestSkip() => _skipRequested.TrySetResult();
}

/// <summary>
/// Optional helper contract for a module's private command table. Runtime routing
/// never queries this interface directly.
/// </summary>
public interface IPrimitiveHandler
{
    PrimitiveDescriptor Descriptor { get; }

    PrimitiveDispatch Dispatch(
        PrimitiveContext context,
        JsonElement arguments,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken);
}

/// <summary>A frozen Game Scope module that owns one prefix and its command descriptors.</summary>
public interface IPrimitiveModule : IDisposable
{
    string Prefix { get; }
    IReadOnlyCollection<PrimitiveDescriptor> Descriptors { get; }

    PrimitiveDispatch Dispatch(
        string command,
        PrimitiveContext context,
        JsonElement arguments,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken);
}

/// <summary>Internal module command implementation used by <see cref="PrimitiveModuleBase"/>.</summary>
public delegate PrimitiveDispatch PrimitiveCommand(
    PrimitiveContext context,
    JsonElement arguments,
    PrimitiveExecutionControl control,
    CancellationToken cancellationToken);

/// <summary>
/// Optional base for modules that want Core to validate and freeze their own
/// command/descriptor table. A module may implement <see cref="IPrimitiveModule"/>
/// directly to define entirely custom semantics.
/// </summary>
public abstract class PrimitiveModuleBase : IPrimitiveModule
{
    private static readonly PrimitiveDispatch Skipped = new(
        PrimitiveDispatchStatus.Skipped,
        new PrimitiveExecutionPolicy(false, false, null),
        Task.FromResult(PrimitiveResult.Empty));

    private readonly Dictionary<string, (PrimitiveDescriptor Descriptor, PrimitiveCommand Command)> _commands = new(StringComparer.Ordinal);
    private IReadOnlyCollection<PrimitiveDescriptor>? _descriptors;

    protected PrimitiveModuleBase(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains('.') ||
            !string.Equals(prefix, prefix.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("Primitive module prefixes must be non-empty, lowercase, dot-free identifiers.", nameof(prefix));
        Prefix = prefix;
    }

    public string Prefix { get; }

    public IReadOnlyCollection<PrimitiveDescriptor> Descriptors =>
        _descriptors ??= _commands.Values.Select(command => command.Descriptor).ToArray();

    /// <summary>Registers one supported command before the descriptor table is first observed.</summary>
    protected void Register(PrimitiveDescriptor descriptor, PrimitiveCommand command)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(command);
        if (_descriptors is not null)
            throw new InvalidOperationException($"Primitive module '{Prefix}' is already frozen.");
        var commandId = GetCommand(descriptor.TypeId);
        if (!_commands.TryAdd(commandId, (descriptor, command)))
            throw new InvalidOperationException($"Primitive '{descriptor.TypeId}' is already registered.");
    }

    public PrimitiveDispatch Dispatch(
        string command,
        PrimitiveContext context,
        JsonElement arguments,
        PrimitiveExecutionControl control,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(control);
        _ = Descriptors;
        return _commands.TryGetValue(command, out var registered)
            ? registered.Command(context, arguments, control, cancellationToken) ?? Skipped
            : Skipped;
    }

    public virtual void Dispose() { }

    private string GetCommand(string typeId)
    {
        var prefix = $"{Prefix}.";
        if (string.IsNullOrWhiteSpace(typeId) || !typeId.StartsWith(prefix, StringComparison.Ordinal) || typeId.Length == prefix.Length)
            throw new ArgumentException($"Primitive type IDs in module '{Prefix}' must start with '{prefix}'.", nameof(typeId));
        return typeId[prefix.Length..];
    }
}
