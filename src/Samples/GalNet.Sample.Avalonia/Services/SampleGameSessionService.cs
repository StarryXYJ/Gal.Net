using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Media.LibVlc;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Project launch arguments and debug-only save policies.</summary>
internal sealed partial class SampleGameSessionService(
    GamePageViewModel gameplay, GamePage page, IAvaloniaPlayerSettings settings,
    IPlayerLocalization localization, IPlayerStartup startup, LibVlcRuntime mediaRuntime, IServiceProvider services)
    : AvaloniaGameSession(CreateOptions(services), gameplay, page, settings, localization, startup, mediaRuntime)
{
    public Task<bool> InitializeAsync(GameLaunchOptions options,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default) =>
        InitializeAsync(options.GameDirectory, options.ProfileDirectory, selectLanguage, cancellationToken);

    private static AvaloniaGameHostOptions CreateOptions(IServiceProvider services)
    {
#if GALNET_GAME_DEBUG
        return SampleCustomization.CreateHostOptions(App.LaunchOptions.DebugInvitation is null ? null :
            engine => services.GetRequiredService<GalNet.Sample.Avalonia.Debug.SampleExternalDebug>().Attach(engine));
#else
        return SampleCustomization.CreateHostOptions();
#endif
    }
}
