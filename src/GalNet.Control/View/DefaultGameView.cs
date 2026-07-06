using Avalonia;
using Avalonia.Controls;
using GalNet.Control.Effect;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.Transition;
using GalNet.Core.Settings;
using GalNet.Core.View;
using LibVLCSharp.Shared;
using Serilog;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.View;

public class DefaultGameView : UserControl, IGameView
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
    private TaskCompletionSource<int>? _clickTcs;

    static DefaultGameView()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _vlcInitialized = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LibVLC initialization failed — audio/video will be unavailable");
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

    public DefaultGameView(GameSettings? settings = null)
    {
        _gameScreen = new GameScreenView();
        _registry = new DefaultGameViewRegistry(_gameScreen);
        _typewriter = new DefaultTypewriterPresenter(settings ?? new GameSettings(), _gameScreen, _registry);
        _choice = new DefaultChoicePresenter(_gameScreen, _registry);

        _libVlc = _vlcInitialized ? new LibVLC() : null!;
        _videoPlayer = _vlcInitialized ? new MediaPlayer(_libVlc) : null!;
        
        _audioController = new AudioController(_libVlc, _vlcInitialized);
        _videoController = new VideoController(_libVlc, _videoPlayer, _vlcInitialized, _gameScreen);

        Content = _gameScreen;

        PointerPressed += (_, e) =>
        {
            Log.Debug("PointerPressed at {X},{Y}", e.GetPosition(this).X, e.GetPosition(this).Y);

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
        => _typewriter.StartAsync(widgetInstanceId, speaker, text, ct);
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
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _gameScreen.ClickIndicator.IsVisible = true);
        var tcs = new TaskCompletionSource<int>();
        _clickTcs = tcs;
        ct.Register(() => { _clickTcs = null; tcs.TrySetCanceled(); });
        await tcs.Task;
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

    async void IEffectView.ApplyTransition(string type, float durationSec)
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
    }

    void IEffectView.ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters)
    {
        var effectId = _effectRegistry.Start(effectType, this, parameters);
        if (string.IsNullOrEmpty(effectId))
        {
            Log.Warning("Effect not found: {Type}", effectType);
        }
    }

    void IEffectView.StopEffect(string effectId)
    {
        _effectRegistry.Stop(effectId);
    }
}
