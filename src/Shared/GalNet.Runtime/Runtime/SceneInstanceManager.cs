using GalNet.Core.Scene;

namespace GalNet.Runtime.Runtime;

/// <summary>Active scene instances indexed by their hidden handle IDs.</summary>
internal sealed class SceneInstanceManager : ISceneInstanceManager
{
    private readonly Dictionary<string, ISceneInstance> _instances = new(StringComparer.Ordinal);
    private readonly SceneState _sceneState;

    public SceneInstanceManager(SceneState sceneState)
    {
        _sceneState = sceneState;
        Rebuild(sceneState.Layers);
    }

    public IReadOnlyList<TInstance> GetAll<TInstance>() where TInstance : class, ISceneInstance =>
        _instances.Values.OfType<TInstance>().ToArray();

    public bool TryGet<TInstance>(string handleId, out TInstance instance) where TInstance : class, ISceneInstance
    {
        if (_instances.TryGetValue(handleId, out var candidate) && candidate is TInstance typed)
        {
            instance = typed;
            return true;
        }

        instance = null!;
        return false;
    }

    public TInstance GetOrAdd<TInstance>(string handleId, Func<string, TInstance> factory) where TInstance : class, ISceneInstance
        => GetOrAddCore(handleId, factory, persistLayer: true);

    public TInstance GetOrAddTransient<TInstance>(string handleId, Func<string, TInstance> factory) where TInstance : class, ISceneInstance
        => GetOrAddCore(handleId, factory, persistLayer: false);

    private TInstance GetOrAddCore<TInstance>(string handleId, Func<string, TInstance> factory, bool persistLayer) where TInstance : class, ISceneInstance
    {
        if (TryGet<TInstance>(handleId, out var existing)) return existing;
        if (_instances.TryGetValue(handleId, out var conflicting))
            throw new InvalidOperationException($"Scene handle '{handleId}' already belongs to {conflicting.GetType().Name}.");

        var created = factory(handleId);
        _instances.Add(handleId, created);
        if (persistLayer && created is Layer layer) _sceneState.Layers.Add(layer);
        return created;
    }

    public bool Remove<TInstance>(string handleId, out TInstance? instance) where TInstance : class, ISceneInstance
    {
        if (!TryGet<TInstance>(handleId, out var existing))
        {
            instance = null;
            return false;
        }

        _instances.Remove(handleId);
        if (existing is Layer layer) _sceneState.Layers.Remove(layer);
        instance = existing;
        return true;
    }

    public void Rebuild(IEnumerable<ISceneInstance> instances)
    {
        _instances.Clear();
        foreach (var instance in instances)
            if (!string.IsNullOrWhiteSpace(instance.Id)) _instances[instance.Id] = instance;
    }
}
