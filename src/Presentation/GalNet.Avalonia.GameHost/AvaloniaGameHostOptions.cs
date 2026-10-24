using GalNet.Assets.Variants;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Content;

namespace GalNet.Avalonia.GameHost;

/// <summary>Project policies supplied to the default Avalonia player composition.</summary>
public sealed class AvaloniaGameHostOptions
{
    public AvaloniaGamePresentationOptions Presentation { get; init; } = new();
    public string GameTitle { get; init; } = "GalNet";
    public string NoGameMessage { get; init; } = "Select a game directory to start.";
    public required Func<(IResourceTypeCatalog Resources, IGalleryTypeCatalog Gallery)> CreateContentCatalogs { get; init; }
    public required Action<GameRegistration, AvaloniaGamePresentation, GamePage, GameContent> ConfigureExecution { get; init; }
    /// <summary>Optional borrowed runtime binding. Disposed before the run; no debugger dependency is required.</summary>
    public Func<GalNet.Runtime.Engine.GameEngine, IDisposable>? AttachDebugRun { get; init; }
    public Action<IAssetManager> ConfigureDecoders { get; init; } = assets =>
    {
        assets.RegisterDecoder<GalNet.Rendering.Scene.SceneTexture>("sprite", new GalNet.Rendering.Scene.SceneTextureAssetDecoder());
        assets.RegisterImageVariantsDecoder();
    };
    public Func<IAssetManager, CancellationToken, Task<IReadOnlyList<IGameFile>>> SelectTexturePreloads { get; init; } =
        (assets, token) => assets.GetFilesAsync("sprite", token);
    public Func<IAssetManager, CancellationToken, Task<EffectProgramResource[]>> SelectEffectPrograms { get; init; } =
        async (assets, token) => (await assets.GetFilesAsync("effect-program", token)).Select(file => new EffectProgramResource(file.Id)).ToArray();
}
