namespace GalNet.Core.View;

public interface IInteractionView
{
    Task WaitForClickAsync(CancellationToken ct);
    Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct);
}
