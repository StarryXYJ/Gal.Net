using GalNet.Control.View;
using GalNet.Core.Services;
using GalNet.Control.Abstraction.UI;
using Microsoft.Extensions.DependencyInjection;
using GalNet.Control.Services;
using GalNet.Control.Views;
using GalNet.Control.UI;
using Ursa.Controls;

namespace GalNet.Control.ViewModels;

public sealed class GameFlowFactory : IGameFlowFactory
{
    private readonly IServiceProvider _serviceProvider;

    public GameFlowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions options) =>
        new(_serviceProvider, _serviceProvider.GetRequiredService<IScreenTemplateRegistry>(), options);

    public GameStartViewModel CreateStart(IGameScreenNavigator navigator, GameFlowOptions options) =>
        new(navigator, _serviceProvider.GetService<IGameExitService>(), options,
            options.SaveService ?? _serviceProvider.GetService<ISaveService>());

    public GameRunViewModel CreateRun(IGameScreenNavigator navigator, GameFlowOptions options, WidgetBuildContext widgetContext, GameScreenConfiguration config)
    {
        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        var factory = _serviceProvider.GetRequiredService<IWidgetFactory>();
        WidgetHostViewModel Host(string role, string fallback, string category) => new(factory, widgetContext,
            config.Widgets.TryGetValue(role, out var id) ? id : fallback, category);
        var screen = new GameScreenViewModel(
            Host("AutoToggle", "toggle.command", "toggle"), Host("QuickToggle", "toggle.command", "toggle"),
            Host("SaveButton", "button.command", "button"), Host("LoadButton", "button.command", "button"),
            Host("SettingsButton", "button.command", "button"), Host("MenuButton", "button.command", "button"),
            Host("ScreenshotButton", "button.command", "button"), Host("HideButton", "button.command", "button"));
        var gameView = new DefaultGameView(settings.GetSnapshot(), factory, widgetContext, screen);
        var variableService = options?.VariableService ?? _serviceProvider.GetService<IVariableService>();
        var save = options.SaveService ?? _serviceProvider.GetService<ISaveService>();
        var progress = options.ProgressService ?? _serviceProvider.GetService<IGameProgressService>();
        var run = new GameRunViewModel(gameView, settings, variableService, options.GameContentProvider, save, progress, options, () =>
        {
            _ = navigator.NavigateAsync("title");
        });
        run.CommandRequested += command => HandleRunCommand(command, run, navigator, options, save);
        options.RunCreated?.Invoke(run);
        return run;
    }

    public SettingsViewModel CreateSettings(IGameScreenNavigator navigator) =>
        new(_serviceProvider.GetRequiredService<ISettingsService>(), navigator);

    public SaveLoadViewModel CreateSaveLoad(IGameScreenNavigator navigator, GameFlowOptions options, SaveLoadMode mode, Func<int, Task>? load = null, Func<int, Task>? save = null) =>
        new(navigator, options.SaveService ?? _serviceProvider.GetService<ISaveService>(), mode, load, save);
    public GalleryViewModel CreateGallery(IGameScreenNavigator navigator, GameFlowOptions options) =>
        new(navigator, options, options.ProgressService ?? _serviceProvider.GetService<IGameProgressService>());

    private void HandleRunCommand(string command, GameRunViewModel run, IGameScreenNavigator navigator, GameFlowOptions options, ISaveService? saves)
    {
        switch (command)
        {
            case "settings": _ = navigator.NavigateAsync("settings"); break;
            case "load": _ = navigator.NavigateAsync("save-load"); break;
            case "save": _ = navigator.NavigateAsync("save-load", "save"); break;
            case "menu":
                _ = run.DisposeAsync();
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
