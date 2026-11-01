using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Game.Controls;
using GalNet.Sample.CrossPlatform;
using Microsoft.Extensions.DependencyInjection;
using ProjectGame = GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel;
using ProjectTitle = GalNet.Sample.CrossPlatform.ViewModels.TitlePageViewModel;

internal static class Program
{
    internal static string Content = "";
    internal static string Output = "";
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("content-directory evidence-directory");
        Content = Path.GetFullPath(args[0]); Output = Path.GetFullPath(args[1]);
        if (Directory.Exists(Output)) throw new IOException("Use a new evidence directory.");
        Directory.CreateDirectory(Output);
        return AppBuilder.Configure<NativeProbeApplication>().UsePlatformDetect().WithInterFont().UseSkia()
            .With(GalNet.Avalonia.Themes.GameTypography.CreateFontManagerOptions("fonts:Inter#Inter"))
            .StartWithClassicDesktopLifetime([]);
    }
}

internal sealed class NativeProbeApplication : Application
{
    private AvaloniaPlayerHost? _host;
    private Window _window = null!;
    private readonly Dictionary<string, object?> _report = new()
    {
        ["passed"] = false,
        ["interactionDriver"] = "Actual template controls' bound commands on the native UI thread",
        ["headless"] = false
    };
    public override void Initialize()
    {
        Styles.Add(new Semi.Avalonia.SemiTheme());
        Styles.Add(new Ursa.Themes.Semi.UrsaSemiTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _window = new GalNet.Sample.CrossPlatform.Views.MainWindow();
        desktop.MainWindow = _window;
        _window.Show();
        Dispatcher.UIThread.Post(async () => await RunAsync(desktop));
        base.OnFrameworkInitializationCompleted();
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private T Service<T>() where T : notnull => _host!.Services.GetRequiredService<T>();
    private async Task OpenAsync(string profile)
    {
        Console.WriteLine("Creating native scope");
        _host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services => SampleCustomization.ConfigureServices(services));
        _window.Content = _host.View;
        _host.BindWindow(_window);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var initialization = _host.InitializeAsync(Program.Content, profile);
        var language = Service<LanguageSelectionViewModel>();
        while (!initialization.IsCompleted)
        {
            if (language.ConfirmCommand.CanExecute(null)) language.ConfirmCommand.Execute(null);
            await Task.Delay(20, cancellation.Token);
        }
        Require(await initialization, _host.Session.StatusMessage);
        Console.WriteLine("Native scope initialized");
        _window.UpdateLayout();
        await Task.Delay(150);
    }
    private async Task InvokeAsync(ICommand command)
    {
        _window.UpdateLayout();
        var button = _window.GetVisualDescendants().OfType<Button>().Single(control => ReferenceEquals(control.Command, command));
        Require(button.IsEffectivelyVisible && button.IsEnabled, "Bound button is not usable.");
        await ((IAsyncRelayCommand)button.Command!).ExecuteAsync(button.CommandParameter);
        _window.UpdateLayout();
        await Task.Delay(100);
    }
    private SaveSlotCard FirstCard()
    {
        _window.UpdateLayout();
        return _window.GetVisualDescendants().OfType<SaveSlotCard>().Single(card => card.SlotIndex == 0 && card.IsEffectivelyVisible);
    }
    private async Task SceneReadyAsync()
    {
        var state = Service<GamePresentationState>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((!state.IsGameUiReady || state.Layers.Count == 0) && DateTime.UtcNow < deadline) await Task.Delay(25);
        Require(_host!.Session.IsPlaying && state.IsGameUiReady && state.Layers.Count > 0, "No active rendered story.");
        _window.UpdateLayout();
        await Task.Delay(200);
    }
    private async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var code = 1;
        try
        {
            var handle = _window.TryGetPlatformHandle();
            Require(handle is not null && handle.Handle != IntPtr.Zero, "A real native window is required.");
            _report["platformHandle"] = handle!.HandleDescriptor;
            _report["operatingSystem"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            var profile = Path.Combine(Program.Output, "profile");
            await OpenAsync(profile);
            Console.WriteLine("Starting project NewGame command");
            await InvokeAsync(Service<ProjectTitle>().StartNewGameCommand);
            await SceneReadyAsync();
            Console.WriteLine("Story ready; capturing scene");
            var surface = Service<GamePage>();
            await File.WriteAllBytesAsync(Path.Combine(Program.Output, "story-before-save.png"), surface.CaptureScenePng());
            await InvokeAsync(Service<ProjectGame>().OpenSaveSlotsCommand);
            Console.WriteLine("Saving visible card");
            var card = FirstCard();
            await ((IAsyncRelayCommand)card.Command!).ExecuteAsync(null);
            await SceneReadyAsync();
            var slot = _host!.Session.SaveSlots[0];
            Require(!slot.IsEmpty && slot.PreviewImage is { Length: > 0 }, "Save did not contain a real scene preview.");
            _report["previewBytes"] = slot.PreviewImage!.Length;
            await InvokeAsync(Service<ProjectGame>().OpenLoadSlotsCommand);
            card = FirstCard();
            await ((IAsyncRelayCommand)card.RenameCommand!).ExecuteAsync("Native template memory");
            Require(_host.Session.SaveSlots[0].Description == "Native template memory", "Rename did not update the slot.");
            await ((IAsyncRelayCommand)card.Command!).ExecuteAsync(null);
            await SceneReadyAsync();
            Require(Service<IGameNavigationService>().CurrentViewModel is ProjectGame, "Load did not mount the project game page.");
            await _host.SuspendAsync();
            Require(Service<IGameNavigationService>().CurrentViewModel is ProjectTitle, "Suspend did not return to title.");
            _window.Content = null; _window.Content = _host.View;
            _window.UpdateLayout();
            Require(_window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "NewGameButton"), "Remount lost the current page.");
            _window.Content = null;
            await _host.DisposeAsync(); _host = null;
            await OpenAsync(profile);
            Console.WriteLine("Scope reopened");
            Require(_host!.Session.SaveSlots[0].Description == "Native template memory", "Slot rename was not persisted across scopes.");
            await InvokeAsync(Service<ProjectTitle>().OpenLoadSlotsCommand);
            await ((IAsyncRelayCommand)FirstCard().Command!).ExecuteAsync(null);
            await SceneReadyAsync();
            await File.WriteAllBytesAsync(Path.Combine(Program.Output, "story-after-reopen.png"), Service<GamePage>().CaptureScenePng());
            var confirmation = _host.ConfirmExitAsync();
            Require(Service<GamePresentationState>().IsInteractionBlocked, "Confirmation did not block gameplay.");
            _host.View.FindControl<Button>("CancelExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!await confirmation && !Service<GamePresentationState>().IsInteractionBlocked, "Cancel did not restore interaction.");
            confirmation = _host.ConfirmExitAsync();
            _host.View.FindControl<Button>("ConfirmExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(await confirmation, "Exit was not confirmed.");
            await _host.DisposeAsync(); _host = null;
            _report["saveLoadRenameReopen"] = true;
            _report["suspendRemount"] = true;
            _report["confirmationCancelAndAccept"] = true;
            _report["profileFiles"] = Directory.GetFiles(profile, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(profile, path)).ToArray();
            _report["passed"] = true;
            code = 0;
        }
        catch (Exception error) { _report["error"] = error.ToString(); }
        finally
        {
            if (_host is not null) { try { await _host.DisposeAsync(); } catch (Exception error) { _report["cleanupError"] = error.ToString(); } }
            await File.WriteAllTextAsync(Path.Combine(Program.Output, "native-flow.json"), JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true }));
            _window.Close(); desktop.Shutdown(code);
        }
    }
}
