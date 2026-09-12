using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Core.Scene;
using GalNet.Control.Screen.Game;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Settings;
using GalNet.Core.UI;
using GalNet.Core.View;
using GalNet.Core.Assets;
using GalNet.Presentation.Defaults.Media;
using LibVLCSharp.Shared;
using Serilog;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Runtime.Presentation;

public class DefaultGameView : Grid, IGameView, IDisposable
{
    private static bool _vlcInitialized;

    private readonly GameScreenView _gameScreen;
    private readonly DefaultGameViewRegistry _registry;
    private readonly DefaultTypewriterPresenter _typewriter;
    private readonly DefaultChoicePresenter _choice;
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _videoPlayer;
    private readonly LibVlcAudioController _audioController;
    private readonly VideoController _videoController;
    private readonly GameSettings _gameSettings;
    private readonly GameScreenViewModel _screen;
    private TaskCompletionSource<int>? _clickTcs;
    private int _disposed;
    private bool _dialogueWasVisible;
    private bool _choiceWasVisible;
    private bool _indicatorWasVisible;
    private bool _advanceQueued;
    public bool AutoMode => _screen.AutoMode;
    public bool QuickMode => _screen.QuickMode;
    public bool IsUiHidden { get; private set; }
    public event Action<string>? CommandRequested;
    public Task<byte[]> CapturePngAsync(bool includeUi)
    {
        var target = includeUi ? (AvaloniaControl)_gameScreen : _gameScreen.LayerCanvas;
        var pixelSize = new PixelSize(Math.Max(1, (int)target.Bounds.Width), Math.Max(1, (int)target.Bounds.Height));
        var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(target);
        using var stream = new MemoryStream(); bitmap.Save(stream);
        return Task.FromResult(stream.ToArray());
    }

    /// <summary>Stop all audio/video and release resources.</summary>
    public void Cleanup()
    {
        _clickTcs?.TrySetCanceled();
        _clickTcs = null;
        _typewriter.Cancel();
        _choice.Cancel();
        _audioController.StopAll();
        _videoController.Stop();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Cleanup();
        _audioController.Dispose();
        _videoController.Dispose();
        _libVlc?.Dispose();
    }

    static DefaultGameView()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _vlcInitialized = true;
        }
        catch (Exception ex)
        {
            Log.ForContext("LogChannel", "Game").Warning(ex, "LibVLC initialization failed — audio/video will be unavailable");
            _vlcInitialized = false;
        }

    }

    public DefaultGameView(GameSettings settings, GameUiConfiguration config, GameScreenViewModel screen, IAssetManager? assets = null)
    {
        _gameSettings = settings;
        _screen = screen;
        _gameScreen = new GameScreenView { DataContext = screen };
        _registry = new DefaultGameViewRegistry(_gameScreen, assets);
        _typewriter = new DefaultTypewriterPresenter(_gameSettings, _gameScreen, screen, assets);
        _choice = new DefaultChoicePresenter(screen);

        _libVlc = _vlcInitialized ? new LibVLC() : null!;
        _videoPlayer = _vlcInitialized ? new MediaPlayer(_libVlc) : null!;
        
        _audioController = new LibVlcAudioController(_libVlc);
        _videoController = new VideoController(_libVlc, _videoPlayer, _vlcInitialized, _gameScreen, assets);

        Children.Add(_gameScreen);

        _screen.CommandRequested += command => CommandRequested?.Invoke(command);
        _screen.HideRequested += HideUi;
        _screen.AdvanceRequested += Advance;

        KeyDown += (_, e) => { if (IsUiHidden) { RestoreUi(); e.Handled = true; } };
    }

    private void Advance()
    {
        if (IsUiHidden)
        {
            RestoreUi();
            return;
        }

        // Choices own their input. The transparent advance layer is disabled while one is visible,
        // but keep this guard to make the rule explicit for future hosts.
        if (_screen.IsChoiceVisible)
            return;

        if (_clickTcs is { Task.IsCompleted: false })
        {
            var tcs = _clickTcs;
            _clickTcs = null;
            tcs.TrySetResult(0);
            return;
        }

        if (_typewriter.CurrentTask is { IsCompleted: false })
        {
            _advanceQueued = true;
            _typewriter.Skip(string.Empty);
        }
    }


    // ── ILayerView ──

    void ILayerView.ShowLayer(LayerRenderRequest request) => ShowLayer(request);
    void ILayerView.ReplaceLayer(string handleId, string assetId) => ReplaceLayer(handleId, assetId);
    void ILayerView.HideLayer(string handleId) => HideLayer(handleId);
    void ILayerView.MoveLayer(string handleId, LayerTransform transform, float z, float durationSec)
        => MoveLayer(handleId, transform, z, durationSec);
    async Task<AnimationOutcome> IAnimationView.AnimateAsync(AnimationRequest request, CancellationToken ct)
    {
        if (request.LoopMode != AnimationLoopMode.Once)
            await Task.Delay(TimeSpan.FromSeconds(request.DurationSeconds * (request.LoopMode == AnimationLoopMode.PingPong ? 2 : 1)), ct);
        return AnimationOutcome.Completed;
    }
    async Task<AnimationPlanPlayResult> IAnimationView.PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken ct)
    {
        if (plan.LoopMode == AnimationLoopMode.Loop) await Task.Delay(TimeSpan.FromSeconds(plan.DurationFrames / (double)plan.FrameRate), ct);
        return new AnimationPlanPlayResult { Outcome = AnimationOutcome.Completed, TrackOutcomes = plan.Tracks.ToDictionary(track => $"{track.HandleId}:{track.Property}", _ => AnimationOutcome.Completed) };
    }
    bool IAnimationView.CompleteAnimationImmediately(string playbackHandleId) => false;
    bool IAnimationView.SkipAnimationBatch() => false;

    public void ShowLayer(LayerRenderRequest request) => _registry.ShowLayer(request);
    public void ReplaceLayer(string handleId, string assetId) => _registry.ReplaceLayer(handleId, assetId);
    public void HideLayer(string handleId) => _registry.HideLayer(handleId);
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec)
        => _registry.MoveLayer(handleId, transform, z, durationSec);

    // ── IControlView ──

    void IControlView.ShowDialogue() => Dispatcher.UIThread.Post(() => _screen.IsDialogueVisible = true);
    void IControlView.HideDialogue() => Dispatcher.UIThread.Post(() => _screen.IsDialogueVisible = false);

    // ── ITypewriterView ──

    Task ITypewriterView.StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
        => StartTypewriterAsync(widgetInstanceId, speaker, text, ct);
    private async Task StartTypewriterAsync(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        var task = _typewriter.StartAsync(widgetInstanceId, speaker, text, ct);
        if (QuickMode) Dispatcher.UIThread.Post(() => _typewriter.Skip(widgetInstanceId));
        await task;
    }
    void ITypewriterView.SkipTypewriter(string widgetInstanceId) => _typewriter.Skip(widgetInstanceId);
    void ITypewriterView.SetVoice(string assetId) => _typewriter.SetVoice(assetId);

    // ── IInteractionView ──

    Task IInteractionView.WaitForClickAsync(CancellationToken ct) => WaitForClickAsync(ct);
    Task<int> IInteractionView.WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct)
        => _choice.ShowAsync(widgetInstanceId, options, ct);

    public async Task WaitForClickAsync(CancellationToken ct)
    {
        if (_typewriter.CurrentTask != null)
        {
            try { await _typewriter.CurrentTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "Typewriter task faulted — continuing to click wait");
            }
        }

        // A click during typewriting is both a skip request and an advance request. Without
        // queuing it, the runtime reaches this wait only after that click has already passed.
        if (_advanceQueued)
        {
            _advanceQueued = false;
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _screen.IsClickIndicatorVisible = true);
        if (QuickMode || AutoMode)
        {
            var delay = QuickMode ? Math.Max(0.01f, _gameSettings.QuickAdvanceInterval) : Math.Max(0.01f, _gameSettings.AutoAdvanceInterval);
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            return;
        }
        var tcs = new TaskCompletionSource<int>();
        _clickTcs = tcs;
        ct.Register(() => { _clickTcs = null; tcs.TrySetCanceled(); });
        await tcs.Task;
    }

    private void HideUi()
    {
        IsUiHidden = true;
        _dialogueWasVisible = _screen.IsDialogueVisible;
        _choiceWasVisible = _screen.IsChoiceVisible;
        _indicatorWasVisible = _screen.IsClickIndicatorVisible;
        _screen.IsDialogueVisible = false;
        _screen.IsChoiceVisible = false;
        _screen.IsCommandBarVisible = false;
        _screen.IsClickIndicatorVisible = false;
        Focus();
    }

    private void RestoreUi()
    {
        IsUiHidden = false;
        _screen.IsCommandBarVisible = true;
        _screen.IsDialogueVisible = _dialogueWasVisible;
        _screen.IsChoiceVisible = _choiceWasVisible;
        _screen.IsClickIndicatorVisible = _indicatorWasVisible;
    }

    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct)
        => _choice.ShowAsync(widgetInstanceId, options, ct);

    // ── IAudioView (LibVLCSharp) ──

    void IAudioView.PlayAudio(string channel, string assetId, float volume, string mode, int times)
    {
        _audioController.Play(channel, assetId, volume);
    }

    void IAudioView.StopAudio(string channel)
    {
        _audioController.Stop(channel);
    }

    void IAudioView.PauseAudio(string channel)
    {
        _audioController.Pause(channel);
    }

    void IAudioView.ResumeAudio(string channel)
    {
        _audioController.Resume(channel);
    }

    void IAudioView.EnqueueAudio(string channel, string assetId, int times)
    {
        _audioController.Enqueue(channel, assetId);
    }

    void IAudioView.ConfigureAudioQueue(string channel, string onEnd, string onEmpty) { }

    // ── IVideoView (LibVLCSharp) ──

    void GalNet.Core.View.IVideoView.PlayVideo(string assetId)
    {
        _videoController.Play(assetId);
    }

    void GalNet.Core.View.IVideoView.StopVideo()
    {
        _videoController.Stop();
    }

    // ── IEffectView ──

    Task IEffectView.StartEffectAsync(EffectRequest request, CancellationToken ct) =>
        Task.CompletedTask;

    Task IEffectView.StopEffectAsync(string instanceId, CancellationToken ct) =>
        Task.CompletedTask;

    private static Task RunOnUiThreadAsync(Func<Task> operation, CancellationToken ct)
    {
        if (Dispatcher.UIThread.CheckAccess()) return operation();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ct.Register(() => completion.TrySetCanceled(ct));
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await operation();
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                completion.TrySetCanceled(ct);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                registration.Dispose();
            }
        });
        return completion.Task;
    }
}
