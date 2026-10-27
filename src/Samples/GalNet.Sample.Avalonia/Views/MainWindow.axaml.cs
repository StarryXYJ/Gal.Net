using Avalonia.Controls;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Sample.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly IAvaloniaPlayerSettings _settings;
    private bool _bindingsDetached;
    private readonly LanguageSelectionViewModel _languageSelectionViewModel;

    public MainWindow(GameShell shell, IServiceProvider services)
    {
        _languageSelectionViewModel = services.GetRequiredService<LanguageSelectionViewModel>();
        InitializeComponent();
        _settings = services.GetRequiredService<IAvaloniaPlayerSettings>();
        _settings.BindWindow(this);
        PlayerRoot.Styles.Add(SampleCustomization.PlayerTheme.CreateStyles());
        LanguageSelection.DataContext = _languageSelectionViewModel;
        GameCanvas.GameContent = shell;
        InitializeDebug(services);
        Closed += OnClosed;
    }

    public async Task<string?> SelectLanguageAsync(string preferred, CancellationToken cancellationToken)
    {
        StartupStatus.IsVisible = false;
        LanguageSelection.IsVisible = true;
        try { return await _languageSelectionViewModel.ChooseAsync(preferred, cancellationToken); }
        finally { LanguageSelection.IsVisible = false; }
    }

    public void CompleteStartup(string? error = null)
    {
        StartupStatus.IsVisible = error is not null;
        if (error is not null) StartupStatus.Text = error;
        GameCanvas.IsVisible = error is null;
        CompleteDebugStartup(error);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
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
