namespace GalNet.Core.View;

public interface ITypewriterView
{
    Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct);
    void SkipTypewriter(string widgetInstanceId);
    void SetVoice(string assetId);
}
