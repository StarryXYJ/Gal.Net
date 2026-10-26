using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;
using GalNet.Runtime.Variables;

internal static class BuiltinTestRuntime
{
    public static StateCatalog Catalog { get; } = CreateCatalog();
    private static StateCatalog CreateCatalog()
    { using var view = new GameRegistration().AddBuiltins().BuildView(); return view.StateCatalog; }
    public static GameRuntime Create(ITextResolver? resolver, string rootNodeId = "",
        IVariableService? variableService = null) => new(resolver, rootNodeId, variableService, Catalog);
    public static GameState State(GameSnapshot snapshot) => GameState.Restore(Catalog, snapshot.States);
}
