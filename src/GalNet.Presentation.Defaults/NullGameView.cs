using GalNet.Core.View;
using GalNet.Core.Scene;

namespace GalNet.Presentation.Defaults;

/// <summary>Framework-neutral no-op game view for tests and headless hosts.</summary>
public class NullGameView : IGameView
{
    public virtual void ShowLayer(LayerRenderRequest request) { }
    public virtual void ReplaceLayer(string handleId, string assetId) { }
    public virtual void HideLayer(string handleId) { }
    public virtual void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) { }
    public virtual void ShowDialogue() { }
    public virtual void HideDialogue() { }
    public virtual void PlayAudio(string channel, string assetId, float volume, string mode, int times) { }
    public virtual void StopAudio(string channel) { }
    public virtual void PauseAudio(string channel) { }
    public virtual void ResumeAudio(string channel) { }
    public virtual void EnqueueAudio(string channel, string assetId, int times) { }
    public virtual void ConfigureAudioQueue(string channel, string onEnd, string onEmpty) { }
    public virtual void PlayVideo(string assetId) { }
    public virtual void StopVideo() { }
    public virtual Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct) => Task.CompletedTask;
    public virtual Task StartEffectAsync(EffectRequest request, CancellationToken ct) => Task.CompletedTask;
    public virtual Task StopEffectAsync(string instanceId, CancellationToken ct) => Task.CompletedTask;
    public virtual Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct) => Task.CompletedTask;
    public virtual void SkipTypewriter(string widgetInstanceId) { }
    public virtual void SetVoice(string assetId) { }
    public virtual Task WaitForClickAsync(CancellationToken ct) => Task.CompletedTask;
    public virtual Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct) => Task.FromResult(0);
}
