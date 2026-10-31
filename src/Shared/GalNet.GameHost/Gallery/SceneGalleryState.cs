using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Variable;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Progress;
using GalNet.Runtime.Variables;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.GameHost.Gallery;

/// <summary>Read-only baseline from the player; all replay writes stay local and cannot reach a profile store.</summary>
public sealed class SceneGalleryState : IDisposable
{
    private SceneGalleryState(PlayerVariableService variables, IGameProgressService progress) { Variables = variables; Progress = progress; }
    public PlayerVariableService Variables { get; }
    public IGameProgressService Progress { get; }
    public static async Task<SceneGalleryState> CreateAsync(PlayerVariableService source, IGameProgressService progress,
        GalleryCatalog catalog, CancellationToken cancellationToken = default)
    {
        var variables = await PlayerVariableService.CreateAsync(new ReplayStore(source.GetSnapshot(VariableScope.Player)), cancellationToken);
        try { variables.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(catalog)); return new(variables, new ReplayProgress(progress)); }
        catch { variables.Dispose(); throw; }
    }
    public void Dispose() => Variables.Dispose();
    private sealed class ReplayProgress(IGameProgressService source) : IGameProgressService
    {
        private readonly ReadProgressIndex _local = new();
        public bool IsRead(string groupId, string entryId) => _local.IsRead(groupId, entryId) || source.IsRead(groupId, entryId);
        public void MarkRead(string groupId, string entryId) => _local.MarkRead(groupId, entryId);
    }
    private sealed class ReplayStore(IReadOnlyDictionary<string, GalVariable> source) : IPlayerVariableStore
    {
        private readonly IReadOnlyDictionary<string, GalVariable> _initial = source.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        public Task<IReadOnlyDictionary<string, GalVariable>> LoadAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.FromResult(_initial); }
        public Task SaveAsync(IReadOnlyDictionary<string, GalVariable> variables, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
}
