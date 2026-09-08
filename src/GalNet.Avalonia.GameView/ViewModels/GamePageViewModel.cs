using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Game.Controls;

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

    public event Action? AdvanceRequested;

    [RelayCommand] private void Advance() => AdvanceRequested?.Invoke();
    [RelayCommand] private void OpenSaveSlots() => _navigation.Navigate<SaveSlotsPageViewModel>();
    [RelayCommand] private void ToggleNvlMode() => IsNvlMode = !IsNvlMode;
    [RelayCommand] private void OpenSettings() => _navigation.Navigate<SettingsPageViewModel>();
    [RelayCommand] private void OpenGallery() => _navigation.Navigate<GalleryPageViewModel>();
    [RelayCommand] private void ReturnToTitle() => _navigation.ResetTo<TitlePageViewModel>();
    [RelayCommand] private void ReturnToGame() => _navigation.GoBack();

    public Task WaitForAdvanceAsync(CancellationToken cancellationToken)
    {
        _advanceWaiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _advanceWaiter.Task.WaitAsync(cancellationToken);
    }

    public Task<int> WaitForChoiceAsync(IEnumerable<string> options, CancellationToken cancellationToken)
    {
        Choices.Clear();
        foreach (var option in options) Choices.Add(option);
        IsChoiceVisible = true;
        _choiceWaiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        return AwaitChoiceAsync(_choiceWaiter, cancellationToken);
    }

    public void CompleteAdvance() => _advanceWaiter?.TrySetResult();

    [RelayCommand] private void CompleteChoice(int selectedIndex) => _choiceWaiter?.TrySetResult(selectedIndex);
    public void PresentNvlLine(string speaker, string text) => NvlLines.Add(new NvlLine(speaker, text));

    public void SetLayer(string id, SceneLayerItem item)
    {
        var existing = Layers.FirstOrDefault(layer => layer.Id == id);
        if (existing is null) Layers.Add(item);
        else
        {
            existing.Content = item.Content;
            existing.X = item.X;
            existing.Y = item.Y;
            existing.ZIndex = item.ZIndex;
            existing.IsVisible = true;
        }
    }

    public void HideLayer(string id)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == id);
        if (layer is not null) layer.IsVisible = false;
    }

    public void MoveLayer(string id, float x, float y, float z)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == id);
        if (layer is null) return;
        layer.X = x;
        layer.Y = y;
        layer.ZIndex = (int)z;
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
            IsChoiceVisible = false;
            Choices.Clear();
        }
    }
}
