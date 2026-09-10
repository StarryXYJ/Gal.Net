using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Game.Controls;
using GalNet.Core.Scene;

namespace GalNet.Avalonia.GameView.ViewModels;

/// <summary>Platform UI state shared by the editor preview and the official Avalonia sample.</summary>
public sealed partial class GamePageViewModel : PageViewModelBase
{
    private readonly IGameNavigationService _navigation;
    private TaskCompletionSource? _advanceWaiter;
    private TaskCompletionSource<int>? _choiceWaiter;

    public GamePageViewModel(IGameNavigationService navigation) => _navigation = navigation;

    public ObservableCollection<SceneLayerItem> Layers { get; } = [];
    public ObservableCollection<string> Choices { get; } = [];
    public ObservableCollection<NvlLine> NvlLines { get; } = [];
    public ObservableCollection<string> ActiveEffects { get; } = [];

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isDialogueVisible;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isChoiceVisible;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isNvlMode;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private double _transitionOpacity;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private IBrush _transitionBrush = Brushes.Black;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private double _textSpeed = 30d;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _statusMessage = string.Empty;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isUiHidden;
    public bool IsGameUiVisible => !IsUiHidden;
    public bool IsDialogueOverlayVisible => IsDialogueVisible && !IsUiHidden;
    public bool IsNvlOverlayVisible => IsNvlMode && !IsUiHidden;
    public bool IsChoiceOverlayVisible => IsChoiceVisible && !IsUiHidden;

    public event Action? AdvanceRequested;
    public event Action? ScreenshotRequested;
    public event Action? ReturnToTitleRequested;
    /// <summary>Host-observable player actions, kept logger-free so the shared view remains reusable.</summary>
    public event Action<string>? InteractionObserved;

    [RelayCommand]
    private void Advance()
    {
        if (IsUiHidden)
        {
            InteractionObserved?.Invoke("advance.restore-ui");
            IsUiHidden = false;
            return;
        }

        InteractionObserved?.Invoke("advance.request");
        AdvanceRequested?.Invoke();
    }

    [RelayCommand]
    private Task OpenSaveSlotsAsync(CancellationToken cancellationToken) =>
        _navigation.NavigateAsync<SaveSlotsPageViewModel, SaveSlotsMode>(SaveSlotsMode.Save, cancellationToken);

    [RelayCommand]
    private Task OpenLoadSlotsAsync(CancellationToken cancellationToken) =>
        _navigation.NavigateAsync<SaveSlotsPageViewModel, SaveSlotsMode>(SaveSlotsMode.Load, cancellationToken);

    [RelayCommand] private void ToggleNvlMode() => IsNvlMode = !IsNvlMode;
    [RelayCommand] private void OpenSettings() => _navigation.Navigate<SettingsPageViewModel>();
    [RelayCommand] private void ReturnToTitle() => ReturnToTitleRequested?.Invoke();
    [RelayCommand] private void RequestScreenshot() => ScreenshotRequested?.Invoke();
    [RelayCommand] private void HideUi() => IsUiHidden = true;
    [RelayCommand] private void ReturnToGame() => _navigation.GoBack();

    public void ObserveAdvancePointerPressed() => InteractionObserved?.Invoke("advance.pointer-pressed");

    partial void OnIsUiHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGameUiVisible));
        OnPropertyChanged(nameof(IsDialogueOverlayVisible));
        OnPropertyChanged(nameof(IsNvlOverlayVisible));
        OnPropertyChanged(nameof(IsChoiceOverlayVisible));
    }

    partial void OnIsDialogueVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDialogueOverlayVisible));
    partial void OnIsNvlModeChanged(bool value) => OnPropertyChanged(nameof(IsNvlOverlayVisible));
    partial void OnIsChoiceVisibleChanged(bool value) => OnPropertyChanged(nameof(IsChoiceOverlayVisible));

    public async Task WaitForAdvanceAsync(CancellationToken cancellationToken)
    {
        InteractionObserved?.Invoke("wait.advance");
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _advanceWaiter = waiter;
        try { await waiter.Task.WaitAsync(cancellationToken); }
        finally
        {
            if (ReferenceEquals(_advanceWaiter, waiter))
                _advanceWaiter = null;
        }
    }

    public Task<int> WaitForChoiceAsync(IEnumerable<string> options, CancellationToken cancellationToken)
    {
        Choices.Clear();
        foreach (var option in options) Choices.Add(option);
        IsChoiceVisible = true;
        InteractionObserved?.Invoke($"wait.choice:{Choices.Count}");
        _choiceWaiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        return AwaitChoiceAsync(_choiceWaiter, cancellationToken);
    }

    public void CompleteAdvance() => _advanceWaiter?.TrySetResult();

    [RelayCommand]
    private void CompleteChoice(int selectedIndex)
    {
        InteractionObserved?.Invoke($"choice.selected:{selectedIndex}");
        _choiceWaiter?.TrySetResult(selectedIndex);
    }
    public void PresentNvlLine(string speaker, string text) => NvlLines.Add(new NvlLine(speaker, text));

    public void SetLayer(string id, SceneLayerItem item)
    {
        var existing = Layers.FirstOrDefault(layer => layer.HandleId == id);
        if (existing is null) Layers.Add(item);
        else
        {
            existing.Image = item.Image;
            existing.X = item.X;
            existing.Y = item.Y;
            existing.RotationDegrees = item.RotationDegrees;
            existing.ScaleX = item.ScaleX;
            existing.ScaleY = item.ScaleY;
            existing.Z = item.Z;
            existing.DisplayMode = item.DisplayMode;
            existing.IsVisible = true;
        }
    }

    public void HideLayer(string id)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.HandleId == id);
        if (layer is not null) Layers.Remove(layer);
    }

    public void ReplaceLayer(string id, IImage? image)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.HandleId == id);
        if (layer is null) return;
        layer.Image = image;
    }

    public void MoveLayer(string id, LayerTransform transform, float z)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.HandleId == id);
        if (layer is null) return;
        layer.X = transform.X;
        layer.Y = transform.Y;
        layer.RotationDegrees = transform.RotationDegrees;
        layer.ScaleX = transform.ScaleX;
        layer.ScaleY = transform.ScaleY;
        layer.Z = z;
    }

    public async Task PlayTransitionAsync(IBrush brush, TimeSpan duration, CancellationToken cancellationToken, double peakOpacity = 1d)
    {
        TransitionBrush = brush;
        TransitionOpacity = peakOpacity;
        await Task.Delay(TimeSpan.FromTicks(duration.Ticks / 2), cancellationToken);
        TransitionOpacity = 0;
        await Task.Delay(TimeSpan.FromTicks(duration.Ticks / 2), cancellationToken);
    }

    private async Task<int> AwaitChoiceAsync(TaskCompletionSource<int> choice, CancellationToken cancellationToken)
    {
        try { return await choice.Task.WaitAsync(cancellationToken); }
        finally
        {
            if (ReferenceEquals(_choiceWaiter, choice))
                _choiceWaiter = null;
            IsChoiceVisible = false;
            Choices.Clear();
        }
    }
}
