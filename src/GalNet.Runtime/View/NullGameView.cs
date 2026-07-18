using GalNet.Core.View;

namespace GalNet.Runtime.View;

/// <summary>
/// NullGameView —— Headless 测试用的 IGameView 实现。
/// 所有操作输出到控制台或静默忽略。
/// </summary>
public class NullGameView : IGameView
{
    private readonly bool _verbose;

    public NullGameView(bool verbose = true)
    {
        _verbose = verbose;
    }

    public void ShowLayer(string id, string assetId, float x, float y, float z = 0)
    {
        if (_verbose)
            Console.WriteLine($"[Layer] show id={id}, asset={assetId}, pos=({x},{y},{z})");
    }

    public void HideLayer(string id)
    {
        if (_verbose)
            Console.WriteLine($"[Layer] hide id={id}");
    }

    public void MoveLayer(string id, float x, float y, float z, float durationSec)
    {
        if (_verbose)
            Console.WriteLine($"[Layer] move id={id} to ({x},{y},{z}) in {durationSec}s");
    }

    public void ShowDialogue()
    {
        if (_verbose)
            Console.WriteLine("[Dialogue] show");
    }

    public void HideDialogue()
    {
        if (_verbose)
            Console.WriteLine("[Dialogue] hide");
    }

    public Task<string> ShowPageAsync(string screenInstanceId, CancellationToken ct)
    {
        if (_verbose)
            Console.WriteLine($"[Screen] show id={screenInstanceId}");
        return Task.FromResult("");
    }

    public void PlayAudio(string channel, string assetId, float volume, string mode, int times)
    {
        if (_verbose)
            Console.WriteLine($"[Audio] play channel={channel}, asset={assetId}, vol={volume}, mode={mode}, times={times}");
    }

    public void StopAudio(string channel)
    {
        if (_verbose)
            Console.WriteLine($"[Audio] stop channel={channel}");
    }

    public void PauseAudio(string channel)
    {
        if (_verbose)
            Console.WriteLine($"[Audio] pause channel={channel}");
    }

    public void ResumeAudio(string channel)
    {
        if (_verbose)
            Console.WriteLine($"[Audio] resume channel={channel}");
    }

    public void EnqueueAudio(string channel, string assetId, int times)
    {
        if (_verbose)
            Console.WriteLine($"[Audio] enqueue channel={channel}, asset={assetId}, times={times}");
    }

    public void ConfigureAudioQueue(string channel, string onEnd, string onEmpty)
    {
        if (_verbose)
            Console.WriteLine($"[Audio] configure queue channel={channel}, onEnd={onEnd}, onEmpty={onEmpty}");
    }

    public void PlayVideo(string assetId)
    {
        if (_verbose)
            Console.WriteLine($"[Video] play asset={assetId}");
    }

    public void StopVideo()
    {
        if (_verbose)
            Console.WriteLine($"[Video] stop");
    }

    public void ApplyTransition(string type, float durationSec)
    {
        if (_verbose)
            Console.WriteLine($"[Transition] type={type}, duration={durationSec}s");
    }

    public void ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters)
    {
        if (_verbose)
            Console.WriteLine($"[Effect] type={effectType}, params={string.Join(", ", parameters.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    public void StopEffect(string effectId)
    {
        if (_verbose)
            Console.WriteLine($"[Effect] stop id={effectId}");
    }

    public virtual Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        if (_verbose)
            Console.WriteLine($"[Text] speaker={speaker}, text=\"{text}\"");
        return Task.CompletedTask;
    }

    public virtual void SkipTypewriter(string widgetInstanceId)
    {
        if (_verbose)
            Console.WriteLine("[Text] skip typewriter");
    }

    public void SetVoice(string assetId)
    {
        if (_verbose)
            Console.WriteLine($"[Voice] asset={assetId}");
    }

    public virtual async Task WaitForClickAsync(CancellationToken ct)
    {
        if (_verbose)
            Console.WriteLine("[Wait] click to continue...");
        await Task.Delay(100, ct); // Simulate instant click in headless
    }

    public virtual Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct)
    {
        if (_verbose)
        {
            Console.WriteLine($"[Choice] options:");
            for (var i = 0; i < options.Length; i++)
                Console.WriteLine($"  [{i}] {options[i]}");
            Console.WriteLine("[Choice] auto-selecting 0");
        }
        return Task.FromResult(0); // Auto-select first
    }
}
