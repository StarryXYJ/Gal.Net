namespace GalNet.Core.Scene;

/// <summary>
/// Resolves opaque handles to currently active scene instances. A removed handle is immediately invalid.
/// </summary>
public interface ISceneInstanceManager
{
    IReadOnlyList<TInstance> GetAll<TInstance>() where TInstance : class, ISceneInstance;
    bool TryGet<TInstance>(string handleId, out TInstance instance) where TInstance : class, ISceneInstance;
    TInstance GetOrAdd<TInstance>(string handleId, Func<string, TInstance> factory) where TInstance : class, ISceneInstance;
    bool Remove<TInstance>(string handleId, out TInstance? instance) where TInstance : class, ISceneInstance;
    void Rebuild(IEnumerable<ISceneInstance> instances);
}
