using GalNet.Core.Assets;

namespace GalNet.Rendering.Scene;

/// <summary>One effect's acquired texture leases, released on stop, replacement or failed preparation.</summary>
public sealed class ShaderTextureBindings : IDisposable
{
    private readonly Dictionary<string, AssetHandle<SceneTexture>> _leases = new(StringComparer.Ordinal);
    public void Add(string parameter, AssetHandle<SceneTexture> lease) => _leases.Add(parameter, lease);
    public SceneTexture? Get(string parameter) => _leases.GetValueOrDefault(parameter)?.Value;
    public void Dispose()
    {
        foreach (var lease in _leases.Values) lease.Dispose();
        _leases.Clear();
    }
}
