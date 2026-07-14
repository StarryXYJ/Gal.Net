using GalNet.Control.View;
using GalNet.Core.Services;
using GalNet.Core.UI;
using Microsoft.Extensions.DependencyInjection;
using GalNet.Control.Services;
using GalNet.Control.Views;
using Ursa.Controls;

namespace GalNet.Control.ViewModels;

public sealed class GameFlowFactory : IGameFlowFactory
{
    private readonly IServiceProvider _serviceProvider;

    public GameFlowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions? options = null) =>
        new(parentNavigation, this, _serviceProvider.GetRequiredService<IScreenTemplateRegistry>(), options);

    public GameStartViewModel CreateStart(INavigationService navigation, GameFlowOptions? options = null) =>
        new(navigation, this, _serviceProvider.GetService<IGameExitService>(), options,
            options?.SaveService ?? _serviceProvider.GetService<ISaveService>(),
            options is null ? null : new UI.UiColorPalette(options.UiProjectProvider));

    public GameRunViewModel CreateRun(INavigationService navigation, GameFlowOptions? options = null)
    {
        var gameView = _serviceProvider.GetRequiredService<DefaultGameView>();
        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        var variableService = options?.VariableService ?? _serviceProvider.GetService<IVariableService>();
        ArgumentNullException.ThrowIfNull(options);
        var save = options.SaveService ?? _serviceProvider.GetService<ISaveService>();
        var progress = options.ProgressService ?? _serviceProvider.GetService<IGameProgressService>();
        var run = new GameRunViewModel(gameView, settings, variableService, options.GameContentProvider, save, progress, options, () =>
        {
            navigation.Clear();
            navigation.NavigateTo(CreateStart(navigation, options));
        });
        run.CommandRequested += command => HandleRunCommand(command, run, navigation, options, save);
        options?.RunCreated?.Invoke(run);
        return run;
    }

    public SettingsViewModel CreateSettings(INavigationService navigation) =>
        new(_serviceProvider.GetRequiredService<ISettingsService>(), navigation);

    public SaveLoadViewModel CreateSaveLoad(INavigationService navigation, GameFlowOptions? options, SaveLoadMode mode, Func<int, Task>? load = null, Func<int, Task>? save = null) =>
        new(navigation, options?.SaveService ?? _serviceProvider.GetService<ISaveService>(), mode, load, save);
    public GalleryViewModel CreateGallery(INavigationService navigation, GameFlowOptions? options) =>
        new(navigation, this, options, options?.ProgressService ?? _serviceProvider.GetService<IGameProgressService>());

    private void HandleRunCommand(string command, GameRunViewModel run, INavigationService navigation, GameFlowOptions? options, ISaveService? saves)
    {
        switch (command)
        {
            case "settings": navigation.NavigateTo(CreateSettings(navigation)); break;
            case "load": navigation.NavigateTo(CreateSaveLoad(navigation, options, SaveLoadMode.Load, async slot =>
                {
                    var snapshot = saves is null ? null : await saves.LoadAsync(slot);
                    if (snapshot is not null) { navigation.Clear(); navigation.NavigateTo(CreateRun(navigation, options with { RestoreSnapshot = snapshot })); }
                })); break;
            case "save": navigation.NavigateTo(CreateSaveLoad(navigation, options, SaveLoadMode.Save, save: async slot =>
                {
                    if (await run.SaveCurrentAsync(slot)) navigation.GoBack();
                })); break;
            case "menu":
                _ = run.DisposeAsync();
                navigation.Clear(); navigation.NavigateTo(CreateStart(navigation, options));
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
