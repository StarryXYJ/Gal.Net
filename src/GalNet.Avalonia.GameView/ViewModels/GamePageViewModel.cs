using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Game.Controls;
using GalNet.Game.Controls.Scene;
using GalNet.Core.Scene;

namespace GalNet.Avalonia.GameView.ViewModels;

/// <summary>Platform UI state shared by the editor preview and the official Avalonia sample.</summary>
public sealed partial class GamePageViewModel : PageViewModelBase
{
    private readonly IGameNavigationService _navigation;
    private TaskCompletionSource? _advanceWaiter;
    private TaskCompletionSource<int>? _choiceWaiter;
    private readonly Dictionary<string, EffectAnimationBinding> _effectAnimations = new(StringComparer.Ordinal);

    public GamePageViewModel(IGameNavigationService navigation) => _navigation = navigation;

    public ObservableCollection<SceneLayerItem> Layers { get; } = [];
    public ObservableCollection<string> Choices { get; } = [];
    public ObservableCollection<NvlLine> NvlLines { get; } = [];
    /// <summary>Transitional host-owned visuals rendered inside the scene surface, before GameShell UI.</summary>
    // The render-graph migration will replace these with texture-to-texture effects.
    public ObservableCollection<Control> SceneVisuals { get; } = [];

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isDialogueVisible;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isChoiceVisible;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isNvlMode;
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
            existing.Color = item.Color;
            existing.Flipbook = item.Flipbook?.Clone();
            existing.X = item.X;
            existing.Y = item.Y;
            existing.RotationDegrees = item.RotationDegrees;
            existing.ScaleX = item.ScaleX;
            existing.ScaleY = item.ScaleY;
            existing.Z = item.Z;
            existing.DisplayMode = item.DisplayMode;
            existing.Opacity = item.Opacity;
            existing.IsVisible = true;
        }
    }

    public void HideLayer(string id)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.HandleId == id);
        if (layer is not null) Layers.Remove(layer);
    }

    /// <summary>Removes all host-owned visuals before a new game session is constructed.</summary>
    public void ResetScenePresentation()
    {
        Layers.Clear();
        SceneVisuals.Clear();
        _effectAnimations.Clear();
        IsDialogueVisible = false;
        IsChoiceVisible = false;
        NvlLines.Clear();
    }

    public void ReplaceLayer(string id, IImage? image)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.HandleId == id);
        if (layer is null) return;
        layer.Image = image;
        layer.Color = null;
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

    public bool TryGetLayerAnimationValue(string id, string property, out double value)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.HandleId == id);
        if (layer is null) { value = 0; return false; }
        value = property switch
        {
            "transform.x" => layer.X,
            "transform.y" => layer.Y,
            "transform.rotationDegrees" => layer.RotationDegrees,
            "transform.scaleX" => layer.ScaleX,
            "transform.scaleY" => layer.ScaleY,
            "opacity" => layer.Opacity,
            "flipbook.index" when layer.Flipbook is not null => layer.FlipbookIndex,
            _ => 0
        };
        return property is "transform.x" or "transform.y" or "transform.rotationDegrees" or "transform.scaleX" or "transform.scaleY" or "opacity" ||
               property == "flipbook.index" && layer.Flipbook is not null;
    }

    public bool SetLayerAnimationValue(string id, string property, double value)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.HandleId == id);
        if (layer is null) return false;
        switch (property)
        {
            case "transform.x": layer.X = value; return true;
            case "transform.y": layer.Y = value; return true;
            case "transform.rotationDegrees": layer.RotationDegrees = value; return true;
            case "transform.scaleX": layer.ScaleX = value; return true;
            case "transform.scaleY": layer.ScaleY = value; return true;
            case "opacity": layer.Opacity = value; return true;
            case "flipbook.index" when layer.Flipbook is not null: layer.FlipbookIndex = value; return true;
            default: return false;
        }
    }

    /// <summary>Registers the presentation-side property sink for a live animatable effect.</summary>
    public void RegisterEffectAnimation(string instanceId, string property, Action<double> apply, double initialValue = 0)
    {
        _effectAnimations[EffectPropertyKey(instanceId, property)] = new EffectAnimationBinding(apply) { Value = initialValue };
        apply(initialValue);
    }

    public void UnregisterEffectAnimation(string instanceId, string property) => _effectAnimations.Remove(EffectPropertyKey(instanceId, property));
    public void UnregisterEffectAnimations(string instanceId)
    {
        foreach (var key in _effectAnimations.Keys.Where(key => key.StartsWith($"{instanceId}:", StringComparison.Ordinal)).ToArray()) _effectAnimations.Remove(key);
    }

    public bool TryGetEffectAnimationValue(string id, string property, out double value)
    {
        if (_effectAnimations.TryGetValue(EffectPropertyKey(id, property), out var binding))
        {
            value = binding.Value;
            return true;
        }
        value = 0;
        return false;
    }

    public bool SetEffectAnimationValue(string id, string property, double value)
    {
        if (!_effectAnimations.TryGetValue(EffectPropertyKey(id, property), out var binding)) return false;
        binding.Value = value;
        binding.Apply(value);
        return true;
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

    private sealed class EffectAnimationBinding(Action<double> apply)
    {
        public Action<double> Apply { get; } = apply;
        public double Value { get; set; }
    }

    private static string EffectPropertyKey(string instanceId, string property) => $"{instanceId}:{property}";
}
