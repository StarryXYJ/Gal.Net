using GalNet.Core.Entry;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Explicit composition of data contracts and game-scoped execution modules.</summary>
public sealed class GameRegistration : IDisposable, IAsyncDisposable
{
    private readonly List<IEntryModule> _schemas = [];
    private readonly List<IPrimitiveModule> _implementations = [];
    private IEntryCatalog? _catalog;
    private bool _frozen;

    public GameRegistration RegisterSchema(IEntryModule schema)
    {
        ObjectDisposedException.ThrowIf(_frozen, this);
        ArgumentNullException.ThrowIfNull(schema);
        if (_catalog is not null) throw new InvalidOperationException("A supplied project catalog cannot be extended with additional schemas.");
        _schemas.Add(schema);
        return this;
    }

    public GameRegistration RegisterImplementation(IPrimitiveModule implementation)
    {
        ObjectDisposedException.ThrowIf(_frozen, this);
        ArgumentNullException.ThrowIfNull(implementation);
        _implementations.Add(implementation);
        return this;
    }

    public GameRegistration RegisterInstruction(string type, Func<GalNet.Core.Primitives.PrimitiveCreateContext, GalNet.Core.Primitives.PrimitiveInstance> factory) =>
        RegisterImplementation(new PrimitiveModule([new PrimitiveImplementation(type, factory)]));

    public GameRegistration UseCatalog(IEntryCatalog catalog)
    {
        ObjectDisposedException.ThrowIf(_frozen, this);
        ArgumentNullException.ThrowIfNull(catalog);
        if (_catalog is not null || _schemas.Count != 0) throw new InvalidOperationException("Select either a supplied project catalog or schema modules once.");
        _catalog = catalog;
        return this;
    }

    public IEntryCatalog BuildCatalog() => _catalog ?? new TargetProfileEntryCatalog(_schemas);

    /// <summary>Freezes registration and transfers module ownership to the view.</summary>
    public CompositeGameView BuildView()
    {
        ObjectDisposedException.ThrowIf(_frozen, this);
        var view = new CompositeGameView(BuildCatalog(), _implementations);
        _frozen = true;
        _implementations.Clear();
        return view;
    }

    public void Dispose()
    {
        if (_frozen) return;
        _frozen = true;
        List<Exception>? errors = null;
        foreach (var module in _implementations.AsEnumerable().Reverse().Distinct())
            try { module.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        _implementations.Clear();
        if (errors is not null) throw new AggregateException(errors);
    }

    public async ValueTask DisposeAsync()
    {
        if (_frozen) return;
        _frozen = true;
        List<Exception>? errors = null;
        foreach (var module in _implementations.AsEnumerable().Reverse().Distinct())
            try { await module.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { (errors ??= []).Add(error); }
        _implementations.Clear();
        if (errors is not null) throw new AggregateException(errors);
    }
}
