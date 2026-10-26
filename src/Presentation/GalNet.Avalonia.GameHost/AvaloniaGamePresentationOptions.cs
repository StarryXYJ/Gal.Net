using GalNet.Avalonia.GameView.Presentation;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Media.LibVlc;
using GalNet.Rendering.Scene;
using LibVLCSharp.Shared;

namespace GalNet.Avalonia.GameHost;

/// <summary>Factories transfer ownership of newly created adapters to the presentation.</summary>
public sealed class AvaloniaGamePresentationOptions
{
    public Func<IReadOnlyDictionary<string, AssetHandle<SceneTexture>>, IGamePageLayerFactory> CreateLayers { get; init; } =
        textures => new DefaultGameLayerFactory(textures);
    public Func<IAssetManager, IAssetFileMaterializer, LibVLC?, IAudioPlaybackHost> CreateAudioHost { get; init; } =
        (assets, files, shared) => new LibVlcAudioPlaybackHost(assets, files: files, sharedLibVlc: shared);
}
