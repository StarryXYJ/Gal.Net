using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Assets;
using GalNet.Media.Miniaudio;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;

namespace GalNet.Avalonia.GameHost;

/// <summary>Factories transfer ownership of newly created adapters to the presentation.</summary>
public sealed class AvaloniaGamePresentationOptions
{
    public Func<IReadOnlyDictionary<string, AssetHandle<SceneTexture>>, IGamePageLayerFactory> CreateLayers { get; init; } =
        textures => new DefaultGameLayerFactory(textures);
    public Func<IAssetManager, IAssetFileMaterializer, IAudioPlaybackHost> CreateAudioHost { get; init; } =
        (assets, files) => new MiniaudioAudioPlaybackHost(files);
    /// <summary>When omitted, uses the default LibVLC pixel presenter. A custom presenter owns its platform resources.</summary>
    public Func<IAssetManager, GamePageViewModel, IAssetFileMaterializer, IVideoPresenter>? CreateVideoPresenter { get; init; }
}
