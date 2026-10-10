namespace GalNet.Presentation.Abstractions.View;

/// <summary>Presentation operations used by dialogue primitive instances.</summary>
public interface IDialoguePresenter
{
    void ShowDialogue();
    void HideDialogue();
    Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken);
    void SkipText();
}
