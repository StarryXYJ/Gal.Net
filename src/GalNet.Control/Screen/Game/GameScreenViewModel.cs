using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.UI;

namespace GalNet.Control.Screen.Game;

/// <summary>State for the fixed game screen. It deliberately exposes no user-defined controls.</summary>
public sealed partial class GameScreenViewModel : ObservableObject
{
    [ObservableProperty] private Avalonia.Controls.Control? _dialogueView;
    [ObservableProperty] private Avalonia.Controls.Control? _choiceView;
    [ObservableProperty] private bool _isDialogueVisible;
    [ObservableProperty] private bool _isChoiceVisible;
    [ObservableProperty] private bool _isClickIndicatorVisible;
    [ObservableProperty] private bool _isCommandBarVisible;
    [ObservableProperty] private bool _autoMode;
    [ObservableProperty] private bool _quickMode;
    public GameUiConfiguration Configuration { get; }
    public IBrush CommandTextBrush => new SolidColorBrush(Configuration.CommandTextColor);
    public IBrush CommandHoverTextBrush => new SolidColorBrush(Configuration.CommandHoverTextColor);
    public IBrush CommandSelectedTextBrush => new SolidColorBrush(Configuration.CommandSelectedTextColor);
    public event Action<string>? CommandRequested;
    public event Action? HideRequested;
    public event Action? AdvanceRequested;

    public GameScreenViewModel(GameUiConfiguration configuration)
    {
        Configuration = configuration;
        IsCommandBarVisible = configuration.CommandBarVisible;
    }

    partial void OnAutoModeChanged(bool value) { if (value) QuickMode = false; }
    partial void OnQuickModeChanged(bool value) { if (value) AutoMode = false; }
    [RelayCommand] private void Save() => CommandRequested?.Invoke("save");
    [RelayCommand] private void Load() => CommandRequested?.Invoke("load");
    [RelayCommand] private void Settings() => CommandRequested?.Invoke("settings");
    [RelayCommand] private void Menu() => CommandRequested?.Invoke("menu");
    [RelayCommand] private void Screenshot() => CommandRequested?.Invoke("screenshot");
    [RelayCommand] private void Hide() => HideRequested?.Invoke();
    [RelayCommand] private void Advance() => AdvanceRequested?.Invoke();
}
