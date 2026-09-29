using GalNet.Core.Settings;
using GalNet.Core.Text;
using GalNet.Presentation.Abstractions.View;
using GalNet.Core.Scene;

namespace GalNet.Sample.Headless;

/// <summary>Basic interactive console adapters used by the official sample player.</summary>
internal sealed class ConsolePresentation : IDialoguePresenter, IChoicePresenter, ILayerPresenter, IAnimationPresenter, IEffectPresenter
{
    private readonly GameSettings _settings;
    private volatile bool _skipCurrentTypewriter;
    private volatile bool _instantTypewriter;
    private int _advancePromptActive;

    public ConsolePresentation(GameSettings settings) => _settings = settings;

    public event Action? AdvanceRequested;

    public void ShowLayer(LayerRenderRequest request) =>
        Console.WriteLine($"[Layer] show {request.HandleId}: {request.AssetId} ({request.Transform.X}, {request.Transform.Y}, {request.Z})");
    public void ReplaceLayer(string handleId, string assetId) => Console.WriteLine($"[Layer] replace {handleId}: {assetId}");
    public void HideLayer(string handleId) => Console.WriteLine($"[Layer] hide {handleId}");
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) =>
        Console.WriteLine($"[Layer] move {handleId}: ({transform.X}, {transform.Y}, {z}) in {durationSec}s");
    public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Animation] {request.PlaybackHandleId}: {request.HandleId}.{request.Property} -> {request.To}");
        return Task.FromResult(AnimationOutcome.Completed);
    }
    public Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Animation] plan {plan.PlaybackHandleId}: {plan.Tracks.Count} track(s), {plan.DurationFrames} frame(s)");
        return Task.FromResult(new AnimationPlanPlayResult { Outcome = AnimationOutcome.Completed });
    }
    public bool CompleteAnimationImmediately(string playbackHandleId)
    {
        Console.WriteLine($"[Animation] complete {playbackHandleId}");
        return true;
    }
    public Task StartEffectAsync(EffectRequest request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Effect] start {request.InstanceId}: {request.Id}{request.ProgramResource}");
        return Task.CompletedTask;
    }
    public Task StopEffectAsync(string instanceId, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Effect] stop {instanceId}");
        return Task.CompletedTask;
    }
    public void ShowDialogue() => Console.WriteLine("[Dialogue] show");
    public void HideDialogue() => Console.WriteLine("[Dialogue] hide");

    public async Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        _skipCurrentTypewriter = false;
        _instantTypewriter = false;
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
                            if (!_skipCurrentTypewriter && !_instantTypewriter && _settings.TextSpeed > 0)
                                await Task.Delay(TimeSpan.FromSeconds(1d / _settings.TextSpeed), ct);
                        }
                        break;
                    case RichTypewriterTokenKind.LineBreak:
                        Console.WriteLine();
                        break;
                    case RichTypewriterTokenKind.Delay when !_skipCurrentTypewriter && !_instantTypewriter:
                        await Task.Delay(token.DelayMilliseconds, ct);
                        break;
                    case RichTypewriterTokenKind.Instant:
                        _instantTypewriter = true;
                        break;
                    case RichTypewriterTokenKind.SkipBoundary:
                        _skipCurrentTypewriter = false;
                        break;
                }
            }
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public void SkipTypewriter(string widgetInstanceId) => _skipCurrentTypewriter = true;
    public void SetVoice(string assetId) => Console.WriteLine($"[Voice] {assetId}");

    public static async Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct)
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

    async Task IDialoguePresenter.PresentTextAsync(string speaker, string text, CancellationToken cancellationToken)
    {
        await StartTypewriter("default_dialogue", speaker, text, cancellationToken);
        _ = RequestAdvanceAsync(cancellationToken);
    }

    void IDialoguePresenter.SkipText() => SkipTypewriter("default_dialogue");

    Task<int> IChoicePresenter.ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken) =>
        WaitForChoiceAsync("default_choice", options.ToArray(), cancellationToken);

    private async Task RequestAdvanceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _advancePromptActive, 1) != 0)
            return;
        try
        {
            Console.Write("  >> ");
            if (await Task.Run(Console.ReadLine, cancellationToken) is null)
                throw new EndOfStreamException("Console input closed while waiting to advance.");
            AdvanceRequested?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            Interlocked.Exchange(ref _advancePromptActive, 0);
        }
    }
}
