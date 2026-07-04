namespace GalNet.Core.View;

/// <summary>
/// Runtime 与游戏 UI 的唯一契约接口。
/// </summary>
public interface IGameView
{
    // ── Layer 管理 ──
    void ShowLayer(string id, string assetId, float x, float y, float z = 0);
    void HideLayer(string id);
    void MoveLayer(string id, float x, float y, float z, float durationSec);

    // ── 控件实例管理 ──
    void ShowControl(string instanceId);
    void HideControl(string instanceId);
    void SetControlProperty(string instanceId, string property, string value);

    // ── 页面切换 ──
    /// <summary>
    /// 切换到指定页面实例。返回被点击的控件 instanceId，Runtime 据此决策。
    /// </summary>
    Task<string> ShowPageAsync(string screenInstanceId, CancellationToken ct);

    // ── 音频 ──
    void PlayAudio(string channel, string assetId, float volume, string mode, int times);
    void StopAudio(string channel);
    void PauseAudio(string channel);
    void ResumeAudio(string channel);
    void EnqueueAudio(string channel, string assetId, int times);
    void ConfigureAudioQueue(string channel, string onEnd, string onEmpty);

    // ── 视频 ──
    void PlayVideo(string assetId);
    void StopVideo();

    // ── 转场 / 特效 ──
    void ApplyTransition(string type, float durationSec);
    void ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters);
    void StopEffect(string effectId);

    // ── 异步操作 ──
    Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct);
    void SkipTypewriter(string widgetInstanceId);
    void SetVoice(string assetId);

    // ── 阻塞交互 ──
    Task WaitForClickAsync(CancellationToken ct);
    Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct);
}
