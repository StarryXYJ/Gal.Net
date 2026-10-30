using Avalonia.Controls;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Avalonia.GameHost.Views;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly IAvaloniaPlayerSettings _settings;
    private bool _bindingsDetached;
    public PlayerView Player => PlayerControl;

    public MainWindow(GameShell shell, IServiceProvider services)
    {
        InitializeComponent();
        _settings = services.GetRequiredService<IAvaloniaPlayerSettings>();
        _settings.BindWindow(this);
        Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        GalNet.Sample.Player.SamplePlayerServices.ConfigureView(PlayerControl);
        PlayerControl.Attach(shell, services.GetRequiredService<LanguageSelectionViewModel>());
        InitializeDebug(services);
        Closed += OnClosed;
    }

    public Task<bool> ConfirmExitAsync() => PlayerControl.ConfirmExitAsync();

    public Task<string?> SelectLanguageAsync(string preferred, CancellationToken cancellationToken) =>
        PlayerControl.SelectLanguageAsync(preferred, cancellationToken);

    public void CompleteStartup(string? error = null)
    {
        PlayerControl.CompleteStartup(error);
        CompleteDebugStartup(error);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        PlayerControl.Detach();
        DetachHostBindings();
        DetachDebugBindings();
    }

    partial void InitializeDebug(IServiceProvider services);
    partial void CompleteDebugStartup(string? error);
    partial void DetachDebugBindings();

    internal void DetachHostBindings()
    {
        if (_bindingsDetached) return;
        _bindingsDetached = true;
        _settings.BindWindow(null);
    }
}
