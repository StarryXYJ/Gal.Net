using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Control.Effect;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.Transition;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.ViewModels;
using GalNet.Core.Settings;
using GalNet.Core.View;
using LibVLCSharp.Shared;
using Serilog;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.View;

public class DefaultGameView : Grid, IGameView, IDisposable
{
    private static bool _vlcInitialized;
    private static readonly TransitionRegistry _transitionRegistry = new();
    private static readonly EffectRegistry _effectRegistry = new();

    private readonly GameScreenView _gameScreen;
    private readonly DefaultGameViewRegistry _registry;
    private readonly DefaultTypewriterPresenter _typewriter;
    private readonly DefaultChoicePresenter _choice;
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _videoPlayer;
    private readonly AudioController _audioController;
    private readonly VideoController _videoController;
    private readonly GameSettings _gameSettings;
    private readonly GameScreenViewModel _screen;
    private TaskCompletionSource<int>? _clickTcs;
    private int _disposed;
    private bool _dialogueWasVisible;
    private bool _choiceWasVisible;
    private bool _indicatorWasVisible;
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

        // ── 注册内置转场 ──
        RegisterBuiltInTransitions();

        // ── 注册内置特效 ──
        RegisterBuiltInEffects();
    }

    private static void RegisterBuiltInTransitions()
    {
        _transitionRegistry.Register(new FadeTransition());
        _transitionRegistry.Register(new SlideLeftTransition());
        _transitionRegistry.Register(new SlideRightTransition());
        _transitionRegistry.Register(new DissolveTransition());
    }

    private static void RegisterBuiltInEffects()
    {
        _effectRegistry.Register(new ShakeEffect());
        _effectRegistry.Register(new VignetteEffect());
        _effectRegistry.Register(new FlashEffect());
    }

    public DefaultGameView(GameSettings settings, IWidgetFactory widgetFactory, WidgetBuildContext widgetContext, GameScreenViewModel screen)
    {
        _gameSettings = settings;
        _screen = screen;
        _gameScreen = new GameScreenView { DataContext = screen };
        _registry = new DefaultGameViewRegistry(_gameScreen);
        _typewriter = new DefaultTypewriterPresenter(_gameSettings, _gameScreen, _registry, widgetFactory, widgetContext, screen);
        _choice = new DefaultChoicePresenter(_registry, widgetFactory, widgetContext, screen);

        _libVlc = _vlcInitialized ? new LibVLC() : null!;
        _videoPlayer = _vlcInitialized ? new MediaPlayer(_libVlc) : null!;
        
        _audioController = new AudioController(_libVlc, _vlcInitialized);
        _videoController = new VideoController(_libVlc, _videoPlayer, _vlcInitialized, _gameScreen);

        Children.Add(_gameScreen);

        _screen.CommandRequested += command => CommandRequested?.Invoke(command);
        _screen.HideRequested += HideUi;

        PointerPressed += (_, e) =>
        {
            if (IsUiHidden)
            {
                RestoreUi();
                e.Handled = true;
                return;
            }
            Log.ForContext("LogChannel", "Game").Debug("PointerPressed at {X},{Y}", e.GetPosition(this).X, e.GetPosition(this).Y);

            if (_clickTcs is { Task.IsCompleted: false })
            {
                var tcs = _clickTcs;
                _clickTcs = null;
                tcs.TrySetResult(0);
                return;
            }

            if (_typewriter.CurrentTask is { IsCompleted: false })
            {
                _typewriter.Skip(string.Empty);
            }
        };

        KeyDown += (_, e) => { if (IsUiHidden) { RestoreUi(); e.Handled = true; } };
    }

    // ── ILayerView ──

    void ILayerView.ShowLayer(string id, string assetId, float x, float y, float z)
        => ShowLayer(id, assetId, x, y, z);
    void ILayerView.HideLayer(string id) => HideLayer(id);
    void ILayerView.MoveLayer(string id, float x, float y, float z, float durationSec)
        => MoveLayer(id, x, y, z, durationSec);

    public void ShowLayer(string id, string assetId, float x, float y, float z = 0)
        => _registry.ShowLayer(id, assetId, x, y, z);
    public void HideLayer(string id) => _registry.HideLayer(id);
    public void MoveLayer(string id, float x, float y, float z, float durationSec)
        => _registry.MoveLayer(id, x, y, z, durationSec);

    // ── IControlView ──

    void IControlView.ShowControl(string instanceId) => _registry.ShowControl(instanceId);
    void IControlView.HideControl(string instanceId) => _registry.HideControl(instanceId);
    void IControlView.SetControlProperty(string instanceId, string property, string value)
        => _registry.SetControlProperty(instanceId, property, value);

    // ── IPageView ──

    Task<string> IPageView.ShowPageAsync(string screenInstanceId, CancellationToken ct)
        => Task.FromResult(screenInstanceId);

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

    void IEffectView.ApplyTransition(string type, float durationSec)
    {
        if (type == "dissolve")
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try { await DissolveAsync(durationSec); }
                catch (Exception ex) { Log.Error(ex, "Dissolve transition failed"); }
            });
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            var transition = _transitionRegistry.Get(type);
            if (transition == null)
            {
                Log.Warning("Transition not found: {Type}", type);
                return;
            }

            try
            {
                await transition.ExecuteAsync(this, null, null, durationSec, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Transition failed: {Type}", type);
            }
        });
    }

    /// <summary>
    /// Cross-fade between old and new layers. Captures current layer images into an
    /// overlay, waits for pending layer changes (hide/show), then fades the overlay out.
    /// </summary>
    private async Task DissolveAsync(float durationSec)
    {
        var canvas = _gameScreen.LayerCanvas;
        var overlay = new Canvas { IsHitTestVisible = false };

        // Phase 1: snapshot current layers into overlay
        var layerImages = canvas.Children.OfType<Image>().ToList();
        foreach (var img in layerImages)
        {
            var copy = new Image
            {
                Source = img.Source,
                Stretch = img.Stretch,
                Opacity = img.Opacity,
            };
            copy.SetValue(Canvas.LeftProperty, img.GetValue(Canvas.LeftProperty));
            copy.SetValue(Canvas.TopProperty, img.GetValue(Canvas.TopProperty));
            copy.SetValue(Canvas.ZIndexProperty, (int)img.GetValue(Canvas.ZIndexProperty) + 1000);
            overlay.Children.Add(copy);
        }

        if (overlay.Children.Count == 0)
        {
            Log.Debug("Dissolve: no layers to capture, skipping");
            return;
        }

        // Ensure overlay renders on top of ALL canvas children (including new layers)
        overlay.SetValue(Canvas.ZIndexProperty, 9999);
        canvas.Children.Add(overlay);
        Log.Debug("Dissolve: overlay added with {Count} children", overlay.Children.Count);

        // Phase 2: wait for all pending Normal-priority dispatcher operations
        // (HideLayer, ShowLayer, etc.) to complete before starting the fade.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Log.Debug("Dissolve: starting fade, duration={Duration}s", durationSec);

        // Phase 3: fade overlay from visible to transparent using manual interpolation
        if (durationSec > 0f)
        {
            var stepMs = 16; // ~60 fps
            var steps = (int)(durationSec * 1000 / stepMs);
            for (var s = 0; s <= steps; s++)
            {
                var t = (float)s / steps; // 0 → 1
                overlay.Opacity = 1f - t;
                await Task.Delay(stepMs);
            }
        }

        Log.Debug("Dissolve: fade complete, removing overlay");
        canvas.Children.Remove(overlay);
    }

    void IEffectView.ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var effectId = _effectRegistry.Start(effectType, this, parameters);
            if (string.IsNullOrEmpty(effectId))
            {
                Log.Warning("Effect not found: {Type}", effectType);
            }
        });
    }

    void IEffectView.StopEffect(string effectId)
    {
        Dispatcher.UIThread.Post(() => _effectRegistry.Stop(effectId));
    }
}
