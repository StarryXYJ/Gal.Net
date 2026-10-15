namespace GalNet.Presentation.Abstractions.Execution;

/// <summary>A game-scoped set of instruction implementations and their shared state.</summary>
public interface IPrimitiveModule : IDisposable, IAsyncDisposable
{
    void Register(PrimitiveRegistry registry);
}

public class PrimitiveModule : IPrimitiveModule
{
    private readonly Action<PrimitiveRegistry> _register;

    public PrimitiveModule(Action<PrimitiveRegistry> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _register = register;
    }

    public void Register(PrimitiveRegistry registry) => _register(registry);
    public virtual void Dispose() => GC.SuppressFinalize(this);
    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
