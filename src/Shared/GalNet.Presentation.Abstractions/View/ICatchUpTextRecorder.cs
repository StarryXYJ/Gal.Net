namespace GalNet.Presentation.Abstractions.View;

/// <summary>Optional text projection used by hosts that rebuild dialogue and NVL after catch-up.</summary>
public interface ICatchUpTextRecorder
{
    void RecordDialogue(string speaker, string text);
    void RecordNvlParagraph(string text);
}
