namespace GalNet.Presentation.Abstractions.View;

/// <summary>Presentation operations used by explicit NVL primitives.</summary>
public interface INvlPresenter
{
    void ShowNvl();
    void HideNvl();
    void ClearNvl();
    Task AppendNvlTextAsync(string text, CancellationToken cancellationToken);
    void SkipNvlText();
    Task AppendNvlTextImmediatelyAsync(string text, CancellationToken cancellationToken);
    void RevealAllNvlText();
}
