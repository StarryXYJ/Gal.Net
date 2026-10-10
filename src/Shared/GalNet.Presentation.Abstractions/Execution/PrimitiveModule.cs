using System.Collections.ObjectModel;

namespace GalNet.Presentation.Abstractions.Execution;

/// <summary>A game-scoped set of instruction implementations and their shared state.</summary>
public interface IPrimitiveModule : IDisposable, IAsyncDisposable
{
    IReadOnlyDictionary<string, PrimitiveImplementation> Instructions { get; }
}

public class PrimitiveModule : IPrimitiveModule
{
    public PrimitiveModule(IEnumerable<PrimitiveImplementation> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        var entries = new Dictionary<string, PrimitiveImplementation>(StringComparer.Ordinal);
        foreach (var instruction in instructions)
            if (!entries.TryAdd(instruction.Type, instruction))
                throw new ArgumentException($"Instruction '{instruction.Type}' is registered more than once.", nameof(instructions));
        Instructions = new ReadOnlyDictionary<string, PrimitiveImplementation>(entries);
    }

    public IReadOnlyDictionary<string, PrimitiveImplementation> Instructions { get; }
    public virtual void Dispose() => GC.SuppressFinalize(this);
    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
