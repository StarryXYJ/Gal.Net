using GalNet.Core.Settings;
using GalNet.Core.Text;
using GalNet.Core.View;
using GalNet.Core.Scene;

namespace GalNet.Sample.Headless;

/// <summary>Basic interactive console adapters used by the official sample player.</summary>
internal sealed class ConsolePresentation :
    ILayerView,
    IAnimationView,
    IControlView,
    IAudioView,
    IVideoView,
    ITransitionView,
    IEffectView,
    ITypewriterView,
    IInteractionView
{
    private readonly GameSettings _settings;
    private TaskCompletionSource _typewriterFinished = CompletedSource();
    private volatile bool _skipCurrentTypewriter;

    public ConsolePresentation(GameSettings settings) => _settings = settings;

    public void ShowLayer(LayerRenderRequest request) =>
        Console.WriteLine($"[Layer] show {request.HandleId}: {request.AssetId} ({request.Transform.X}, {request.Transform.Y}, {request.Z})");
    public void ReplaceLayer(string handleId, string assetId) => Console.WriteLine($"[Layer] replace {handleId}: {assetId}");
    public void HideLayer(string handleId) => Console.WriteLine($"[Layer] hide {handleId}");
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) =>
        Console.WriteLine($"[Layer] move {handleId}: ({transform.X}, {transform.Y}, {z}) in {durationSec}s");
    public async Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken ct)
    {
        Console.WriteLine($"[Animate] {request.HandleId}.{request.Property} -> {request.To} in {request.DurationSeconds}s");
        if (request.LoopMode == AnimationLoopMode.Loop) await Task.Delay(TimeSpan.FromSeconds(request.DurationSeconds), ct);
        return AnimationOutcome.Completed;
    }
    public async Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken ct)
    {
        Console.WriteLine($"[AnimationPlan] {plan.Tracks.Count} tracks, {plan.DurationFrames} frames @ {plan.FrameRate} FPS");
        if (plan.LoopMode == AnimationLoopMode.Loop) await Task.Delay(TimeSpan.FromSeconds(plan.DurationFrames / (double)plan.FrameRate), ct);
        return new AnimationPlanPlayResult { Outcome = AnimationOutcome.Completed, TrackOutcomes = plan.Tracks.ToDictionary(track => $"{track.HandleId}:{track.Property}", _ => AnimationOutcome.Completed) };
    }
    public bool CompleteAnimationImmediately(string playbackHandleId) => false;
    public bool SkipAnimationBatch() => false;
    public void ShowDialogue() => Console.WriteLine("[Dialogue] show");
    public void HideDialogue() => Console.WriteLine("[Dialogue] hide");
    public void PlayAudio(string channel, string assetId, float volume, string mode, int times) =>
        Console.WriteLine($"[Audio] play {channel}: {assetId} ({mode}, {times}x, volume {volume})");
    public void StopAudio(string channel) => Console.WriteLine($"[Audio] stop {channel}");
    public void PauseAudio(string channel) => Console.WriteLine($"[Audio] pause {channel}");
    public void ResumeAudio(string channel) => Console.WriteLine($"[Audio] resume {channel}");
    public void EnqueueAudio(string channel, string assetId, int times) => Console.WriteLine($"[Audio] queue {channel}: {assetId} ({times}x)");
    public void ConfigureAudioQueue(string channel, string onEnd, string onEmpty) { }
    public void PlayVideo(string assetId) => Console.WriteLine($"[Video] play {assetId}");
    public void StopVideo() => Console.WriteLine("[Video] stop");
    public Task PlayTransitionAsync(TransitionRequest request, CancellationToken ct)
    {
        Console.WriteLine($"[Transition] {request.Id}: {request.FromImageId} -> {request.ToImageId}, {request.Duration.TotalSeconds}s");
        return Task.CompletedTask;
    }

    public Task StartEffectAsync(EffectRequest request, CancellationToken ct)
    {
        Console.WriteLine($"[Effect] start {request.Id} ({request.InstanceId})");
        return Task.CompletedTask;
    }

    public Task StopEffectAsync(string instanceId, CancellationToken ct)
    {
        Console.WriteLine($"[Effect] stop {instanceId}");
        return Task.CompletedTask;
    }

    public async Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        _skipCurrentTypewriter = false;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _typewriterFinished = completion;
        try
        {
            if (!string.IsNullOrWhiteSpace(speaker)) Console.Write($"{speaker}: ");
            foreach (var token in RichTypewriterTextParser.Parse(text))
            {
                switch (token.Kind)
                {
                    case RichTypewriterTokenKind.Text:
                        foreach (var character in token.Text)
                        {
                            ct.ThrowIfCancellationRequested();
                            Console.Write(character);
                            if (!_skipCurrentTypewriter && _settings.TextSpeed > 0)
                                await Task.Delay(TimeSpan.FromSeconds(1d / _settings.TextSpeed), ct);
                        }
                        break;
                    case RichTypewriterTokenKind.LineBreak:
                        Console.WriteLine();
                        break;
                    case RichTypewriterTokenKind.Delay when !_skipCurrentTypewriter:
                        await Task.Delay(token.DelayMilliseconds, ct);
                        break;
                    case RichTypewriterTokenKind.Instant:
                        _skipCurrentTypewriter = true;
                        break;
                }
            }
            Console.WriteLine();
            completion.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(ct);
            throw;
        }
    }

    public void SkipTypewriter(string widgetInstanceId) => _skipCurrentTypewriter = true;
    public void SetVoice(string assetId) => Console.WriteLine($"[Voice] {assetId}");

    public async Task WaitForClickAsync(CancellationToken ct)
    {
        await _typewriterFinished.Task.WaitAsync(ct);
        Console.Write("  >> ");
        if (await Task.Run(Console.ReadLine, ct) is null)
            throw new EndOfStreamException("Console input closed while waiting to advance.");
    }

    public async Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct)
    {
        for (var index = 0; index < options.Length; index++)
            Console.WriteLine($"  {index + 1}. {options[index]}");

        while (true)
        {
            Console.Write("  Select: ");
            var input = await Task.Run(Console.ReadLine, ct);
            if (input is null)
                throw new EndOfStreamException("Console input closed while waiting for a choice.");
            if (int.TryParse(input, out var selected) && selected >= 1 && selected <= options.Length) return selected - 1;
            Console.WriteLine($"  Enter a number from 1 to {options.Length}.");
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult();
        return source;
    }
}
