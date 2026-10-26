using System.Runtime.CompilerServices;
using GalNet.Core.Runtime;
using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Primitives.Builtins;

/// <summary>Optional scene capability. Each state root owns independent transient controls.</summary>
public static class BuiltinScene
{
    public const string KeySpace = "builtin.scene";
    private static readonly ConditionalWeakTable<GameState, SceneInstances> Instances = new();
    public static SceneInstances Scene(this IGameContext context) => For(context.State);
    public static SceneInstances For(GameState state) => Instances.GetValue(state, root => new(root));
}

public sealed class SceneInstances(GameState state)
{
    private readonly Dictionary<string, ISceneInstance> _transient = new(StringComparer.Ordinal);
    public IReadOnlyList<T> GetAll<T>() where T : class, ISceneInstance => state.Read(() =>
        state.IdentityObjects<T>(BuiltinScene.KeySpace).Concat(_transient.Values.OfType<T>()).ToArray());
    public bool TryGet<T>(string id, out T instance) where T : class, ISceneInstance
    {
        T? found = null;
        var exists = state.Read(() =>
        {
            if (_transient.TryGetValue(id, out var value)) { found = value as T; return found is not null; }
            return state.TryGetIdentity(BuiltinScene.KeySpace, id, out found);
        });
        instance = found!; return exists;
    }
    public T GetOrAdd<T>(string id, Func<string, T> factory) where T : class, ISceneInstance => state.Update(() =>
    {
        if (TryGet<T>(id, out var existing)) return existing;
        if (_transient.ContainsKey(id)) throw new InvalidOperationException($"Scene handle '{id}' is already in use.");
        var value = factory(id); state.Table<T>().Add(value); return value;
    });
    public T GetOrAddTransient<T>(string id, Func<string, T> factory) where T : class, ISceneInstance => state.Update(() =>
    {
        if (TryGet<T>(id, out var existing)) return existing;
        if (GetAll<ISceneInstance>().Any(item => item.Id == id)) throw new InvalidOperationException($"Scene handle '{id}' is already in use.");
        var value = factory(id); _transient.Add(id, value); return value;
    });
    public bool Remove<T>(string id, out T? instance) where T : class, ISceneInstance
    {
        T? removed = null;
        var result = state.Update(() =>
        {
            if (!TryGet<T>(id, out var existing)) return false;
            removed = existing;
            return _transient.Remove(id) || state.Table<T>().Remove(id);
        });
        instance = removed; return result;
    }
}
