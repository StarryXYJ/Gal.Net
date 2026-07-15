using GalNet.Control.View;
using GalNet.Control.Views;
using GalNet.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using GalNet.Control.Services;
using GalNet.Control.UI;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.UI;
using Ursa.Controls;
using Avalonia.Controls;

namespace GalNet.Control.ViewModels;

public sealed class GameFlowFactory : IGameFlowFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<IGameScreenNavigator, GameRunViewModel> _runs = [];

    public GameFlowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions options) => new(this, options);

    public object BuildScreen(IGameScreenNavigator navigator, string screen, object? parameter, GameFlowOptions options)
    {
        return screen.ToLowerInvariant() switch
        {
            "title" => CreateStart(navigator, options, options.Ui.Title),
            "game" => CreateRun(navigator, options, options.Ui.Game, parameter),
            "settings" => CreateSettings(navigator, options.Ui.Settings),
            "save-load" => CreateSaveLoad(navigator, options, parameter as string == "save" ? SaveLoadMode.Save : SaveLoadMode.Load, options.Ui.SaveLoad),
            "gallery" => CreateGallery(navigator, options, options.Ui.Gallery),
            _ => throw new InvalidOperationException($"Unknown built-in screen '{screen}'.")
        };
    }

    public GameStartViewModel CreateStart(IGameScreenNavigator navigator, GameFlowOptions options, TitleUiConfiguration config) =>
        new(navigator, _serviceProvider.GetService<IGameExitService>(), options,
            options.SaveService ?? _serviceProvider.GetService<ISaveService>(), config);

    public GameRunViewModel CreateRun(IGameScreenNavigator navigator, GameFlowOptions options, GameUiConfiguration config, object? parameter)
    {
        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        var screen = new GameScreenViewModel(config);
        var gameView = new DefaultGameView(settings.GetSnapshot(), config, screen);
        var variableService = options.VariableService ?? _serviceProvider.GetService<IVariableService>();
        var save = options.SaveService ?? _serviceProvider.GetService<ISaveService>();
        var progress = options.ProgressService ?? _serviceProvider.GetService<IGameProgressService>();
        var runOptions = parameter is GalNet.Core.Runtime.GameSnapshot snapshot ? options with { RestoreSnapshot = snapshot } :
            parameter is string startNode ? options with { StartNodeId = startNode, IsGalleryPresentation = true } : options;
        var run = new GameRunViewModel(gameView, settings, variableService, options.GameContentProvider, save, progress, runOptions, () =>
        {
            _ = navigator.NavigateAsync("title");
        });
        _runs[navigator] = run;
        run.CommandRequested += command => HandleRunCommand(command, run, navigator, options, save);
        options.RunCreated?.Invoke(run);
        return run;
    }

    public SettingsViewModel CreateSettings(IGameScreenNavigator navigator, SettingsUiConfiguration config) =>
        new(_serviceProvider.GetRequiredService<ISettingsService>(), navigator, config);

    public SaveLoadViewModel CreateSaveLoad(IGameScreenNavigator navigator, GameFlowOptions options, SaveLoadMode mode, SaveLoadUiConfiguration config)
    {
        var saves = options.SaveService ?? _serviceProvider.GetService<ISaveService>();
        _runs.TryGetValue(navigator, out var run);
        return new SaveLoadViewModel(
            navigator,
            saves,
            mode,
            config,
            loadAction: async slot =>
            {
                if (saves is null) return;
                var snapshot = await saves.LoadAsync(slot);
                if (snapshot is null) return;
                if (run is not null) await run.DisposeAsync();
                await navigator.NavigateAsync("game", snapshot);
            },
            saveAction: run is null ? null : async slot => await run.SaveCurrentAsync(slot));
    }
    public GalleryViewModel CreateGallery(IGameScreenNavigator navigator, GameFlowOptions options, GalleryUiConfiguration config) =>
        new(navigator, options, options.ProgressService ?? _serviceProvider.GetService<IGameProgressService>(), config);

    private void HandleRunCommand(string command, GameRunViewModel run, IGameScreenNavigator navigator, GameFlowOptions options, ISaveService? saves)
    {
        switch (command)
        {
            case "settings": _ = navigator.NavigateAsync("settings"); break;
            case "load": _ = navigator.NavigateAsync("save-load"); break;
            case "save": _ = navigator.NavigateAsync("save-load", "save"); break;
            case "menu":
                _ = run.DisposeAsync();
                _runs.Remove(navigator);
                _ = navigator.NavigateAsync("title");
                break;
            case "screenshot": _ = ShowScreenshotAsync(run); break;
        }
    }

    private static async Task ShowScreenshotAsync(GameRunViewModel run)
    {
        var vm = new ScreenshotDialogViewModel(async (directory, includeUi) =>
        {
            if (!Directory.Exists(directory)) return "The directory does not exist.";
            try
            {
                var path = Path.Combine(directory, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                await File.WriteAllBytesAsync(path, await run.GameView.CapturePngAsync(includeUi));
                return null;
            }
            catch (Exception ex) { return $"Could not save screenshot: {ex.Message}"; }
        });
        await OverlayDialog.ShowCustomAsync<ScreenshotDialog, ScreenshotDialogViewModel, bool>(vm);
    }
}
