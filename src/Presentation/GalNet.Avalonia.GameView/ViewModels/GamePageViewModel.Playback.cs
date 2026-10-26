using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Execution;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GamePageViewModel
{
    public GalNet.Avalonia.GameView.Input.PlayerInputSettings? Input { get; set; }
    public Task SetQuickHeldAsync(bool held) => RequestModeAsync(held ? PlaybackMode.Quick : PlaybackMode.Manual);
    private IPlayerPlaybackController? _playback;
    private Task _playbackRequests = Task.CompletedTask;
    private PlaybackMode? _pointerToggle;
    private bool _pointerWasSelected;
    [ObservableProperty] private PlaybackMode _playbackMode;
    [ObservableProperty] private bool _isSkipConfirmationOpen;
    public bool IsQuickMode => PlaybackMode == PlaybackMode.Quick;
    public bool IsAutoMode => PlaybackMode == PlaybackMode.Auto;

    public void BindPlayback(IPlayerPlaybackController? controller)
    {
        if (_playback is { } previous)
        {
            previous.CancelPlayback();
            previous.PlaybackModeChanged -= OnEngineModeChanged;
        }
        _playback = controller;
        _pointerToggle = null;
        IsSkipConfirmationOpen = false;
        PlaybackMode = controller?.PlaybackMode ?? PlaybackMode.Manual;
        if (controller is not null) controller.PlaybackModeChanged += OnEngineModeChanged;
    }

    public void CancelPlaybackFromPointer(PlaybackMode? toggle)
    {
        _pointerToggle = toggle;
        _pointerWasSelected = toggle == PlaybackMode;
        CancelPlayback();
    }
    public void ClearPlaybackPointer() => _pointerToggle = null;

    public void CancelPlayback()
    {
        _playback?.CancelPlayback();
        PlaybackMode = PlaybackMode.Manual;
        _ = RequestModeAsync(PlaybackMode.Manual);
    }

    [RelayCommand] private Task ToggleQuickAsync() => ToggleAsync(PlaybackMode.Quick);
    [RelayCommand] private Task ToggleAutoAsync() => ToggleAsync(PlaybackMode.Auto);
    private Task ToggleAsync(PlaybackMode target)
    {
        if (IsSkipConfirmationOpen) return Task.CompletedTask;
        var selected = _pointerToggle == target ? _pointerWasSelected : PlaybackMode == target;
        _pointerToggle = null;
        return RequestModeAsync(selected ? PlaybackMode.Manual : target);
    }

    private Task RequestModeAsync(PlaybackMode mode)
    {
        var controller = _playback;
        return _playbackRequests = ApplyModeAsync(_playbackRequests, controller, mode);
    }
    private async Task ApplyModeAsync(Task previous, IPlayerPlaybackController? controller, PlaybackMode mode)
    {
        await previous;
        if (controller is null || !ReferenceEquals(controller, _playback)) return;
        try
        {
            await controller.SetPlaybackModeAsync(mode);
            if (ReferenceEquals(controller, _playback))
            {
                PlaybackMode = controller.PlaybackMode;
                // ToggleButton toggles itself after pointer cancellation. Re-publish even
                // when the authoritative mode stayed Manual so OneWay bindings reset it.
                OnPropertyChanged(nameof(IsQuickMode));
                OnPropertyChanged(nameof(IsAutoMode));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ReportPlaybackFailure(controller, error); }
    }

    [RelayCommand]
    private void RequestSkip()
    {
        CancelPlayback();
        if (_playback is not null) IsSkipConfirmationOpen = true;
    }
    [RelayCommand] private void CancelSkip() => IsSkipConfirmationOpen = false;
    [RelayCommand]
    private async Task ConfirmSkipAsync(CancellationToken cancellationToken)
    {
        if (!IsSkipConfirmationOpen) return;
        var controller = _playback;
        IsSkipConfirmationOpen = false;
        await _playbackRequests;
        if (controller is null || !ReferenceEquals(controller, _playback)) return;
        try { await controller.SkipToUnreadAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { ReportPlaybackFailure(controller, error); }
    }

    public Task UpdatePlaybackOptionsAsync(PlaybackOptions options, CancellationToken cancellationToken = default) =>
        _playback?.UpdatePlaybackOptionsAsync(options, cancellationToken) ?? Task.CompletedTask;
    private void ReportPlaybackFailure(IPlayerPlaybackController? controller, Exception error)
    {
        if (!ReferenceEquals(controller, _playback)) return;
        StatusMessage = error.Message;
        InteractionObserved?.Invoke($"playback.failed:{error}");
    }
    private void OnEngineModeChanged(PlaybackMode mode) => Dispatcher.UIThread.Post(() =>
        PlaybackMode = _playback?.PlaybackMode ?? PlaybackMode.Manual);
    partial void OnPlaybackModeChanged(PlaybackMode value)
    {
        OnPropertyChanged(nameof(IsQuickMode));
        OnPropertyChanged(nameof(IsAutoMode));
    }
}
