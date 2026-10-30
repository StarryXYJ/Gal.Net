using GalNet.Assets.Variants;
using GalNet.Avalonia.GameView.Page;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.GameHost.Content;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Avalonia.GameHost;

/// <summary>Project policies supplied to the default Avalonia player composition.</summary>
public sealed class AvaloniaGameHostOptions
{
    public GalNet.Storage.FileSystem.GameLaunchPolicy LaunchPolicy { get; init; } =
        GalNet.Storage.FileSystem.GameLaunchPolicy.FromAssembly(System.Reflection.Assembly.GetEntryAssembly() ?? typeof(AvaloniaGameHostOptions).Assembly);
    public GameContentLoadOptions Content { get; init; } = new();
    public Func<ProjectConfiguration, AvaloniaProjectOptions> SelectProjectOptions { get; init; } = _ => new();
    public AvaloniaGamePresentationOptions Presentation { get; init; } = new();
    public bool ShowTrackAudioControls { get; init; }
    public string GameTitle { get; init; } = "GalNet";
    public string NoGameMessage { get; init; } = "Select a game directory to start.";
    public required Func<(IResourceTypeCatalog Resources, IGalleryTypeCatalog Gallery)> CreateContentCatalogs { get; init; }
    public required Action<GameRegistration, AvaloniaGamePresentation, GamePage, GameContent> ConfigureExecution { get; init; }
    /// <summary>Project binding attached after preparation and released after the engine stops.</summary>
    public Func<GalNet.Runtime.Engine.GameEngine, IDisposable>? AttachRun { get; init; }
    /// <summary>Profile services attached after onboarding/settings; released before player storage.</summary>
    public Func<AvaloniaPlayerContext, IDisposable>? AttachPlayer { get; init; }
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

/// <summary>Values consumed by the default player, projected once by the project's composition root.</summary>
public sealed record AvaloniaProjectOptions
{
    public int SaveSlotCount { get; init; } = 60;
    public string DefaultCulture { get; init; } = "en-US";
    public AudioConfiguration Audio { get; init; } = AudioConfiguration.Empty;
}

/// <summary>Borrowed profile resources for project services. The session retains ownership.</summary>
public sealed record AvaloniaPlayerContext(GalNet.GameHost.PlayerSession Player, GameContent Content, string ProfileDirectory);
