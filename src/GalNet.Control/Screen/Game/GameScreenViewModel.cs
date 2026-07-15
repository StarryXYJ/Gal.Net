using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.UI;
using GalNet.Control.Widget;

namespace GalNet.Control.ViewModels;

public sealed partial class GameScreenViewModel : ObservableObject
{
    [ObservableProperty] private WidgetHostViewModel? _dialogueHost;
    [ObservableProperty] private WidgetHostViewModel? _choiceHost;
    [ObservableProperty] private bool _isDialogueVisible;
    [ObservableProperty] private bool _isChoiceVisible;
    [ObservableProperty] private bool _isClickIndicatorVisible;
    [ObservableProperty] private bool _isCommandBarVisible = true;

    public WidgetHostViewModel AutoHost { get; }
    public WidgetHostViewModel QuickHost { get; }
    public WidgetHostViewModel SaveHost { get; }
    public WidgetHostViewModel LoadHost { get; }
    public WidgetHostViewModel SettingsHost { get; }
    public WidgetHostViewModel MenuHost { get; }
    public WidgetHostViewModel ScreenshotHost { get; }
    public WidgetHostViewModel HideHost { get; }
    public bool AutoMode { get; private set; }
    public bool QuickMode { get; private set; }
    public event Action<string>? CommandRequested;
    public event Action? HideRequested;

    public GameScreenViewModel(WidgetHostViewModel autoHost, WidgetHostViewModel quickHost,
        WidgetHostViewModel saveHost, WidgetHostViewModel loadHost, WidgetHostViewModel settingsHost,
        WidgetHostViewModel menuHost, WidgetHostViewModel screenshotHost, WidgetHostViewModel hideHost)
    {
        AutoHost = autoHost; QuickHost = quickHost; SaveHost = saveHost; LoadHost = loadHost;
        SettingsHost = settingsHost; MenuHost = menuHost; ScreenshotHost = screenshotHost; HideHost = hideHost;
        var auto = AutoHost.RequireWidget<ToggleWidgetViewModel>(); auto.Label = "Auto"; auto.CheckedChanged += SetAuto;
        var quick = QuickHost.RequireWidget<ToggleWidgetViewModel>(); quick.Label = "Quick"; quick.CheckedChanged += SetQuick;
        Configure(SaveHost, "Save", SaveCommand); Configure(LoadHost, "Load", LoadCommand);
        Configure(SettingsHost, "Settings", SettingsCommand); Configure(MenuHost, "Menu", MenuCommand);
        Configure(ScreenshotHost, "Screenshot", ScreenshotCommand); Configure(HideHost, "Hide", HideCommand);
    }

    private static void Configure(WidgetHostViewModel host, string text, System.Windows.Input.ICommand command)
    {
        var button = host.RequireWidget<ButtonWidgetViewModel>(); button.Text = text; button.Command = command;
    }

    private void SetAuto(bool value)
    {
        AutoMode = value;
        if (value) { QuickMode = false; QuickHost.RequireWidget<ToggleWidgetViewModel>().IsChecked = false; }
        OnPropertyChanged(nameof(AutoMode)); OnPropertyChanged(nameof(QuickMode));
    }
    private void SetQuick(bool value)
    {
        QuickMode = value;
        if (value) { AutoMode = false; AutoHost.RequireWidget<ToggleWidgetViewModel>().IsChecked = false; }
        OnPropertyChanged(nameof(AutoMode)); OnPropertyChanged(nameof(QuickMode));
    }

    [RelayCommand] private void Save() => CommandRequested?.Invoke("save");
    [RelayCommand] private void Load() => CommandRequested?.Invoke("load");
    [RelayCommand] private void Settings() => CommandRequested?.Invoke("settings");
    [RelayCommand] private void Menu() => CommandRequested?.Invoke("menu");
    [RelayCommand] private void Screenshot() => CommandRequested?.Invoke("screenshot");
    [RelayCommand] private void Hide() => HideRequested?.Invoke();
}
