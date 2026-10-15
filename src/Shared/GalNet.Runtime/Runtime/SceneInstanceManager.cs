using GalNet.Core.Runtime;
using GalNet.Core.Scene;

namespace GalNet.Runtime.Runtime;

/// <summary>Derived handle lookup over canonical state plus ephemeral execution controls.</summary>
internal sealed class SceneInstanceManager(GameState state) : ISceneInstanceManager
{
    private readonly Dictionary<string, ISceneInstance> _transient = new(StringComparer.Ordinal);
    public IReadOnlyList<T> GetAll<T>() where T : class, ISceneInstance => state.Read(() =>
        state.SceneObjects<T>().Concat(_transient.Values.OfType<T>()).ToArray());
    public bool TryGet<T>(string id, out T instance) where T : class, ISceneInstance
    {
        lock (state.SyncRoot)
        {
            if (_transient.TryGetValue(id, out var value)) { instance = value as T ?? null!; return instance is not null; }
            return state.TryGetScene(id, out instance);
        }
    }
    public T GetOrAdd<T>(string id, Func<string, T> factory) where T : class, ISceneInstance
    {
        lock (state.SyncRoot)
        {
            if (TryGet<T>(id, out var existing)) return existing;
            if (_transient.ContainsKey(id)) throw new InvalidOperationException($"Scene handle '{id}' is already in use.");
            var value = factory(id);
            state.Table<T>().Add(value);
            return value;
        }
    }
    public T GetOrAddTransient<T>(string id, Func<string, T> factory) where T : class, ISceneInstance
    {
        lock (state.SyncRoot)
        {
            if (TryGet<T>(id, out var existing)) return existing;
            if (GetAll<ISceneInstance>().Any(item => item.Id == id)) throw new InvalidOperationException($"Scene handle '{id}' is already in use.");
            var value = factory(id);
            _transient.Add(id, value);
            return value;
        }
    }
    public bool Remove<T>(string id, out T? instance) where T : class, ISceneInstance
    {
        lock (state.SyncRoot)
        {
            if (!TryGet<T>(id, out var existing)) { instance = null; return false; }
            instance = existing;
            if (_transient.Remove(id)) return true;
            return state.Table<T>().Remove(id);
        }
    }
}
