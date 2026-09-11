using GalNet.Core.Scene;

namespace GalNet.Core.View;

/// <summary>
/// Framework-neutral <see cref="IGameView"/> implementation that composes focused
/// presentation services supplied by the host composition root.
/// </summary>
public sealed class CompositeGameView : IGameView
{
    private readonly ILayerView _layers;
    private readonly IAnimationView _animations;
    private readonly IControlView _controls;
    private readonly IAudioView _audio;
    private readonly IVideoView _video;
    private readonly ITransitionView _transitions;
    private readonly IEffectView _effects;
    private readonly ITypewriterView _typewriter;
    private readonly IInteractionView _interaction;

    public CompositeGameView(
        ILayerView layers,
        IAnimationView animations,
        IControlView controls,
        IAudioView audio,
        IVideoView video,
        ITransitionView transitions,
        IEffectView effects,
        ITypewriterView typewriter,
        IInteractionView interaction)
    {
        _layers = layers;
        _animations = animations;
        _controls = controls;
        _audio = audio;
        _video = video;
        _transitions = transitions;
        _effects = effects;
        _typewriter = typewriter;
        _interaction = interaction;
    }

    public void ShowLayer(LayerRenderRequest request) => _layers.ShowLayer(request);
    public void ReplaceLayer(string handleId, string assetId) => _layers.ReplaceLayer(handleId, assetId);
    public void HideLayer(string handleId) => _layers.HideLayer(handleId);
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) => _layers.MoveLayer(handleId, transform, z, durationSec);
    public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken ct) => _animations.AnimateAsync(request, ct);
    public Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken ct) => _animations.PlayAnimationPlanAsync(plan, ct);
    public bool CompleteAnimationImmediately(string playbackHandleId) => _animations.CompleteAnimationImmediately(playbackHandleId);
    public bool SkipAnimationBatch() => _animations.SkipAnimationBatch();
    public void ShowDialogue() => _controls.ShowDialogue();
    public void HideDialogue() => _controls.HideDialogue();
    public void PlayAudio(string channel, string assetId, float volume, string mode, int times) => _audio.PlayAudio(channel, assetId, volume, mode, times);
    public void StopAudio(string channel) => _audio.StopAudio(channel);
    public void PauseAudio(string channel) => _audio.PauseAudio(channel);
    public void ResumeAudio(string channel) => _audio.ResumeAudio(channel);
    public void EnqueueAudio(string channel, string assetId, int times) => _audio.EnqueueAudio(channel, assetId, times);
    public void ConfigureAudioQueue(string channel, string onEnd, string onEmpty) => _audio.ConfigureAudioQueue(channel, onEnd, onEmpty);
    public void PlayVideo(string assetId) => _video.PlayVideo(assetId);
    public void StopVideo() => _video.StopVideo();
    public Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct) => _transitions.PlayTransitionAsync(request, ct);
    public Task StartEffectAsync(EffectRequest request, CancellationToken ct) => _effects.StartEffectAsync(request, ct);
    public Task StopEffectAsync(string instanceId, CancellationToken ct) => _effects.StopEffectAsync(instanceId, ct);
    public Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct) => _typewriter.StartTypewriter(widgetInstanceId, speaker, text, ct);
    public void SkipTypewriter(string widgetInstanceId) => _typewriter.SkipTypewriter(widgetInstanceId);
    public void SetVoice(string assetId) => _typewriter.SetVoice(assetId);
    public Task WaitForClickAsync(CancellationToken ct) => _interaction.WaitForClickAsync(ct);
    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct) => _interaction.WaitForChoiceAsync(widgetInstanceId, options, ct);
}
